/**
 * 第五轮收尾取证：核这批改动在真机上的样子 + 补前几轮漏掉的抽屉/弹窗。
 *
 * 前几轮的取证缺口（都实际吃过亏）：
 *   1. 只截页面默认态 → 排产甘特在另一个 Tab，#1428 分组切换整轮没验到
 *   2. 只截首屏 → 泳道兜底排在最后，误判成「工序全丢了」
 *   3. **抽屉与弹窗一次都没截过** → owner 点名的溢出贴边、#1421 的组件层收口从未在真机验证
 *
 * 只取证不断言，判断留给协调方看图。
 */
import { test, type Page } from '@playwright/test'
import { mkdir, writeFile } from 'node:fs/promises'
import path from 'node:path'

const baseURL = process.env.NERV_IIP_PLAYWRIGHT_BASE_URL
const adminPassword = process.env.NERV_IIP_FULLSTACK_ADMIN_PASSWORD
const outDir = process.env.NERV_IIP_R5_OUT_DIR

test.skip(!baseURL || !adminPassword || !outDir, 'requires a running leader-demo stack')
test.setTimeout(40 * 60 * 1000)

const notes: Record<string, unknown>[] = []
let shotDir = ''
const failed: string[] = []

async function shot(page: Page, id: string, title: string, extra?: Record<string, unknown>) {
  await page.screenshot({ path: path.join(shotDir, `${id}-${title}.png`) })
  notes.push({ id, title, ...extra })
  // eslint-disable-next-line no-console
  console.log(`[${id}] ${title}${extra ? ' ' + JSON.stringify(extra) : ''}`)
}

async function login(page: Page) {
  await page.goto('/')
  await page.getByLabel('登录名').waitFor({ state: 'visible', timeout: 120_000 })
  await page.getByLabel('登录名').fill('admin')
  await page.getByLabel('密码').fill(adminPassword!)
  await page.getByRole('button', { name: '登录' }).click()
  await page.waitForURL(new URL('/', baseURL!).toString(), { timeout: 60_000 })
}

const row = (page: Page) => page.locator('tbody tr').first()

/** 品牌层是 `nv-sheet-content`（原版才是 `sheet-content`）——上一轮少写 nv- 前缀，白等一轮。 */
const PANEL_SELECTOR =
  '[role="dialog"], [data-slot="nv-sheet-content"], [data-slot="sheet-content"]'

const isPanelOpen = (page: Page) =>
  page.evaluate((selector) => document.querySelector(selector) != null, PANEL_SELECTOR)

/** 打开某页第一行的详情抽屉/弹窗，截首屏 + 底部（底部专看有没有被裁切）。 */
async function openAndShoot(page: Page, id: string, title: string, opener: () => Promise<void>) {
  try {
    await opener()
    await page.waitForTimeout(2_200)
    await shot(page, id, `${title}-抽屉`)
    // **滚抽屉自己的滚动容器，不是背后的页面**——page.mouse.wheel without moving the
    // cursor onto the drawer scrolls the page behind it，两张截图会一模一样（实际踩到）。
    const scrolled = await page.evaluate((selector) => {
      const panel = document.querySelector(selector)
      if (!panel) return { found: false, scrollable: false, delta: 0 }
      const scroller =
        [panel, ...Array.from(panel.querySelectorAll('*'))].find((el) => {
          const node = el as HTMLElement
          return node.scrollHeight - node.clientHeight > 24
        }) ?? null
      if (!scroller) return { found: true, scrollable: false, delta: 0 }
      const node = scroller as HTMLElement
      const before = node.scrollTop
      node.scrollTop = node.scrollHeight
      return { found: true, scrollable: true, delta: node.scrollTop - before }
    }, PANEL_SELECTOR)
    await page.waitForTimeout(900)
    await shot(page, `${id}b`, `${title}-抽屉底部`, scrolled)
    await page.keyboard.press('Escape').catch(() => {})
    await page.waitForTimeout(700)
  } catch (error) {
    notes.push({ id, title, opened: false, reason: String(error).slice(0, 160) })
    // eslint-disable-next-line no-console
    console.log(`[${id}] ${title} 打不开: ${String(error).slice(0, 120)}`)
  }
}

test('第五轮收尾取证', async ({ page }) => {
  shotDir = path.join(outDir!, 'screenshots/第五轮-收尾')
  await mkdir(shotDir, { recursive: true })
  page.on('response', (r) => {
    const u = new URL(r.url())
    if (r.status() >= 400 && u.pathname.startsWith('/api/'))
      failed.push(`${r.status()} ${u.pathname}`)
  })

  await login(page)

  // ── A. 排产：待排池的紧迫度与优先级（#1445 第 1~3 条）──
  await page.goto('/scheduling', { waitUntil: 'networkidle', timeout: 60_000 }).catch(() => {})
  await page.waitForTimeout(3_000)
  await shot(page, 'F-01', '待排池-紧迫度与优先级')
  await page.mouse.wheel(0, 600)
  await page.waitForTimeout(1_000)
  await shot(page, 'F-02', '待排池-下滚')
  await page.mouse.wheel(0, -600)

  // ── B. 甘特泳道排序（#1445 第 4 条）──
  await page
    .getByRole('tab', { name: '甘特图' })
    .click()
    .catch(() => {})
  await page.waitForTimeout(3_500)
  const groupTrigger = page.getByLabel('分组维度')
  if (await groupTrigger.isVisible().catch(() => false)) {
    await groupTrigger.click().catch(() => {})
    await page.waitForTimeout(800)
    const line = page.getByRole('option', { name: '产线' })
    if (await line.isVisible().catch(() => false)) await line.click()
    await page.waitForTimeout(2_500)
  }
  await shot(page, 'F-03', '甘特-按产线-默认排序（应为有活的在前）')

  const orderTrigger = page.getByLabel('泳道排序')
  const orderVisible = await orderTrigger.isVisible().catch(() => false)
  notes.push({ id: 'F-04', check: '泳道排序选择器', visible: orderVisible })
  if (orderVisible) {
    for (const [i, label] of ['名称升序', '仅有排程', '利用率降序'].entries()) {
      await orderTrigger.click().catch(() => {})
      await page.waitForTimeout(800)
      const opt = page.getByRole('option', { name: label })
      if (await opt.isVisible().catch(() => false)) {
        await opt.click()
        await page.waitForTimeout(2_200)
        await shot(page, `F-04-${i + 1}`, `泳道排序-${label}`)
      } else {
        await page.keyboard.press('Escape').catch(() => {})
      }
    }
  }

  // ── C. 抽屉与弹窗（前几轮完全没取过证）──
  const drawers: Array<[string, string, string]> = [
    ['F-05', '销售订单', '/erp/sales/orders'],
    ['F-06', 'MES工单', '/mes/work-orders'],
    ['F-07', '不合格品NCR', '/quality/ncrs'],
    ['F-08', '纠正措施CAPA', '/quality/capas'],
    ['F-09', '设备报警', '/equipment/alarms'],
    ['F-10', '维护工单', '/maintenance/work-orders'],
    ['F-11', '采购订单', '/erp/procurement/purchase-orders'],
    ['F-12', '库存批次', '/inventory/lots'],
  ]
  for (const [id, title, route] of drawers) {
    await page.goto(route, { waitUntil: 'networkidle', timeout: 60_000 }).catch(() => {})
    await page.waitForTimeout(2_500)
    await openAndShoot(page, id, title, async () => {
      // 上一轮只会「点首行」，而这些页大多靠**行内动作按钮**开抽屉（销售订单是「履约追踪」），
      // 于是 8 个里 6 个根本没打开，报告却只记 found=false —— 看不出是没开还是没匹配。
      // 改成多策略依次试，并把**命中的是哪一招**记进报告，下次再失败能直接定位。
      const strategies: Array<[string, () => Promise<boolean>]> = [
        [
          'row-action-button',
          async () => {
            const btn = row(page)
              .getByRole('button', { name: /履约追踪|详情|查看|解释|明细|处理/ })
              .first()
            if (!(await btn.isVisible().catch(() => false))) return false
            await btn.click()
            return true
          },
        ],
        [
          'row-first-link',
          async () => {
            const link = row(page).locator('a, button').first()
            if (!(await link.isVisible().catch(() => false))) return false
            await link.click()
            return true
          },
        ],
        [
          'row-click',
          async () => {
            if (
              !(await row(page)
                .isVisible()
                .catch(() => false))
            )
              return false
            await row(page).click()
            return true
          },
        ],
      ]
      for (const [name, run] of strategies) {
        if (!(await run().catch(() => false))) continue
        await page.waitForTimeout(1_800)
        if (await isPanelOpen(page)) {
          notes.push({ id, openedBy: name })
          return
        }
        await page.keyboard.press('Escape').catch(() => {})
        await page.waitForTimeout(400)
      }
      throw new Error('三种 opener 策略都没能打开抽屉')
    })
  }

  notes.push({ failedRequests: [...new Set(failed)] })
  await writeFile(
    path.join(outDir!, 'reports/r5-final.json'),
    JSON.stringify(notes, null, 2),
    'utf8',
  )
  // eslint-disable-next-line no-console
  console.log('FAILED:', failed.length ? [...new Set(failed)].join(' | ') : '（无）')
})
