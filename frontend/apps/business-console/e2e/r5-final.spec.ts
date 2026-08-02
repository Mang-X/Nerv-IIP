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
// business-console 是 PC 台，移动端由 business-pda 单独负责——在 mobile project 上跑既没意义，
// 还会**把桌面截图覆盖掉**：两个 project 写同一个 `${shotDir}/${id}-${title}.png`。
// （实际吃过：以为在看 1366 宽的桌面版式，读到的其实是 390×844@2.75DPR 的手机截图，
// 排产工作台被压成单列堆叠，据此判断布局全是错的。）
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
  // 文件级 test.skip 的回调只拿得到 fixture、拿不到 testInfo，必须在用例体内判断。
  test.skip(test.info().project.name !== 'desktop', '只在 desktop project 取证')
  // Playwright 的 actionTimeout 默认是 0 = **永不超时**。声明的按钮一旦不在页上，
  // click 会一直挂到用例超时（实际踩到：CAPA 那步空转 6 分钟才被发现）。
  // 取证脚本宁可快速判失败、留下 opener 失效的记录，也不该静默卡住。
  page.setDefaultTimeout(10_000)
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
  //
  // 上一轮 8 个只开出 2 个，另外 6 个报告里只写一句 found=false —— 分不清是
  // 「没打开」还是「打开了但选择器没匹配」，等于没取到证。逐页读了触发器才发现
  // 三种情况，一把「点首行」根本盖不住：
  //   · NCR / 设备报警 / 维护工单 —— 抽屉在行内 NvRowActions 的「…」菜单里，
  //     得先开菜单再点菜单项
  //   · 采购订单 —— 行上没有详情抽屉，页上唯一的弹框是工具栏「新建」
  //   · 库存批次 —— **整页没有任何抽屉/弹框**，硬凑 opener 只会造出假失败
  // 所以改成按页声明打开方式，而不是盲试；`none` 是如实记录"此页无抽屉"。
  type DrawerCase = {
    id: string
    title: string
    route: string
  } & (
    | { via: 'row-action'; name: RegExp }
    | { via: 'row' }
    | { via: 'row-menu'; name: RegExp }
    | { via: 'toolbar'; name: RegExp }
    | { via: 'none' }
  )

  const drawers: DrawerCase[] = [
    {
      id: 'F-05',
      title: '销售订单',
      route: '/erp/sales/orders',
      via: 'row-action',
      name: /履约追踪/,
    },
    {
      id: 'F-06',
      title: 'MES工单',
      route: '/mes/work-orders',
      via: 'row-action',
      name: /详情|查看/,
    },
    { id: 'F-07', title: '不合格品NCR', route: '/quality/ncrs', via: 'row-menu', name: /打开处置/ },
    // CAPA 是整行可点、不是行内按钮——上一轮声明成 row-action，靠兜底才开出来；
    // 声明既然不准就改准，别让兜底把烂声明一直背下去。
    { id: 'F-08', title: '纠正措施CAPA', route: '/quality/capas', via: 'row' },
    // 只点「抑制」（开搁置弹框）。**取证脚本绝不能点会落库的动作**：上一版正则里
    // 带了「确认」，它直接把演示库里一条报警确认掉了（右下角弹「报警已确认。」），
    // 抽屉压根没开，报告里只留一句 found=false——既污染数据又没取到证。
    { id: 'F-09', title: '设备报警', route: '/equipment/alarms', via: 'row-menu', name: /抑制/ },
    {
      id: 'F-10',
      title: '维护工单',
      route: '/maintenance/work-orders',
      via: 'row-menu',
      name: /完成|详情|派工/,
    },
    {
      id: 'F-11',
      title: '采购订单-新建',
      route: '/erp/procurement/purchase-orders',
      via: 'toolbar',
      name: /新建|创建/,
    },
    { id: 'F-12', title: '库存批次', route: '/inventory/lots', via: 'none' },
  ]

  for (const item of drawers) {
    await page.goto(item.route, { waitUntil: 'networkidle', timeout: 60_000 }).catch(() => {})
    await page.waitForTimeout(2_500)

    if (item.via === 'none') {
      await shot(page, item.id, `${item.title}-整页`, { drawer: '此页无抽屉/弹框，只取页面证据' })
      continue
    }

    await openAndShoot(page, item.id, item.title, async () => {
      const declared = async () => {
        if (item.via === 'toolbar') {
          await page.getByRole('button', { name: item.name }).first().click()
          return
        }
        if (item.via === 'row') {
          await row(page).click()
          return
        }
        if (item.via === 'row-action') {
          await row(page).getByRole('button', { name: item.name }).first().click()
          return
        }
        // row-menu：NvRowActions 的触发器带 aria-label（页面自定义如「NCR 操作 NCR-…」，
        // 兜底是「更多操作」），先开菜单，等菜单项挂上 Portal 再点。
        await row(page)
          .getByRole('button', { name: /操作/ })
          .first()
          .click()
        await page.getByRole('menuitem', { name: item.name }).first().click()
      }
      try {
        await declared()
      } catch {
        // 声明写错（页面改了触发器）时退回点首行，并**记下来用的是兜底**——
        // 悄悄成功比失败更坏：声明会一直烂着，下次谁也不知道它早就不准了。
        await page.keyboard.press('Escape').catch(() => {})
        await row(page).click()
        notes.push({ id: item.id, openedBy: 'fallback-row-click', declared: item.via })
      }
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
