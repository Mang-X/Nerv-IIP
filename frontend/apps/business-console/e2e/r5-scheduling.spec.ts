/**
 * 第五轮走查 · 排产专场取证（owner 两次点名着重检查）。
 *
 * 覆盖：三个 Tab 的完整性、甘特分组维度切换（#1428）、单工单排产入口与弹框。
 * 只取证不断言——判断留给协调方看图。
 */
import { test, type Page } from '@playwright/test'
import { mkdir, writeFile } from 'node:fs/promises'
import path from 'node:path'

const baseURL = process.env.NERV_IIP_PLAYWRIGHT_BASE_URL
const adminPassword = process.env.NERV_IIP_FULLSTACK_ADMIN_PASSWORD
const outDir = process.env.NERV_IIP_R5_OUT_DIR

test.skip(!baseURL || !adminPassword || !outDir, 'requires a running leader-demo stack')
test.setTimeout(30 * 60 * 1000)

const notes: Record<string, unknown>[] = []
let shotDir = ''

async function shot(page: Page, id: string, title: string, note?: Record<string, unknown>) {
  const file = path.join(shotDir, `${id}-${title}.png`)
  await page.screenshot({ path: file })
  notes.push({ id, title, screenshot: path.basename(file), ...note })
  // eslint-disable-next-line no-console
  console.log(`[${id}] ${title}${note ? ' ' + JSON.stringify(note) : ''}`)
}

test('第五轮 · 排产专场', async ({ page }) => {
  shotDir = path.join(outDir!, 'screenshots/第五轮-排产')
  await mkdir(shotDir, { recursive: true })

  const failed: string[] = []
  page.on('response', (r) => {
    const u = new URL(r.url())
    if (r.status() >= 400 && u.pathname.startsWith('/api/')) failed.push(`${r.status()} ${u.pathname}`)
  })

  // ── 登录 ──
  await page.goto('/')
  await page.getByLabel('登录名').waitFor({ state: 'visible', timeout: 120_000 })
  await page.getByLabel('登录名').fill('admin')
  await page.getByLabel('密码').fill(adminPassword!)
  await page.getByRole('button', { name: '登录' }).click()
  await page.waitForURL(new URL('/', baseURL!).toString(), { timeout: 60_000 })

  // ── A. 排程总览（默认 Tab）──
  await page.goto('/scheduling', { waitUntil: 'networkidle', timeout: 60_000 }).catch(() => {})
  await page.waitForTimeout(3_000)
  await shot(page, 'S-01', '排程总览-首屏')

  // 滚到待排池下方，看排程草案工作区
  await page.mouse.wheel(0, 1200)
  await page.waitForTimeout(1_200)
  await shot(page, 'S-02', '排程总览-草案工作区')
  await page.mouse.wheel(0, 1200)
  await page.waitForTimeout(1_200)
  await shot(page, 'S-03', '排程总览-底部')
  await page.mouse.wheel(0, -3000)
  await page.waitForTimeout(800)

  // ── B. 表格 Tab ──
  await page.getByRole('tab', { name: '表格' }).click().catch(() => {})
  await page.waitForTimeout(2_500)
  await shot(page, 'S-04', '表格Tab')

  // ── C. 甘特图 Tab ──
  await page.getByRole('tab', { name: '甘特图' }).click().catch(() => {})
  await page.waitForTimeout(3_500)
  await shot(page, 'S-05', '甘特图Tab-默认')

  // ── D. 分组维度切换（#1428 的验收点）──
  const groupTrigger = page.getByLabel('分组维度')
  const groupVisible = await groupTrigger.isVisible().catch(() => false)
  notes.push({ id: 'S-06', check: '分组维度选择器是否存在', visible: groupVisible })
  if (groupVisible) {
    for (const [i, dim] of ['车间', '产线', '工作中心'].entries()) {
      await groupTrigger.click().catch(() => {})
      await page.waitForTimeout(900)
      if (i === 0) await shot(page, 'S-06', '分组维度-下拉展开')
      const opt = page.getByRole('option', { name: dim })
      const ok = await opt.isVisible().catch(() => false)
      if (ok) {
        await opt.click()
        await page.waitForTimeout(2_500)
        await shot(page, `S-07-${i + 1}`, `甘特-按${dim}分组`)
        // 泳道列表往下滚：兜底的「未归属」泳道排在最后，首屏看不到——
        // 上一轮就是只截首屏，误判成「工序全丢了」。
        for (const step of [1, 2]) {
          await page.mouse.move(500, 700)
          await page.mouse.wheel(0, 900)
          await page.waitForTimeout(1_200)
          await shot(page, `S-07-${i + 1}-scroll${step}`, `甘特-按${dim}分组-下滚${step}`)
        }
        await page.mouse.wheel(0, -2400)
        await page.waitForTimeout(800)
      } else {
        await page.keyboard.press('Escape').catch(() => {})
        notes.push({ id: `S-07-${i + 1}`, check: `分组选项「${dim}」`, found: false })
      }
    }
  }

  // ── E. 时间刻度切换 ──
  const scaleTrigger = page.getByLabel('时间刻度')
  if (await scaleTrigger.isVisible().catch(() => false)) {
    await scaleTrigger.click().catch(() => {})
    await page.waitForTimeout(800)
    await shot(page, 'S-08', '时间刻度-下拉')
    await page.keyboard.press('Escape').catch(() => {})
  }

  // ── F. 单工单排产：计划工作台入口 ──
  await page.goto('/planning', { waitUntil: 'networkidle', timeout: 60_000 }).catch(() => {})
  await page.waitForTimeout(3_000)
  await shot(page, 'S-09', '计划工作台-首屏')

  // 计划建议 Tab 里才有「对该单排产」
  const suggestTab = page.getByRole('tab', { name: /计划建议/ })
  if (await suggestTab.isVisible().catch(() => false)) {
    await suggestTab.click()
    await page.waitForTimeout(2_500)
    await shot(page, 'S-10', '计划建议列表')
  }

  // 入口可能在列表下方/行内菜单里，先把计划建议列表滚一遍再判断「找不到」。
  for (const step of [1, 2, 3]) {
    await page.mouse.wheel(0, 1000)
    await page.waitForTimeout(1_000)
    await shot(page, `S-10-scroll${step}`, `计划建议列表-下滚${step}`)
  }
  const singleBtn = page.getByRole('button', { name: '对该单排产' }).first()
  const singleVisible = await singleBtn.isVisible().catch(() => false)
  const singleCount = await page.getByRole('button', { name: '对该单排产' }).count().catch(() => 0)
  // 行内「⋯」菜单也可能藏着它——数一下页面上有多少个，0 才是真没有。
  notes.push({
    id: 'S-11',
    check: '「对该单排产」入口',
    visible: singleVisible,
    countOnPage: singleCount,
  })
  if (singleVisible) {
    await singleBtn.click()
    await page.waitForTimeout(2_500)
    await shot(page, 'S-11', '单工单排产-弹框')
    // 弹框内滚到底，看按钮有没有被裁切
    await page.mouse.wheel(0, 800)
    await page.waitForTimeout(900)
    await shot(page, 'S-12', '单工单排产-弹框底部')
    await page.keyboard.press('Escape').catch(() => {})
  }

  // ── G. 工单列表里的单工单排产入口 ──
  await page.goto('/mes/work-orders', { waitUntil: 'networkidle', timeout: 60_000 }).catch(() => {})
  await page.waitForTimeout(3_000)
  await shot(page, 'S-13', 'MES工单列表')

  notes.push({ failedRequests: [...new Set(failed)] })
  await writeFile(
    path.join(outDir!, 'reports/r5-scheduling.json'),
    JSON.stringify(notes, null, 2),
    'utf8',
  )
  // eslint-disable-next-line no-console
  console.log('FAILED:', failed.length ? [...new Set(failed)].join(' | ') : '（无）')
})
