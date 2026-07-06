// 门厅 mock 聚合：层级计数从 masterdata 真实汇总，动态指标受控抖动。
// 🟠 产量/达成/健康度等待 #570 真实聚合端点，接入后由 fetchers/launcher.ts 单点切换。
import type { GlanceChip, LauncherSummary, ScreenGlance } from '@/data/contracts/launcher'
import { jitter } from './fixtures'
import { devicesByWorkshop, linesByWorkshop, workshopsByFactory } from './masterdata'

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
  ]

  return {
    factoryId,
    kpis: { output, achievement, runningDevices, totalDevices, openAlarms, health },
    glances,
  }
}
