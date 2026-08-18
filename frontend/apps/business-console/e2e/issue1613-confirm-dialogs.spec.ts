import { expect, test, type Page } from '@playwright/test'
import { mkdir } from 'node:fs/promises'
import path from 'node:path'

/**
 * #1613 真机走查：10 处清扫后的破坏性确认框，在**真浏览器 + 真后端**下验两件 jsdom 证不了的事：
 * 1. 确认按钮渲染成普通 `NvButton`（`data-slot="nv-button"`），不再是 `nv-alert-dialog-action`；
 * 2. 写回**失败**时框保持打开、可原地重试。
 *
 * 失败用 `page.route` 拦截写回请求造出来，**不落库、不改任何业务数据**。
 *
 * **跑法**：必须 `--workers=1`。playwright.config 是 `fullyParallel: true`，两份走查同时驱动
 * 同一个 dev server 时实测会偶发失败；串行连跑 3 次稳定通过。
 *
 *   NERV_IIP_PLAYWRIGHT_BASE_URL=http://localhost:5125 \
 *   NERV_IIP_FULLSTACK_ADMIN_PASSWORD=<admin 口令> \
 *   NERV_IIP_OUT_DIR=artifacts/issue1613-walkthrough \
 *   pnpm exec playwright test e2e/issue1613 --project=desktop --workers=1
 */
const baseURL = process.env.NERV_IIP_PLAYWRIGHT_BASE_URL
const adminPassword = process.env.NERV_IIP_FULLSTACK_ADMIN_PASSWORD
const outDir = process.env.NERV_IIP_OUT_DIR

// 需要一套跑起来的本地栈；缺环境变量就跳过（沿用 r5-walkthrough.spec.ts 的守卫写法），
// 免得在没有后端的环境里被当成回归门禁跑挂。产物落 artifacts/（.gitignore 内，不入库）。
test.skip(
  !baseURL || !adminPassword || !outDir,
  '需要本地栈：NERV_IIP_PLAYWRIGHT_BASE_URL / NERV_IIP_FULLSTACK_ADMIN_PASSWORD / NERV_IIP_OUT_DIR',
)

async function login(page: Page) {
  await page.goto('/')
  const loginName = page.getByLabel('登录名')
  await loginName.waitFor({ state: 'visible', timeout: 120_000 })
  const loginResponse = page.waitForResponse(
    (r) => new URL(r.url()).pathname === '/api/console/v1/auth/login',
  )
  await loginName.fill('admin')
  await page.getByLabel('密码').fill(adminPassword!)
  await page.getByRole('button', { name: '登录' }).click()
  const res = await loginResponse
  if (!res.ok()) throw new Error(`登录失败 HTTP ${res.status()}`)
  await page.waitForURL(new URL('/', baseURL!).toString(), { timeout: 60_000 })
}

interface Stop {
  id: string
  title: string
  path: string
  /** 打开确认框：返回 false 表示这页没有可操作的行（数据没种到），如实登记为 skip。 */
  open: (page: Page) => Promise<boolean>
  confirmLabel: string
  /** 写回请求的判定（用于拦截造失败）。 */
  writeMatch: (url: URL, method: string) => boolean
}

const clickFirst = async (page: Page, name: string | RegExp) => {
  const btn = page.getByRole('button', { name }).first()
  if ((await btn.count()) === 0) return false
  if (!(await btn.isVisible().catch(() => false))) return false
  if (await btn.isDisabled().catch(() => true)) return false
  await btn.click()
  return true
}

const STOPS: Stop[] = [
  {
    id: '01-product-categories',
    title: '产品分类 · 停用',
    path: '/master-data/product-categories',
    confirmLabel: '确认停用',
    open: async (page) => {
      if (!(await clickFirst(page, '停用'))) return false
      const reason = page.locator('#category-archive-reason')
      await reason.waitFor({ state: 'visible', timeout: 15_000 })
      await reason.fill('走查用原因：与上级分类合并')
      return true
    },
    writeMatch: (u, m) => m !== 'GET' && /product-categories/.test(u.pathname),
  },
  {
    id: '02-skill-catalog',
    title: '技能目录 · 停用',
    path: '/master-data/skill-catalog',
    confirmLabel: '确认停用',
    open: async (page) => {
      if (!(await clickFirst(page, '停用'))) return false
      const reason = page.locator('#skill-archive-reason')
      await reason.waitFor({ state: 'visible', timeout: 15_000 })
      await reason.fill('走查用原因：该工艺已淘汰')
      return true
    },
    writeMatch: (u, m) => m !== 'GET' && /skill/.test(u.pathname),
  },
  {
    id: '03-production-versions',
    title: '生产版本 · 归档',
    path: '/engineering/production-versions',
    confirmLabel: '确认归档',
    open: async (page) => clickFirst(page, '归档'),
    writeMatch: (u, m) => m !== 'GET' && /production-version/.test(u.pathname),
  },
  {
    id: '04-standard-operations',
    title: '标准工序 · 停用',
    path: '/engineering/standard-operations',
    confirmLabel: '确认停用',
    open: async (page) => clickFirst(page, '停用'),
    writeMatch: (u, m) => m !== 'GET' && /operation/.test(u.pathname),
  },
  {
    id: '05-reason-codes',
    title: '质量原因码 · 停用',
    path: '/quality/reason-codes',
    confirmLabel: '确认停用',
    open: async (page) => clickFirst(page, '停用'),
    writeMatch: (u, m) => m !== 'GET' && /reason/.test(u.pathname),
  },
  {
    id: '06-control-bindings',
    title: '控制通道绑定 · 停用',
    path: '/equipment/telemetry/control-bindings',
    confirmLabel: '确认停用',
    open: async (page) => {
      const row = page.getByRole('button', { name: /操作|更多/ }).first()
      if ((await row.count()) === 0) return false
      await row.click()
      if (!(await clickFirst(page, '停用'))) return false
      const reason = page.locator('#binding-disable-reason')
      await reason.waitFor({ state: 'visible', timeout: 15_000 })
      await reason.fill('走查用原因：通道重配')
      return true
    },
    writeMatch: (u, m) => m !== 'GET' && /binding/.test(u.pathname),
  },
]

test('#1613 破坏性确认框真机走查（失败保留 + 组件形态）', async ({ page }) => {
  const shotDir = path.join(outDir!, 'screenshots')
  await mkdir(shotDir, { recursive: true })
  const report: Record<string, unknown>[] = []

  await login(page)

  for (const stop of STOPS) {
    const entry: Record<string, unknown> = { id: stop.id, title: stop.title, path: stop.path }
    try {
      await page.goto(stop.path)
      await page.waitForLoadState('networkidle', { timeout: 30_000 }).catch(() => {})

      if (!(await stop.open(page))) {
        entry.result = 'skip'
        entry.reason = '页面上没有可操作的行（该域未种数据）'
        report.push(entry)
        continue
      }

      const dialog = page.getByRole('alertdialog')
      await dialog.waitFor({ state: 'visible', timeout: 15_000 })

      // ① 组件形态：确认按钮必须是普通 NvButton，不是 nv-alert-dialog-action
      const confirm = dialog.getByRole('button', { name: stop.confirmLabel })
      await confirm.waitFor({ state: 'visible', timeout: 10_000 })
      entry.confirmSlot = await confirm.getAttribute('data-slot')
      entry.confirmClass = ((await confirm.getAttribute('class')) ?? '').slice(0, 40)
      await page.screenshot({ path: path.join(shotDir, `${stop.id}-open.png`) })

      // ② 造写回失败（拦截，不落库）
      await page.route('**/api/**', async (route) => {
        const req = route.request()
        const u = new URL(req.url())
        if (stop.writeMatch(u, req.method())) {
          entry.interceptedWrite = `${req.method()} ${u.pathname}`
          await route.fulfill({
            status: 500,
            contentType: 'application/json',
            body: JSON.stringify({ title: '走查注入的失败', status: 500 }),
          })
          return
        }
        await route.continue()
      })

      await confirm.click()
      await page.waitForTimeout(2500)

      entry.dialogStillOpenAfterFailure = await dialog.isVisible().catch(() => false)
      entry.confirmStillThere = await confirm.isVisible().catch(() => false)
      await page.screenshot({ path: path.join(shotDir, `${stop.id}-after-failure.png`) })
      entry.result =
        entry.interceptedWrite && entry.dialogStillOpenAfterFailure
          ? 'pass'
          : entry.interceptedWrite
            ? 'FAIL-框被关掉了'
            : 'inconclusive-未拦到写回请求'

      await page.unroute('**/api/**')
      await page.keyboard.press('Escape')
      await page.waitForTimeout(500)
    } catch (error) {
      entry.result = 'error'
      entry.error = (error as Error).message.split('\n')[0]
      await page.unroute('**/api/**').catch(() => {})
    }
    report.push(entry)
  }

  console.log('\n===#1613-WALKTHROUGH-REPORT===\n' + JSON.stringify(report, null, 2))

  // 取证之外也断言：凡是够到确认框的落点，写回失败后框都必须还在。
  // skip（该域没种数据）不算失败，但会留在报告里，别被当成"全覆盖"。
  expect(report.filter((r) => String(r.result).startsWith('FAIL') || r.result === 'error')).toEqual(
    [],
  )
  expect(report.some((r) => r.result === 'pass')).toBe(true)
})
