/** 漏词告警频道是否已经可信：走一圈上一轮报过假警报的页面，看还剩几条。 */
import { test, type Page } from '@playwright/test'

const baseURL = process.env.NERV_IIP_PLAYWRIGHT_BASE_URL
const adminPassword = process.env.NERV_IIP_FULLSTACK_ADMIN_PASSWORD
test.skip(!baseURL || !adminPassword, 'requires a running leader-demo stack')
test.setTimeout(10 * 60 * 1000)

async function login(page: Page) {
  await page.goto('/')
  await page.getByLabel('登录名').waitFor({ state: 'visible', timeout: 120_000 })
  await page.getByLabel('登录名').fill('admin')
  await page.getByLabel('密码').fill(adminPassword!)
  await page.getByRole('button', { name: '登录' }).click()
  await page.waitForURL(new URL('/', baseURL!).toString(), { timeout: 60_000 })
}

test('漏词告警频道复核', async ({ page }) => {
  test.skip(test.info().project.name !== 'desktop', 'desktop only')
  page.setDefaultTimeout(15_000)
  const warns: string[] = []
  page.on('console', (m) => {
    if (/词表缺失/.test(m.text())) warns.push(m.text().slice(0, 140))
  })
  await login(page)
  for (const r of ['/quality/calibration', '/mes/downtime', '/approval', '/erp/sales/orders']) {
    await page.goto(r, { waitUntil: 'networkidle', timeout: 60_000 }).catch(() => {})
    await page.waitForTimeout(3_000)
  }
  // eslint-disable-next-line no-console
  console.log('剩余漏词告警：', warns.length ? [...new Set(warns)].join(' | ') : '（无）')
})
