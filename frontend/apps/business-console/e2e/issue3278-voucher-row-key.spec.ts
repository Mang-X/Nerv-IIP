import { expect, test, type Page } from '@playwright/test'
import { mkdir } from 'node:fs/promises'
import path from 'node:path'
import { requireBrowserEvidenceOutputDir } from '../playwright.config'

/**
 * #3278 S4 真机走查：会计凭证表的 `row-key` 钉在稳定 id 上之后，来回翻页不再长出多余的行。
 *
 * ── 为什么必须到浏览器里看 ──
 * 缺陷的可达路径带着一个**只有真运行时才有**的前提：`NvDataTable` 只要 `loading` 为真就整体
 * 切成骨架行，数据行被卸载重挂，于是「第一次向前翻页」根本不把两批数据交给 keyed diff——
 * 缺陷被加载态盖住了。真正暴露它的是**回到访问过的页**：pinia-colada 命中缓存直接给出数据、
 * 不经过加载态，表体在同一次 patch 里从上一批换成下一批。
 * 这个前提在 jsdom 用例里是手写的（桩里 `pending` 恒为 false），只有浏览器能证明它真的成立。
 *
 * ── 旧写法（`r.voucherNo ?? '凭证'`）在本用例路径上的实测 ──
 * 第 1 页 3 行 → 第 2 页 3 行 → 回第 1 页 **4 行** → 再到第 2 页 **5 行**，多出来的是上一批
 * 残留的凭证行，并且**每来回一次就多一行**；同时浏览器打出两条
 * `[Vue warn]: Duplicate keys found during update: "凭证"`。
 * 在财务页上这等于把不存在的凭证摆进列表，且表体金额与上方「借贷合计」互相矛盾。
 *
 * ── 为什么用 `page.route` 造数而不是连真栈 ──
 * 要触发的输入形态是「同一页里部分凭证缺凭证号」，真栈上不一定造得出来；而缺陷完全落在前端
 * 渲染层，后端契约本 PR 没动。浏览器、Vue、NvDataTable、分页交互全部是真的，只有 HTTP 响应
 * 是桩。沿用 `e2e/issue2386-machine-overhead.spec.ts` 的免登录 + `/api/` 全打桩姿势。
 *
 * ── ⚠️ 这个文件不是回归护栏 ──
 * 它**不在任何 CI job 上跑**：`ci.yml:2116` 是全仓唯一一处 playwright 调用，按文件名只点名
 * `e2e/issue1974-tooling-visual.spec.ts`，并被 `scripts/tests/ci-impact-plan.Tests.ps1:253` 钉成契约；
 * 27 个 e2e spec 里只有那 1 个进 CI。所以本 PR 的**回归保护实际由 vitest 承担**
 * （`src/pages/erp/finance-voucher-row-key.test.ts`），这个 spec 只是可复现的走查配方。
 * 这是仓库现状，不是本 PR 引入的。
 *
 * ── 覆盖边界 ──
 * 只覆盖 `/erp/finance/vouchers`。`/erp/finance` 的「最近凭证」表没有分页、刷新必经加载态，
 * 浏览器层当前**造不出**同样的换行时机；那一页的 row-key 契约由
 * `src/pages/erp/finance-voucher-row-key.test.ts` 覆盖。
 *
 * 跑法：
 *   NERV_IIP_OUT_DIR=artifacts/issue3278-s4-walkthrough \
 *   pnpm -C frontend/apps/business-console exec playwright test \
 *     e2e/issue3278-voucher-row-key.spec.ts --project=desktop --workers=1
 */

const principal = {
  principalId: 'finance-01',
  principalType: 'User',
  loginName: 'finance.accountant',
  organizationId: 'org-001',
  environmentId: 'env-dev',
  permissionVersion: 1,
  permissionCodes: ['business.erp.finance.read'],
}
const session = {
  principal,
  accessToken: 'ui-contract-access',
  refreshToken: 'ui-contract-refresh',
  sessionId: 'ui-contract-session',
  expiresAtUtc: '2099-01-01T00:00:00Z',
}

/**
 * 一张凭证；`voucherNo` 传 null 表示服务端没给出凭证号。
 *
 * ⚠️ 这是**当前后端契约产不出**的输入（域侧 `ErpText.Required` 拒空、`voucher_no` 列 NOT NULL、
 * `(org, env, voucher_no)` 无 filter 唯一索引）。刻意选它，是因为本用例要钉的是**渲染层不变式**：
 * 「row-key 撞了会怎样」。真实可达路径见文件头与 `shared.ts` 里 `stableRowKey` 的注释。
 */
function voucher(id: string, voucherNo: string | null, amount: number, day: number) {
  return {
    id,
    voucherNo,
    postingDate: `2026-03-0${day}T00:00:00Z`,
    status: 'POSTED',
    totalDebitAmount: amount,
    totalCreditAmount: amount,
    postedAtUtc: `2026-03-0${day}T02:00:00Z`,
    lines: [],
  }
}

// 两批都「部分缺号」，且首尾凭证号对不齐——正好落进 Vue keyed diff 的乱序分支。
const PAGE_1 = [
  voucher('v-1', null, 111, 1),
  voucher('v-2', null, 222, 2),
  voucher('v-3', 'JV-2026-0003', 333, 3),
]
const PAGE_2 = [
  voucher('v-4', 'JV-2026-0004', 444, 4),
  voucher('v-5', null, 555, 5),
  voucher('v-6', null, 666, 6),
]

async function installStubs(page: Page) {
  await page.addInitScript(
    (stored) => localStorage.setItem('nerv-iip.business-console.auth', JSON.stringify(stored)),
    session,
  )
  // 只拦真正的后端路径；用 `**/api/**` 这类通配会把 Vite 的模块请求一起吃掉，页面会白屏。
  await page.route(
    (url) => url.pathname.startsWith('/api/'),
    async (route) => {
      const url = new URL(route.request().url())
      let data: unknown = { items: [], total: 0 }
      if (url.pathname.endsWith('/auth/refresh')) data = session
      else if (url.pathname.endsWith('/auth/me')) data = principal
      else if (url.pathname.endsWith('/erp/finance/vouchers')) {
        const skip = Number(url.searchParams.get('skip') ?? '0')
        data = { items: skip > 0 ? PAGE_2 : PAGE_1, total: 26 }
      }
      await route.fulfill({ json: { success: true, data, code: 0, message: '' } })
    },
  )
}

test('会计凭证页来回翻页不会长出多余的凭证行', async ({ page }) => {
  const duplicateKeyWarnings: string[] = []
  page.on('console', (message) => {
    if (message.text().includes('Duplicate keys')) duplicateKeyWarnings.push(message.text())
  })
  await installStubs(page)

  const output = requireBrowserEvidenceOutputDir()
  await mkdir(output, { recursive: true })
  const rows = page.locator('tbody tr')
  const shot = (name: string) =>
    page.screenshot({ path: path.join(output, `issue3278-s4-${name}.png`), fullPage: true })

  await page.goto('/erp/finance/vouchers')
  await expect(page.getByText('¥111.00').first()).toBeVisible()
  await expect(rows).toHaveCount(PAGE_1.length)
  await shot('p1')

  await page.getByRole('button', { name: '第 2 页' }).click()
  await expect(page.getByText('JV-2026-0004')).toBeVisible()
  await expect(rows).toHaveCount(PAGE_2.length)
  await shot('p2')

  // 回到第 1 页：命中缓存、不经过加载态——旧写法在这一步开始多渲染行。
  await page.getByRole('button', { name: '第 1 页' }).click()
  await expect(page.getByText('JV-2026-0003')).toBeVisible()
  await expect(rows).toHaveCount(PAGE_1.length)
  await shot('p1-back')

  // 再翻一次：旧写法下多出来的行会**累积**（3 → 4 → 5）。
  await page.getByRole('button', { name: '第 2 页' }).click()
  await expect(page.getByText('JV-2026-0004')).toBeVisible()
  await expect(rows).toHaveCount(PAGE_2.length)
  await shot('p2-again')

  expect(duplicateKeyWarnings).toEqual([])
})
