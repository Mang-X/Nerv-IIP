/**
 * 库存批次页的**可演示性**探针（只读）。
 *
 * 起因：第六轮取证拍到「批次与预留 3811 条全厂批次」，但首屏每一行的
 * 现存量/预留量/可用量全是 0、效期全是「效期未知」、连物料和库位都一样。
 * 需要分清：
 *   (a) 只是默认排序把已耗尽的老批次排在前面 → 演示换个筛选即可
 *   (b) 全库都是 0 → 缺料/齐套故事没有库存事实支撑
 */
import { test, type Page } from '@playwright/test'
import { writeFile } from 'node:fs/promises'

const baseURL = process.env.NERV_IIP_PLAYWRIGHT_BASE_URL
const adminPassword = process.env.NERV_IIP_FULLSTACK_ADMIN_PASSWORD
const outFile = process.env.NERV_IIP_R6_LOTS_OUT

test.skip(!baseURL || !adminPassword || !outFile, 'requires a running leader-demo stack')
test.setTimeout(15 * 60 * 1000)

async function login(page: Page) {
  await page.goto('/')
  await page.getByLabel('登录名').waitFor({ state: 'visible', timeout: 120_000 })
  await page.getByLabel('登录名').fill('admin')
  await page.getByLabel('密码').fill(adminPassword!)
  await page.getByRole('button', { name: '登录' }).click()
  await page.waitForURL(new URL('/', baseURL!).toString(), { timeout: 60_000 })
}

test('库存批次可演示性', async ({ page }) => {
  test.skip(test.info().project.name !== 'desktop', '只在 desktop project 探测')
  page.setDefaultTimeout(15_000)
  await login(page)
  await page.goto('/inventory/lots', { waitUntil: 'networkidle', timeout: 60_000 })
  await page.waitForTimeout(3_000)

  const findings: unknown[] = []
  const pages = Number(process.env.NERV_IIP_R6_LOTS_PAGES ?? 10)
  for (let p = 0; p < pages; p += 1) {
    const rows = await page.locator('tbody tr').count()
    let nonZero = 0
    const samples: string[] = []
    for (let i = 0; i < rows; i += 1) {
      const cells = await page.locator('tbody tr').nth(i).locator('td').allInnerTexts()
      // 末尾几列是 现存/预留/可用/冻结，取数字判断有没有库存事实。
      const nums = cells.slice(-5).flatMap((c) => c.match(/\d[\d,]*/g) ?? [])
      const hasStock = nums.some((n) => Number(n.replace(/,/g, '')) > 0)
      if (hasStock) {
        nonZero += 1
        if (samples.length < 2) samples.push(cells.slice(0, 2).join(' / ') + ' → ' + nums.join(','))
      }
    }
    findings.push({ page: p + 1, rows, nonZero, samples })
    await page
      .getByRole('button', { name: /下一页|next/i })
      .first()
      .click()
      .catch(() => {})
    await page.waitForTimeout(1_800)
  }

  await writeFile(outFile!, JSON.stringify(findings, null, 2), 'utf8')
  // eslint-disable-next-line no-console
  console.log(JSON.stringify(findings, null, 1))
})
