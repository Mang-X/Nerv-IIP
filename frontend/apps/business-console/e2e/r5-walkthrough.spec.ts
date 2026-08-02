/**
 * 第五轮走查取证脚本（MAN-698）。
 *
 * 不做断言，只做三件事：逐页截图、记录每页的控制台错误与失败请求、把「看到了什么」
 * 落成机器可读的清单。**判断留给人**——协调方逐张看图核，代理报告不作数。
 *
 * 认证复用 leader-demo 系列的做法：口令从环境变量取，不落盘、不进日志。
 *
 * 产物落在 `artifacts/walkthrough-man698/`（该目录在 .gitignore 里，不入库）。
 */
import { test, type Page } from '@playwright/test'
import { mkdir, writeFile } from 'node:fs/promises'
import path from 'node:path'

const baseURL = process.env.NERV_IIP_PLAYWRIGHT_BASE_URL
const adminPassword = process.env.NERV_IIP_FULLSTACK_ADMIN_PASSWORD
const outDir = process.env.NERV_IIP_R5_OUT_DIR

test.skip(!baseURL || !adminPassword || !outDir, 'requires a running leader-demo stack')
test.setTimeout(60 * 60 * 1000)

type Stop = { id: string; chapter: string; title: string; path: string }

/** 九章走查路线。顺序即演示顺序——每章内部也按因果排，不是按菜单排。 */
const ROUTE: Stop[] = [
  // ── 第 1 棒 销售 ──
  { id: '01-01', chapter: '1-销售', title: '销售订单', path: '/erp/sales/orders' },
  { id: '01-02', chapter: '1-销售', title: '销售机会', path: '/erp/sales/opportunities' },
  { id: '01-03', chapter: '1-销售', title: '销售报价', path: '/erp/sales/quotations' },
  { id: '01-04', chapter: '1-销售', title: '销售发货', path: '/erp/sales/deliveries' },
  // ── 第 2 棒 计划 ──
  { id: '02-01', chapter: '2-计划', title: '需求与物料计划', path: '/planning' },
  { id: '02-02', chapter: '2-计划', title: '排产工作台', path: '/scheduling' },
  // ── 第 3 棒 工程 ──
  { id: '03-01', chapter: '3-工程', title: '工程变更', path: '/engineering/eco' },
  { id: '03-02', chapter: '3-工程', title: '生产版本', path: '/engineering/production-versions' },
  { id: '03-03', chapter: '3-工程', title: '工程文档', path: '/engineering/documents' },
  // ── 第 4 棒 制造 ──
  { id: '04-01', chapter: '4-制造', title: 'MES 工单', path: '/mes/work-orders' },
  { id: '04-02', chapter: '4-制造', title: '派工看板', path: '/mes/dispatch' },
  { id: '04-03', chapter: '4-制造', title: '工序任务', path: '/mes/operation-tasks' },
  { id: '04-04', chapter: '4-制造', title: '生产报工', path: '/mes/production-reports' },
  { id: '04-05', chapter: '4-制造', title: '在制品', path: '/mes/wip' },
  { id: '04-06', chapter: '4-制造', title: '领料与收料', path: '/mes/materials' },
  { id: '04-07', chapter: '4-制造', title: '完工入库', path: '/mes/receipts' },
  { id: '04-08', chapter: '4-制造', title: '停机记录', path: '/mes/downtime' },
  { id: '04-09', chapter: '4-制造', title: '班次交接', path: '/mes/handovers' },
  // ── 第 5 棒 质量 ──
  { id: '05-01', chapter: '5-质量', title: '待检工作台', path: '/quality/inspection-tasks' },
  { id: '05-02', chapter: '5-质量', title: '检验任务与记录', path: '/quality/inspections' },
  { id: '05-03', chapter: '5-质量', title: '不合格品 NCR', path: '/quality/ncrs' },
  { id: '05-04', chapter: '5-质量', title: '纠正措施 CAPA', path: '/quality/capas' },
  { id: '05-05', chapter: '5-质量', title: '质量分析 SPC', path: '/quality/analysis' },
  { id: '05-06', chapter: '5-质量', title: '量具校准', path: '/quality/calibration' },
  // ── 第 6 棒 库存 ──
  { id: '06-01', chapter: '6-库存', title: '库存可用量', path: '/inventory/availability' },
  { id: '06-02', chapter: '6-库存', title: '批次与预留', path: '/inventory/lots' },
  { id: '06-03', chapter: '6-库存', title: '库存移动', path: '/inventory/movements' },
  { id: '06-04', chapter: '6-库存', title: '库存盘点', path: '/inventory/counts' },
  // ── 第 7 棒 采购 ──
  { id: '07-01', chapter: '7-采购', title: '采购申请', path: '/erp/procurement/requisitions' },
  { id: '07-02', chapter: '7-采购', title: '询价 RFQ', path: '/erp/procurement/rfqs' },
  { id: '07-03', chapter: '7-采购', title: '供应商报价', path: '/erp/procurement/supplier-quotations' },
  { id: '07-04', chapter: '7-采购', title: '采购订单', path: '/erp/procurement/purchase-orders' },
  { id: '07-05', chapter: '7-采购', title: '采购收货', path: '/erp/procurement/receipts' },
  // ── 第 8 棒 财务 ──
  { id: '08-01', chapter: '8-财务', title: '财务摘要', path: '/erp/finance' },
  { id: '08-02', chapter: '8-财务', title: 'AR/AP', path: '/erp/finance/ar-ap' },
  { id: '08-03', chapter: '8-财务', title: '会计凭证', path: '/erp/finance/vouchers' },
  { id: '08-04', chapter: '8-财务', title: '成本候选', path: '/erp/finance/cost-candidates' },
  // ── 第 9 棒 设备与维护（owner 点名：前四轮从未覆盖）──
  { id: '09-01', chapter: '9-设备', title: '采集健康', path: '/equipment/telemetry/connectors' },
  { id: '09-02', chapter: '9-设备', title: '采集标签', path: '/equipment/telemetry/tags' },
  { id: '09-03', chapter: '9-设备', title: '设备运行看板', path: '/equipment' },
  { id: '09-04', chapter: '9-设备', title: '历史趋势', path: '/equipment/telemetry/history' },
  { id: '09-05', chapter: '9-设备', title: '设备报警', path: '/equipment/alarms' },
  { id: '09-06', chapter: '9-设备', title: '报警规则', path: '/equipment/telemetry/alarm-rules' },
  { id: '09-07', chapter: '9-设备', title: 'OEE 与可用性', path: '/equipment/telemetry/oee' },
  { id: '09-08', chapter: '9-设备', title: '控制通道绑定', path: '/equipment/telemetry/control-bindings' },
  { id: '09-09', chapter: '9-设备', title: '保养计划', path: '/maintenance/plans' },
  { id: '09-10', chapter: '9-设备', title: '维护工单', path: '/maintenance/work-orders' },
  { id: '09-11', chapter: '9-设备', title: '点检记录', path: '/maintenance/inspections' },
  { id: '09-12', chapter: '9-设备', title: '可用窗口', path: '/maintenance/availability' },
  { id: '09-13', chapter: '9-设备', title: '可靠性指标', path: '/maintenance/reliability' },
  { id: '09-14', chapter: '9-设备', title: '备件需求', path: '/maintenance/spare-parts' },
]

const UUID = /[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i

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

test('第五轮走查取证', async ({ page }) => {
  const shotDir = path.join(outDir!, 'screenshots/第五轮')
  await mkdir(shotDir, { recursive: true })

  await login(page)

  const findings: Record<string, unknown>[] = []

  for (const stop of ROUTE) {
    const consoleErrors: string[] = []
    const failedRequests: string[] = []
    const onConsole = (m: { type: () => string; text: () => string }) => {
      if (m.type() === 'error') consoleErrors.push(m.text().slice(0, 300))
    }
    const onResponse = (r: { status: () => number; url: () => string }) => {
      if (r.status() >= 400) {
        const u = new URL(r.url())
        if (u.pathname.startsWith('/api/')) failedRequests.push(`${r.status()} ${u.pathname}`)
      }
    }
    page.on('console', onConsole)
    page.on('response', onResponse)

    let navError: string | null = null
    try {
      await page.goto(stop.path, { waitUntil: 'networkidle', timeout: 60_000 })
    } catch (e) {
      navError = String(e).slice(0, 200)
      // networkidle 超时不等于页面坏——轮询类页面永远不 idle。继续取证。
      try {
        await page.waitForLoadState('domcontentloaded', { timeout: 10_000 })
      } catch {
        /* 记录即可 */
      }
    }
    await page.waitForTimeout(2_500)

    const file = path.join(shotDir, `${stop.id}-${stop.title}.png`)
    await page.screenshot({ path: file, fullPage: false })

    const bodyText = (await page.locator('body').innerText().catch(() => '')) ?? ''
    findings.push({
      id: stop.id,
      chapter: stop.chapter,
      title: stop.title,
      path: stop.path,
      screenshot: path.basename(file),
      navError,
      consoleErrors: [...new Set(consoleErrors)].slice(0, 6),
      failedRequests: [...new Set(failedRequests)].slice(0, 10),
      // 给协调方看图前的预筛信号，不代替看图
      signals: {
        leaksUuid: UUID.test(bodyText),
        leaksTechAccount: /user-emp-/.test(bodyText),
        textLength: bodyText.length,
        looksEmpty: bodyText.length < 400,
        hasNoDataWord: /暂无|没有数据|空空如也|无记录/.test(bodyText),
      },
      textHead: bodyText.replace(/\s+/g, ' ').slice(0, 260),
    })

    page.off('console', onConsole)
    page.off('response', onResponse)
    // eslint-disable-next-line no-console
    console.log(
      `[${stop.id}] ${stop.chapter} ${stop.title} — err=${consoleErrors.length} 4xx/5xx=${failedRequests.length}${navError ? ' NAV_SLOW' : ''}`,
    )
  }

  await writeFile(
    path.join(outDir!, 'reports/r5-walkthrough.json'),
    JSON.stringify(findings, null, 2),
    'utf8',
  )
})
