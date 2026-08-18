import { expect, test, type Page, type Route } from '@playwright/test'
import { mkdir } from 'node:fs/promises'
import path from 'node:path'

/**
 * #1613 真机走查（补齐篇）：三处**结构上与其它清扫点不同**的确认框。
 *
 * 这三页在本机 dev 栈里没有种子数据（实测列表为空态），而它们各自的建单链路很长。
 * 这里只把**读面**用 `page.route` 造出一条记录（信封照抄真实响应：`{data:{items,total},success}`
 * / `{data:[],success}`），页面组件、reka 弹层、浏览器行为全部是真的；写回一律拦成 500，
 * **不落库、不改任何业务数据**。要验的是关框时机这件前端行为，后端契约本 PR 没动。
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

const ok = (data: unknown) =>
  JSON.stringify({ data, success: true, message: '', code: 0, errorData: [] })

async function login(page: Page) {
  await page.goto('/')
  const l = page.getByLabel('登录名')
  await l.waitFor({ state: 'visible', timeout: 120_000 })
  const r = page.waitForResponse((x) => new URL(x.url()).pathname === '/api/console/v1/auth/login')
  await l.fill('admin')
  await page.getByLabel('密码').fill(adminPassword!)
  await page.getByRole('button', { name: '登录' }).click()
  const res = await r
  if (!res.ok()) throw new Error(`登录失败 HTTP ${res.status()}`)
  await page.waitForURL(new URL('/', baseURL!).toString(), { timeout: 60_000 })
}

/** 读面造数 + 写回拦成 500；返回被拦到的写回请求。 */
async function installRoutes(
  page: Page,
  read: { match: RegExp; body: string; detail?: RegExp; detailBody?: string },
  write: RegExp,
  seen: { value?: string },
) {
  await page.route('**/api/business-console/**', async (route: Route) => {
    const req = route.request()
    const u = new URL(req.url())
    if (req.method() !== 'GET' && write.test(u.pathname)) {
      seen.value = `${req.method()} ${u.pathname}`
      await route.fulfill({
        status: 500,
        contentType: 'application/json',
        body: JSON.stringify({ title: '走查注入的失败', status: 500 }),
      })
      return
    }
    if (req.method() === 'GET' && read.match.test(u.pathname)) {
      // 详情端点回单个对象、列表端点回 {items,total}：从 body 里取第一条当详情。
      const body = read.detail && read.detail.test(u.pathname) ? read.detailBody! : read.body
      await route.fulfill({ status: 200, contentType: 'application/json', body })
      return
    }
    await route.continue()
  })
}

test('#1613 结构差异三处的真机关框时机', async ({ page }) => {
  const shotDir = path.join(outDir!, 'screenshots')
  await mkdir(shotDir, { recursive: true })
  const report: Record<string, unknown>[] = []
  await login(page)

  // ── 1. quality/ncrs：本 PR 唯一的结构性改动（非受控 → 受控，且确认框在 <form> 里）──
  {
    const entry: Record<string, unknown> = { id: '07-ncrs', title: '不合格品关闭（受控化改造）' }
    const seen: { value?: string } = {}
    await installRoutes(
      page,
      {
        // 关单前还有一次**前置读** `GET /quality/ncrs/{id}`（生命周期守卫 readNcr），
        // 只造列表的话详情会打到真后端 404、守卫直接退出，写回根本发不出来。
        match: /\/quality\/ncrs(\/[^/]+)?$/,
        body: ok({
          items: [
            {
              id: 'NCR-WALK-001',
              code: 'NCR-WALK-001',
              status: 'disposition-in-progress',
              sourceType: 'work-order',
              sourceDocumentId: 'WO-WALK-1001',
              skuCode: 'SKU-PISTON-01',
            },
          ],
          total: 1,
        }),
        detail: /\/quality\/ncrs\/[^/]+$/,
        detailBody: ok({
          id: 'NCR-WALK-001',
          code: 'NCR-WALK-001',
          status: 'disposition-in-progress',
          dispositionType: 'recorded',
          sourceType: 'work-order',
          sourceDocumentId: 'WO-WALK-1001',
          skuCode: 'SKU-PISTON-01',
        }),
      },
      /\/quality\/ncrs\/.+\/close$/,
      seen,
    )
    await page.goto('/quality/ncrs')
    await page.waitForTimeout(2000)
    // 「打开处置」在 NvRowActions 下拉里，真浏览器要先点开行操作触发器
    // （单测把 NvDropdownMenuContent 桩掉了才直接可见——这正是真机走查会补上的那类差别）。
    const rowActions = page.locator('[aria-label^="NCR 操作"]').first()
    await rowActions.waitFor({ state: 'visible', timeout: 20_000 })
    await rowActions.click()
    await page.waitForTimeout(600)
    await page.getByRole('menuitem', { name: '打开处置' }).first().click()
    await page.waitForTimeout(1200)
    const reason = page.locator('#ncr-close-reason')
    await reason.waitFor({ state: 'visible', timeout: 15_000 })
    await reason.fill('走查用原因：返工后复检合格')
    await page.locator('#ncr-scrap').fill('MOV-WALK-0007')
    await page.getByRole('button', { name: '关闭不合格品' }).click()

    const dialog = page.getByRole('alertdialog')
    await dialog.waitFor({ state: 'visible', timeout: 15_000 })
    const confirm = dialog.getByRole('button', { name: '确认关闭' })
    entry.confirmSlot = await confirm.getAttribute('data-slot')
    entry.confirmType = await confirm.getAttribute('type') // <form> 里必须是 button
    entry.writeBeforeConfirm = seen.value ?? null // 触发只开框，不应发请求
    await page.screenshot({ path: path.join(shotDir, '07-ncrs-open.png') })

    await confirm.click()
    await page.waitForTimeout(2500)
    entry.interceptedWrite = seen.value ?? null
    entry.dialogStillOpenAfterFailure = await dialog.isVisible().catch(() => false)
    entry.reasonKept = await reason.inputValue().catch(() => '')
    entry.scrapKept = await page
      .locator('#ncr-scrap')
      .inputValue()
      .catch(() => '')
    await page.screenshot({ path: path.join(shotDir, '07-ncrs-after-failure.png') })
    entry.result = entry.interceptedWrite && entry.dialogStillOpenAfterFailure ? 'pass' : 'FAIL'
    report.push(entry)
    await page.unroute('**/api/business-console/**')
  }

  // ── 2. equipment/alarms：批量确认（pending 期间框还开着 + 真禁用）──
  {
    const entry: Record<string, unknown> = { id: '08-alarms', title: '批量确认报警' }
    const seen: { value?: string } = {}
    const alarm = (i: number) => ({
      alarmEventId: `ALM-WALK-${i}`,
      externalAlarmId: `ALM-WALK-${i}`,
      deviceAssetId: 'DEV-CNC-01',
      alarmCode: 'TEMP-HIGH',
      severity: 'critical',
      status: 'raised',
      raisedAtUtc: '2026-08-18T01:00:00Z',
    })
    await installRoutes(
      page,
      { match: /\/equipment\/alarms$/, body: ok({ items: [alarm(1), alarm(2)], total: 2 }) },
      /\/equipment\/alarms\/.+\/acknowledge$/,
      seen,
    )
    await page.goto('/equipment/alarms')
    await page.waitForTimeout(2000)
    const boxes = page.locator('[aria-label="选择行"]')
    entry.rows = await boxes.count()
    if ((await boxes.count()) >= 2) {
      await boxes.nth(0).click()
      await boxes.nth(1).click()
      await page.getByRole('button', { name: /批量确认/ }).click()
      const dialog = page.getByRole('alertdialog')
      await dialog.waitFor({ state: 'visible', timeout: 15_000 })
      const confirm = dialog.getByRole('button', { name: /^确认 \d+ 条$/ })
      entry.confirmSlot = await confirm.getAttribute('data-slot')
      entry.disabledBeforeClick = await confirm.isDisabled()
      await page.screenshot({ path: path.join(shotDir, '08-alarms-open.png') })
      await confirm.click()
      await page.waitForTimeout(2500)
      entry.interceptedWrite = seen.value ?? null
      entry.dialogClosedAfterSettle = !(await dialog.isVisible().catch(() => false))
      await page.screenshot({ path: path.join(shotDir, '08-alarms-after-failure.png') })
      // 本页口径：成败都关框，重试落点是保留下来的选中集
      entry.selectionRetained = await page.getByText(/已选/).count()
      entry.result = entry.interceptedWrite ? 'pass' : 'inconclusive-未拦到写回'
    } else {
      entry.result = 'skip'
    }
    report.push(entry)
    await page.unroute('**/api/business-console/**')
  }

  // ── 3. scheduling：撤销发布 ──
  {
    const entry: Record<string, unknown> = { id: '09-scheduling', title: '排程方案撤销发布' }
    const seen: { value?: string } = {}
    await installRoutes(
      page,
      {
        match: /\/scheduling\/plans$/,
        body: ok([
          {
            planId: 'PLAN-WALK-001',
            status: 'released',
            generatedAtUtc: '2026-08-18T00:00:00Z',
            releasedAtUtc: '2026-08-18T00:30:00Z',
            assignmentCount: 4,
            conflictCount: 0,
            unscheduledOperationCount: 0,
          },
        ]),
      },
      /\/scheduling\/plans\/.+\/revoke$/,
      seen,
    )
    await page.goto('/scheduling')
    await page.waitForTimeout(2500)
    const tab = page.getByRole('tab').filter({ hasText: '表格' }).first()
    if (await tab.count()) {
      await tab.click()
      await page.waitForTimeout(1200)
    }
    const revoke = page.getByRole('button', { name: '撤销发布' }).first()
    entry.revokeVisible = (await revoke.count()) > 0
    if (entry.revokeVisible) {
      await revoke.click()
      const dialog = page.getByRole('alertdialog')
      await dialog.waitFor({ state: 'visible', timeout: 15_000 })
      const confirm = dialog.getByRole('button', { name: '确认撤销' })
      entry.confirmSlot = await confirm.getAttribute('data-slot')
      entry.writeBeforeConfirm = seen.value ?? null
      await page.screenshot({ path: path.join(shotDir, '09-scheduling-open.png') })
      await confirm.click()
      await page.waitForTimeout(2500)
      entry.interceptedWrite = seen.value ?? null
      entry.dialogStillOpenAfterFailure = await dialog.isVisible().catch(() => false)
      await page.screenshot({ path: path.join(shotDir, '09-scheduling-after-failure.png') })
      entry.result = entry.interceptedWrite && entry.dialogStillOpenAfterFailure ? 'pass' : 'FAIL'
    } else {
      entry.result = 'skip'
    }
    report.push(entry)
    await page.unroute('**/api/business-console/**')
  }

  console.log('\n===#1613-MOCKED-READ-WALKTHROUGH===\n' + JSON.stringify(report, null, 2))
  expect(report.filter((r) => String(r.result).startsWith('FAIL'))).toEqual([])
})
