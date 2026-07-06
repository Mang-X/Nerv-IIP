// 设备监控 mock 聚合（MAN-317）：设备清单从 masterdata 真实汇总，状态画像稳定
// （不随轮询跳变），数量/进度类微抖。🟠 计数/时长等待 #570 真实聚合端点。
import type {
  DeviceCell,
  DeviceState,
  EquipmentOverview,
  InspectionRow,
  OpenAlarmRow,
  PmTask,
  Reliability,
  RepairOrder,
  StateCounts,
} from '@/data/contracts/equipment'
import { clock, jitter } from './fixtures'
import { devicesByWorkshop, LINES, workshopsByFactory } from './masterdata'

function clamp(n: number, lo: number, hi: number): number {
  return Math.min(hi, Math.max(lo, n))
}

/** ⚠️ 真实设备状态端点 deviceAssetIds ≤ 50/批 —— 分批取数再合并（mock 同形状演练）。 */
export const DEVICE_BATCH_LIMIT = 50
export function chunkIds(ids: string[], size = DEVICE_BATCH_LIMIT): string[][] {
  const out: string[][] = []
  for (let i = 0; i < ids.length; i += size) out.push(ids.slice(i, i + size))
  return out
}

// 稳定状态画像：键 = `${lineId}:${主机|辅机}`；未列出的默认运行。
// 叙事与工厂屏一致：电芯线主机报警、涂装换型待机、总装一线待修、焊装二线辅机断线。
interface DeviceProfile {
  state: DeviceState
  block?: string
  sourceFresh?: boolean
}
const DEVICE_PROFILES: Record<string, DeviceProfile> = {
  'LN-BAT-1:主机': { state: 'alarm', block: '急停触发 · 安全门未复位' },
  'LN-PAINT-1:主机': { state: 'idle', block: '换型待机' },
  'LN-PAINT-1:辅机': { state: 'idle', block: '换型待机' },
  'LN-ASSY-1:主机': { state: 'down', block: '等待维修 · 液压异常' },
  'LN-WELD-2:辅机': { state: 'offline', sourceFresh: false },
}

const STATE_LABELS: Record<DeviceState, string> = {
  run: '运行',
  idle: '待机',
  down: '停机',
  alarm: '报警',
  offline: '断线',
}

export function buildEquipmentOverview(
  factoryId = 'F01',
  workshopIds: string[] | 'all' = 'all',
): EquipmentOverview {
  const factoryWorkshops = workshopsByFactory(factoryId)
  const workshops =
    workshopIds === 'all' ? factoryWorkshops : factoryWorkshops.filter((w) => workshopIds.includes(w.id))
  const rawDevices = workshops.flatMap((w) => devicesByWorkshop(w.id))

  // 分批约束演练：真实端点每批 ≤50，逐批取状态后合并
  const batches = chunkIds(rawDevices.map((d) => d.id))
  const byId = new Map(rawDevices.map((d) => [d.id, d]))
  const lineNameOf = (lineId: string) => LINES.find((l) => l.id === lineId)?.name ?? lineId

  const devices: DeviceCell[] = batches.flatMap((batch) =>
    batch.map((id) => {
      const d = byId.get(id)!
      const kind = d.name.includes('主机') ? '主机' : '辅机'
      const p = DEVICE_PROFILES[`${d.lineId}:${kind}`]
      const state: DeviceState = p?.state ?? 'run'
      return {
        id: d.id,
        code: d.code,
        name: d.name,
        lineName: lineNameOf(d.lineId),
        state,
        stateLabel: STATE_LABELS[state],
        block: p?.block,
        sourceFresh: p?.sourceFresh ?? true,
      }
    }),
  )

  const counts: StateCounts = { run: 0, idle: 0, down: 0, alarm: 0, offline: 0 }
  for (const d of devices) counts[d.state]++

  // —— 未恢复报警表（级别·未恢复时长·已触发工单 ✅ 闭环）——
  const hasAlarm = counts.alarm > 0
  const alarms: OpenAlarmRow[] = [
    ...(hasAlarm
      ? [
          {
            time: clock(jitter(26, 6)),
            line: '电芯线',
            level: 'sev' as const,
            name: '主机急停触发',
            wo: 'WO-1934',
            status: `未恢复 ${clamp(jitter(26, 6), 12, 45)} min`,
          },
        ]
      : []),
    ...(counts.down > 0
      ? [
          {
            time: clock(jitter(48, 8)),
            line: '总装一线',
            level: 'sev' as const,
            name: '液压系统压力异常',
            wo: 'WO-1929',
            status: `维修中 ${clamp(jitter(41, 8), 20, 70)} min`,
          },
        ]
      : []),
    {
      time: clock(jitter(64, 10)),
      line: workshops[0] ? lineNameOf(devicesByWorkshop(workshops[0].id)[0]?.lineId ?? '') : '冲压一线',
      level: 'gen',
      name: '润滑油位低',
      wo: 'WO-1917',
      status: '已恢复待确认',
    },
    {
      time: clock(jitter(80, 10)),
      line: '涂装线',
      level: 'gen',
      name: '烘房温度波动',
      wo: 'WO-1921',
      status: `观察中 ${clamp(jitter(12, 5), 5, 25)} min`,
    },
  ]

  // —— 维修工单进度（含超时 🟡 与已恢复待确认 ✅）——
  const repairs: RepairOrder[] = [
    {
      wo: 'WO-1934',
      device: '电芯线主机',
      issue: '急停复位与安全回路检查',
      progress: clamp(jitter(16, 6), 5, 30),
      stage: '已接单',
      overdue: false,
      awaitingConfirm: false,
    },
    {
      wo: 'WO-1929',
      device: '总装一线主机',
      issue: '液压站压力异常排查',
      progress: clamp(jitter(62, 6), 45, 80),
      stage: '维修中',
      overdue: true,
      awaitingConfirm: false,
    },
    {
      wo: 'WO-1917',
      device: '冲压一线主机',
      issue: '润滑系统补油',
      progress: 100,
      stage: '待验证',
      overdue: false,
      awaitingConfirm: true,
    },
    {
      wo: 'WO-1912',
      device: '焊装一线辅机',
      issue: '焊枪电极更换',
      progress: clamp(jitter(88, 5), 70, 98),
      stage: '维修中',
      overdue: false,
      awaitingConfirm: false,
    },
  ]

  // —— 可靠性：小样本工厂（<6 台）MTBF/MTTR 无样本 → null，页面显「—」 ——
  const smallSample = devices.length < 6
  const reliability: Reliability = {
    availability: clamp(jitter(84, 5), 70, 96), // 🟠 待 #570；渲染标注 ≈可用率
    mtbfHours: smallSample ? null : clamp(jitter(76, 10), 48, 120),
    mttrMinutes: smallSample ? null : clamp(jitter(42, 10), 20, 80),
    failures: smallSample ? 0 : 3,
    repairs: smallSample ? 0 : 2,
  }

  const pmTasks: PmTask[] = [
    { device: '冲压一线主机', task: '月度精度校准', due: '今日 16:00', state: 'due' },
    { device: '涂装线辅机', task: '过滤网更换', due: '超期 1 天', state: 'overdue' },
    { device: '总装二线主机', task: '导轨润滑', due: '已完成 11:20', state: 'done' },
  ]

  const inspections: InspectionRow[] = [
    { time: clock(jitter(30, 8)), device: '电芯线辅机', item: '气压/温度点检', by: '孙立军', result: '合格' },
    { time: clock(jitter(55, 8)), device: '焊装一线主机', item: '焊接参数抽检', by: '王海涛', result: '合格' },
    { time: clock(jitter(85, 8)), device: '总装一线主机', item: '液压油位点检', by: '赵敏', result: '异常' },
    { time: clock(jitter(110, 10)), device: '冲压二线辅机', item: '模具状态点检', by: '李国强', result: '合格' },
  ]

  return { factoryId, counts, devices, alarms, repairs, reliability, pmTasks, inspections }
}
