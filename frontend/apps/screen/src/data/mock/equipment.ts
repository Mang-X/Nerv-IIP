// 设备监控 mock 聚合（MAN-317）：设备清单从 masterdata 真实汇总，状态画像稳定
// （不随轮询跳变），数量/进度类微抖。🟠 计数/时长等待 #570 真实聚合端点。
import type {
  DeviceCell,
  DeviceDetail,
  DeviceParamBrief,
  DeviceParamSeries,
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
import { devicesByWorkshop, LINES, WORK_CENTERS, WORKSHOPS, workshopsByFactory } from './masterdata'

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

// —— 关键参数（🟠 演示数据流，historian/实时采集接入待 #570）——
interface ParamSpec {
  label: string
  base: number
  amp: number
  dp: number
  unit: string
}
const HOST_PARAMS: ParamSpec[] = [
  { label: '主轴转速', base: 1450, amp: 80, dp: 0, unit: 'rpm' },
  { label: '电机温度', base: 58, amp: 8, dp: 0, unit: '℃' },
  { label: '负载电流', base: 32, amp: 5, dp: 1, unit: 'A' },
  { label: '振动速度', base: 2.4, amp: 0.7, dp: 1, unit: 'mm/s' },
]
const AUX_PARAMS: ParamSpec[] = [
  { label: '气源压力', base: 0.62, amp: 0.06, dp: 2, unit: 'MPa' },
  { label: '液压压力', base: 8.5, amp: 0.9, dp: 1, unit: 'MPa' },
  { label: '油温', base: 46, amp: 6, dp: 0, unit: '℃' },
  { label: '冷却流量', base: 28, amp: 5, dp: 0, unit: 'L/min' },
]

function jitterF(base: number, amp: number, dp: number): number {
  return +(base + (Math.random() - 0.5) * amp).toFixed(dp)
}
function seriesOf(spec: ParamSpec, n = 12): number[] {
  return Array.from({ length: n }, () => jitterF(spec.base, spec.amp, spec.dp))
}

function deviceKind(name: string): '主机' | '辅机' {
  return name.includes('主机') ? '主机' : '辅机'
}

/** 状态修饰后的参数序列：报警主机超温、急停/停机转速归零、断线无数据（spark 空）。 */
export function paramSeriesFor(kind: '主机' | '辅机', state: DeviceState): DeviceParamSeries[] {
  const specs = kind === '主机' ? HOST_PARAMS : AUX_PARAMS
  return specs.map((spec, i) => {
    if (state === 'offline') return { label: spec.label, value: null, unit: spec.unit, spark: [] }
    let eff = spec
    let tone: 'warn' | 'bad' | undefined
    if (kind === '主机' && i === 0 && state !== 'run') {
      // 急停/停机/待机：主轴转速归零（待机属正常，不标色）
      eff = { ...spec, base: 0, amp: 0 }
      tone = state === 'idle' ? undefined : 'warn'
    }
    if (kind === '主机' && i === 1 && state === 'alarm') {
      eff = { ...spec, base: 92, amp: 4 } // 电机温度超限
      tone = 'bad'
    }
    const spark = seriesOf(eff)
    return { label: spec.label, value: spark[spark.length - 1], unit: spec.unit, spark, tone }
  })
}

/** 格上简版：取前 2 个参数，值并入单位；断线为「—」。 */
function paramBriefs(kind: '主机' | '辅机', state: DeviceState): DeviceParamBrief[] {
  return paramSeriesFor(kind, state)
    .slice(0, 2)
    .map((p) => ({
      label: p.label,
      value: p.value === null ? '—' : `${p.value}${p.unit}`,
      tone: p.tone,
    }))
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

  const workshopNameOf = (id: string) => WORKSHOPS.find((w) => w.id === id)?.name ?? id
  const devices: DeviceCell[] = batches.flatMap((batch) =>
    batch.map((id) => {
      const d = byId.get(id)!
      const kind = deviceKind(d.name)
      const p = DEVICE_PROFILES[`${d.lineId}:${kind}`]
      const state: DeviceState = p?.state ?? 'run'
      return {
        id: d.id,
        code: d.code,
        name: d.name,
        lineId: d.lineId,
        lineName: lineNameOf(d.lineId),
        workshopId: d.workshopId,
        workshopName: workshopNameOf(d.workshopId),
        state,
        stateLabel: STATE_LABELS[state],
        block: p?.block,
        sourceFresh: p?.sourceFresh ?? true,
        params: paramBriefs(kind, state),
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

/** 设备详情（点击按需取）：与全景墙同源画像 + 全参数趋势 + 该设备的保养维修档案。 */
export function buildDeviceDetail(
  deviceId: string,
  factoryId = 'F01',
  workshopIds: string[] | 'all' = 'all',
): DeviceDetail | null {
  const ov = buildEquipmentOverview(factoryId, workshopIds)
  const device = ov.devices.find((d) => d.id === deviceId)
  if (!device) return null
  const kind = deviceKind(device.name)
  const wc = WORK_CENTERS.find((w) => w.lineId === device.lineId)
  const manager = WORKSHOPS.find((w) => w.id === device.workshopId)?.managerName ?? '—'
  // 单机可靠性：有故障样本（报警/停机中）才有值，否则 null 显「—」
  const hasIssue = device.state === 'alarm' || device.state === 'down'
  return {
    device,
    workCenterName: wc?.name ?? '—',
    managerName: manager,
    params: paramSeriesFor(kind, device.state),
    repairs: ov.repairs.filter((r) => r.device === device.name),
    pmTasks: ov.pmTasks.filter((t) => t.device === device.name),
    inspections: ov.inspections.filter((i) => i.device === device.name),
    mtbfHours: hasIssue ? clamp(jitter(52, 10), 24, 90) : null,
    mttrMinutes: hasIssue ? clamp(jitter(45, 10), 20, 80) : null,
  }
}
