/**
 * 第六轮补扫：前几轮取证没覆盖到的页面，只取整页证据 + 记控制台告警与失败请求。
 *
 * 只读：不点任何行内动作、不开会落库的弹框（上一版取证脚本误确认过一条报警）。
 */
import { test, type Page } from '@playwright/test'
import { mkdir, writeFile } from 'node:fs/promises'
import path from 'node:path'

const baseURL = process.env.NERV_IIP_PLAYWRIGHT_BASE_URL
const adminPassword = process.env.NERV_IIP_FULLSTACK_ADMIN_PASSWORD
const outDir = process.env.NERV_IIP_R6_SWEEP_DIR

test.skip(!baseURL || !adminPassword || !outDir, 'requires a running leader-demo stack')
test.setTimeout(30 * 60 * 1000)

async function login(page: Page) {
  await page.goto('/')
  await page.getByLabel('登录名').waitFor({ state: 'visible', timeout: 120_000 })
  await page.getByLabel('登录名').fill('admin')
  await page.getByLabel('密码').fill(adminPassword!)
  await page.getByRole('button', { name: '登录' }).click()
  await page.waitForURL(new URL('/', baseURL!).toString(), { timeout: 60_000 })
}

const PAGES: Array<[string, string]> = [
  ['质量-检验任务', '/quality/inspections'],
  ['质量-追溯查询', '/quality/traceability'],
  ['质量-分析', '/quality/analysis'],
  ['质量-校准', '/quality/calibration'],
  ['MES-齐套', '/mes/materials'],
  ['MES-产能', '/mes/capacity'],
  ['MES-停机', '/mes/downtime'],
  ['设备-OEE与可用性', '/equipment/oee'],
  ['设备-历史趋势', '/equipment/trends'],
  ['维护-保养计划', '/maintenance/plans'],
  ['维护-备件需求', '/maintenance/spare-demands'],
  ['维护-可靠性指标', '/maintenance/reliability'],
  ['财务-AR/AP', '/erp/finance/ar-ap'],
  ['采购-请购单', '/erp/procurement/requisitions'],
  ['WMS-收货入库', '/wms/receiving'],
  ['WMS-出库发货', '/wms/outbound'],
]

test('第六轮补扫', async ({ page }) => {
  test.skip(test.info().project.name !== 'desktop', '只在 desktop project 取证')
  page.setDefaultTimeout(15_000)
  const dir = path.join(outDir!, 'screenshots')
  await mkdir(dir, { recursive: true })

  const warns: string[] = []
  const failed: string[] = []
  page.on('console', (m) => {
    if (m.type() === 'warning' || m.type() === 'error') warns.push(m.text().slice(0, 160))
  })
  page.on('response', (r) => {
    const u = new URL(r.url())
    if (r.status() >= 400 && u.pathname.startsWith('/api/'))
      failed.push(`${r.status()} ${u.pathname}`)
  })

  await login(page)
  const notes: unknown[] = []
  for (const [title, route] of PAGES) {
    const before = failed.length
    await page.goto(route, { waitUntil: 'networkidle', timeout: 60_000 }).catch(() => {})
    await page.waitForTimeout(2_500)
    await page.screenshot({ path: path.join(dir, `${title}.png`) })
    const body = await page
      .locator('body')
      .innerText()
      .catch(() => '')
    notes.push({
      title,
      route,
      // 空态/错误态的常见措辞，供人工复核时优先看这几页。
      looksEmpty: /暂无|没有数据|空空如也|未找到|无记录/.test(body),
      hasError: /出错|失败|无法|错误/.test(body),
      newFailedRequests: failed.slice(before),
    })
    // eslint-disable-next-line no-console
    console.log(
      `[${title}] empty=${/暂无|没有数据|未找到|无记录/.test(body)} err=${failed.length - before}`,
    )
  }

  await writeFile(
    path.join(outDir!, 'sweep.json'),
    JSON.stringify({ notes, warns: [...new Set(warns)], failed: [...new Set(failed)] }, null, 2),
    'utf8',
  )
})
