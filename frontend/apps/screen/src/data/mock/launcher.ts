// 门厅 mock 聚合：层级计数从 masterdata 真实汇总，动态指标受控抖动。
// 🟠 产量/达成/健康度等待 #570 真实聚合端点，接入后由 fetchers/launcher.ts 单点切换。
import type { GlanceChip, LauncherSummary, ScreenGlance } from '@/data/contracts/launcher'
import { jitter } from './fixtures'
import { buildLineCards } from './line'
import { devicesByWorkshop, linesByWorkshop, workshopsByFactory } from './masterdata'
import { buildQualityBoard } from './quality'
import { buildWarehouseBoard } from './warehouse'

function clamp(n: number, lo: number, hi: number): number {
  return Math.min(hi, Math.max(lo, n))
}

export function buildLauncherSummary(
  factoryId: string,
  workshopIds: string[] | 'all' = 'all',
): LauncherSummary {
  // persona 收窄：车间线长等角色只聚合白名单车间（与未来真实端点的 query 形状一致）
  const factoryWorkshops = workshopsByFactory(factoryId)
  const workshops =
    workshopIds === 'all' ? factoryWorkshops : factoryWorkshops.filter((w) => workshopIds.includes(w.id))
  const lines = workshops.flatMap((w) => linesByWorkshop(w.id))
  const totalDevices = workshops.reduce((n, w) => n + devicesByWorkshop(w.id).length, 0)

  // 设备状态简化为四桶（报警/离线/待机/运行），互斥且和恒等于总数
  const alarmDevices = clamp(jitter(1, 2), 0, Math.min(2, totalDevices))
  const offlineDevices = clamp(jitter(1, 1), 0, totalDevices - alarmDevices)
  const idleDevices = clamp(jitter(2, 2), 0, totalDevices - alarmDevices - offlineDevices)
  const runningDevices = totalDevices - alarmDevices - offlineDevices - idleDevices

  const runningLines = clamp(jitter(lines.length - 1, 2), Math.min(1, lines.length), lines.length)
  const activeWorkshops = clamp(jitter(workshops.length, 1), Math.min(1, workshops.length), workshops.length)

  const achievement = clamp(jitter(96, 5), 88, 100) // 🟠 待 #570
  const shiftAchievement = clamp(jitter(94, 6), 85, 100) // 🟠 待 #570
  const takt = clamp(jitter(97, 4), 90, 103) // 🟠 待 #570
  const health = clamp(jitter(94, 4) - alarmDevices * 4, 62, 99) // 🟠 待 #570
  // 产量按可见产线占比折算，scope 收窄后数字跟着变小 🟠 待 #570
  const factoryLines = factoryWorkshops.flatMap((w) => linesByWorkshop(w.id))
  const baseOutput = factoryId === 'F02' ? 3620 : 12480
  const scaledOutput = factoryLines.length > 0 ? Math.round((baseOutput * lines.length) / factoryLines.length) : 0
  const output = jitter(scaledOutput, Math.max(40, Math.round(scaledOutput * 0.02))) // 🟠 待 #570
  const openAlarms = alarmDevices + clamp(jitter(1, 2), 0, 2)

  // —— 成员导航层：报警/离线设备按序落到前几台（mock 确定性分配，够真实即可）——
  const devices = workshops.flatMap((w) => devicesByWorkshop(w.id))
  const alarmedDevices = devices.slice(0, alarmDevices)
  const offlinedDevices = devices.slice(alarmDevices, alarmDevices + offlineDevices)
  const abnormalChips: GlanceChip[] = [
    ...alarmedDevices.map((d) => ({ label: `${d.name} 报警`, tone: 'alarm' as const })),
    ...offlinedDevices.map((d) => ({ label: `${d.name} 离线`, tone: 'off' as const })),
  ]
  const alarmWorkshopIds = new Set(alarmedDevices.map((d) => d.workshopId))
  const workshopChips: GlanceChip[] = workshops.map((w) => ({
    label: w.name,
    tone: alarmWorkshopIds.has(w.id) ? ('alarm' as const) : ('run' as const),
  }))
  const lineChips: GlanceChip[] = lines.map((l, i) => ({
    label: l.name,
    tone: i < runningLines ? ('run' as const) : ('idle' as const),
  }))

  const glances: ScreenGlance[] = [
    {
      key: 'factory',
      state: health >= 85 ? 'run' : 'idle',
      stateLabel: health >= 85 ? '运行正常' : '需关注',
      stats: [
        { label: '在产车间', value: `${activeWorkshops}/${workshops.length}` },
        { label: '全厂健康度', value: `${health}%`, tone: health >= 85 ? 'ok' : 'warn' },
        { label: '今日达成', value: `${achievement}%` },
      ],
      chipsLabel: '车间状态',
      chips: workshopChips,
    },
    {
      key: 'equipment',
      state: alarmDevices > 0 ? 'alarm' : offlineDevices > 0 ? 'idle' : 'run',
      stateLabel:
        alarmDevices > 0 ? `${alarmDevices} 台报警` : offlineDevices > 0 ? `${offlineDevices} 台离线` : '全部在线',
      stats: [
        { label: '运行', value: `${runningDevices} 台` },
        { label: '报警', value: `${alarmDevices} 台`, tone: alarmDevices > 0 ? 'bad' : undefined },
        { label: '离线', value: `${offlineDevices} 台`, tone: offlineDevices > 0 ? 'warn' : undefined },
      ],
      chipsLabel: '异常设备',
      chips: abnormalChips.length > 0 ? abnormalChips : [{ label: '全部在线', tone: 'run' }],
    },
    {
      key: 'line',
      state: 'run',
      stateLabel: '作业中',
      stats: [
        { label: '在产产线', value: `${runningLines}/${lines.length}` },
        { label: '当班达成', value: `${shiftAchievement}%`, tone: shiftAchievement >= 95 ? 'ok' : undefined },
        { label: '节拍达成', value: `${takt}%` },
      ],
      chipsLabel: '产线状态',
      chips: lineChips,
    },
    // —— M2 二期屏：一瞥全部从各屏 mock 同源派生（与进屏后看到的是同一批数字）——
    ...buildM2Glances(factoryId, workshopIds, workshops.map((w) => w.id)),
  ]

  return {
    factoryId,
    kpis: { output, achievement, runningDevices, totalDevices, openAlarms, health },
    glances,
  }
}

/** M2 屏一瞥：车间总览 / 仓储物流 / 质量看板 —— 全部调用各屏 build 纯函数取摘要，
 *  与进屏后的数字同源（不另拍一套）。 */
function buildM2Glances(
  factoryId: string,
  workshopIds: string[] | 'all',
  visibleWorkshopIds: string[],
): ScreenGlance[] {
  // 车间总览：产线卡按车间归并（与 factory/line 同一事实源）
  const lineCards = buildLineCards(factoryId, workshopIds)
  const byWs = new Map<string, { alarm: number; attention: number }>()
  for (const c of lineCards) {
    const ws = workshopsByFactory(factoryId).find((w) => linesByWorkshop(w.id).some((l) => l.id === c.id))
    if (!ws) continue
    const agg = byWs.get(ws.id) ?? { alarm: 0, attention: 0 }
    if (c.state === 'alarm') agg.alarm += 1
    if (c.state === 'attention') agg.attention += 1
    byWs.set(ws.id, agg)
  }
  const wsGood = lineCards.reduce((n, c) => n + c.output.good, 0)
  const wsPlan = lineCards.reduce((n, c) => n + c.output.plan, 0)
  const wsAch = wsPlan > 0 ? Math.round((wsGood / wsPlan) * 100) : 0
  const alarmWs = [...byWs.values()].filter((a) => a.alarm > 0).length
  const workshopChips: GlanceChip[] = visibleWorkshopIds.map((id) => {
    const w = workshopsByFactory(factoryId).find((x) => x.id === id)
    const agg = byWs.get(id)
    return {
      label: w?.name ?? id,
      tone: agg && agg.alarm > 0 ? ('alarm' as const) : agg && agg.attention > 0 ? ('idle' as const) : ('run' as const),
    }
  })
  const workshopGlance: ScreenGlance = {
    key: 'workshop',
    state: alarmWs > 0 ? 'alarm' : 'run',
    stateLabel: alarmWs > 0 ? `${alarmWs} 车间报警` : '当班作业中',
    stats: [
      { label: '当班达成', value: `${wsAch}%`, tone: wsAch >= 93 ? 'ok' : wsAch >= 85 ? 'warn' : 'bad' },
      { label: '产线报警', value: `${lineCards.filter((c) => c.state === 'alarm').length} 条`, tone: lineCards.some((c) => c.state === 'alarm') ? 'bad' : undefined },
      { label: '需关注产线', value: `${lineCards.filter((c) => c.state === 'attention').length} 条` },
    ],
    chipsLabel: '车间作战态',
    chips: workshopChips,
  }

  // 仓储物流：WMS 作业指挥摘要（工厂级，不随车间收窄）
  const wh = buildWarehouseBoard(new Date(), factoryId)
  const whChips: GlanceChip[] = [
    ...wh.wcs.failures.map((f) => ({ label: `${f.adapter} 失败`, tone: 'alarm' as const })),
    ...(wh.pick.overdue > 0 ? [{ label: `拣货超时 ${wh.pick.overdue}`, tone: 'idle' as const }] : []),
    ...(wh.putaway.overdue > 0 ? [{ label: `上架超时 ${wh.putaway.overdue}`, tone: 'idle' as const }] : []),
  ]
  const warehouseGlance: ScreenGlance = {
    key: 'warehouse',
    state: wh.kpis.wcsFailed > 0 ? 'alarm' : 'run',
    stateLabel: wh.kpis.wcsFailed > 0 ? `${wh.kpis.wcsFailed} 条 WCS 失败` : '作业顺畅',
    stats: [
      { label: '当日出库', value: `${wh.kpis.outboundPct}%`, tone: wh.kpis.outboundPct >= 90 ? 'ok' : undefined },
      { label: '拣货积压', value: `${wh.kpis.pickBacklog} 项` },
      { label: 'WCS 失败', value: `${wh.kpis.wcsFailed} 条`, tone: wh.kpis.wcsFailed > 0 ? 'bad' : 'ok' },
    ],
    chipsLabel: '异常作业',
    chips: whChips.length > 0 ? whChips : [{ label: '作业顺畅', tone: 'run' }],
  }

  // 质量看板：健康度 + 待办摘要
  const q = buildQualityBoard(factoryId, workshopIds)
  const qChips: GlanceChip[] = q.pareto.slice(0, 3).map((p, i) => ({
    label: p.defect,
    tone: i === 0 ? ('alarm' as const) : ('idle' as const),
  }))
  const qualityGlance: ScreenGlance = {
    key: 'quality',
    state: q.kpis.overdueNcr > 0 ? 'alarm' : q.kpis.defectRatePct > q.kpis.redLinePct ? 'idle' : 'run',
    stateLabel:
      q.kpis.overdueNcr > 0
        ? `${q.kpis.overdueNcr} 条 NCR 超期`
        : q.kpis.defectRatePct > q.kpis.redLinePct
          ? '不良率越线'
          : '质量平稳',
    stats: [
      { label: '批次合格率', value: `${q.kpis.batchPassRate}%`, tone: q.kpis.batchPassRate >= 97 ? 'ok' : 'warn' },
      { label: '待处置 NCR', value: `${q.kpis.openNcr} 单`, tone: q.kpis.overdueNcr > 0 ? 'warn' : undefined },
      { label: '检验积压', value: `${q.kpis.inspectionBacklog} 项` },
    ],
    chipsLabel: '缺陷 TOP',
    chips: qChips.length > 0 ? qChips : [{ label: '无在册缺陷', tone: 'run' }],
  }

  return [workshopGlance, warehouseGlance, qualityGlance]
}
