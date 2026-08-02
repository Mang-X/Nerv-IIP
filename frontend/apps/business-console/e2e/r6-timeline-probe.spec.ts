/**
 * 履约追踪的**数据可演示性**探针（只读，不点任何落库动作）。
 *
 * 起因：第六轮取证拍到销售订单首行 SO-DEMO-001 的履约链十一个节点全是
 * 「尚未产生 / 尚未建立关联」。需要分清两种情况：
 *   (a) 这是"新单待推进"的设计，别的单点开就有完整链路 → 演示挑对单即可
 *   (b) 没有任何单能点出完整链路 → 这个功能在演示里就是个空壳
 * 所以横向扫前若干张单，逐张记下各节点是 established 还是 pending。
 */
import { test, type Page } from '@playwright/test'
import { writeFile } from 'node:fs/promises'

const baseURL = process.env.NERV_IIP_PLAYWRIGHT_BASE_URL
const adminPassword = process.env.NERV_IIP_FULLSTACK_ADMIN_PASSWORD
const outFile = process.env.NERV_IIP_R6_PROBE_OUT

test.skip(!baseURL || !adminPassword || !outFile, 'requires a running leader-demo stack')
test.setTimeout(20 * 60 * 1000)

async function login(page: Page) {
  await page.goto('/')
  await page.getByLabel('登录名').waitFor({ state: 'visible', timeout: 120_000 })
  await page.getByLabel('登录名').fill('admin')
  await page.getByLabel('密码').fill(adminPassword!)
  await page.getByRole('button', { name: '登录' }).click()
  await page.waitForURL(new URL('/', baseURL!).toString(), { timeout: 60_000 })
}

test('履约链数据可演示性', async ({ page }) => {
  test.skip(test.info().project.name !== 'desktop', '只在 desktop project 探测')
  page.setDefaultTimeout(15_000)
  await login(page)
  await page.goto('/erp/sales/orders', { waitUntil: 'networkidle', timeout: 60_000 })
  await page.waitForTimeout(3_000)

  const rows = await page.locator('tbody tr').count()
  const findings: unknown[] = []

  for (let i = 0; i < Math.min(rows, 8); i += 1) {
    const tr = page.locator('tbody tr').nth(i)
    const orderNo = (
      await tr
        .locator('td')
        .first()
        .innerText()
        .catch(() => '?')
    ).trim()
    const btn = tr.getByRole('button', { name: /履约追踪/ }).first()
    if (!(await btn.isVisible().catch(() => false))) {
      findings.push({ i, orderNo, error: '没有履约追踪按钮' })
      continue
    }
    await btn.click().catch(() => {})
    await page.waitForTimeout(2_500)

    // 节点标题 + 其下第一行状态文案；established 的节点状态不是「尚未…」。
    const nodes = await page.evaluate(() => {
      const panel = document.querySelector('[role="dialog"], [data-slot="nv-sheet-content"]')
      if (!panel) return null
      return Array.from(panel.querySelectorAll('[data-fulfillment-node], li, .nv-ft-node'))
        .map((el) => (el as HTMLElement).innerText.split('\n').slice(0, 2).join(' | '))
        .filter((t) => t.trim().length > 0)
        .slice(0, 20)
    })
    const text =
      (await page
        .locator('[role="dialog"], [data-slot="nv-sheet-content"]')
        .first()
        .innerText()
        .catch(() => '')) ?? ''
    findings.push({
      i,
      orderNo,
      established: (text.match(/已建立|已产生/g) ?? []).length,
      pending: (text.match(/尚未产生|尚未建立关联/g) ?? []).length,
      nodes,
    })
    await page.keyboard.press('Escape').catch(() => {})
    await page.waitForTimeout(800)
  }

  await writeFile(outFile!, JSON.stringify(findings, null, 2), 'utf8')
  // eslint-disable-next-line no-console
  console.log(
    JSON.stringify(
      findings.map((f) => ({ ...(f as object), nodes: undefined })),
      null,
      1,
    ),
  )
})
