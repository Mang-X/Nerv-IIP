// 设备监控 mock 聚合（MAN-317）：设备清单从 masterdata 真实汇总，状态画像稳定
// （不随轮询跳变），数量/进度类微抖。🟠 计数/时长等待 #570 真实聚合端点。
import type {
  DeviceCell,
  DeviceDetail,
  DeviceParamBrief,
  DeviceParamSeries,
  DeviceParamsTick,
  DeviceState,
  EquipmentOverview,
  InspectionRow,
  OpenAlarmRow,
  ParamKind,
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

// 稳定状态画像（按设备名，不随轮询跳变）；未列出的按散点规则/默认运行。
// 叙事与工厂屏一致：电芯线卷绕机报警、涂装换型待机、总装一线待修、焊装二线断线。
interface DeviceProfile {
  state: DeviceState
  block?: string
  sourceFresh?: boolean
}
const DEVICE_PROFILES: Record<string, DeviceProfile> = {
  '卷绕机 1#': { state: 'alarm', block: '急停触发 · 安全门未复位' },
  '前处理线体': { state: 'idle', block: '换型待机' },
  '电泳槽': { state: 'idle', block: '换型待机' },
  '合装举升机': { state: 'down', block: '等待维修 · 液压异常' },
  '输送滚床 2#': { state: 'offline', sourceFresh: false },
}
/** 大规模下的稳定散点：每 16 台落一台计划保养待机（确定性索引，轮询不跳变）。 */
function scatterIdle(index: number): boolean {
  return index % 16 === 5
}

const STATE_LABELS: Record<DeviceState, string> = {
  run: '运行',
  idle: '待机',
  down: '停机',
  alarm: '报警',
  offline: '断线',
}

// —— 关键参数模板库（按设备类型关键词匹配；🟠 演示数据流，historian 待 #570）——
interface ParamSpec {
  label: string
  base: number
  amp: number
  dp: number
  unit: string
  kind: ParamKind
  range: string
}
const PARAM_LIBRARY: { match: RegExp; specs: ParamSpec[] }[] = [
  {
    match: /机器人|机械手/,
    specs: [
      { label: '轴温', base: 52, amp: 6, dp: 0, unit: '℃', kind: 'temp', range: '≤ 75℃' },
      { label: '伺服电流', base: 18, amp: 3, dp: 1, unit: 'A', kind: 'current', range: '≤ 30A' },
      { label: '循环节拍', base: 58, amp: 5, dp: 1, unit: 's', kind: 'cycle', range: '55–65s' },
      { label: '重复精度', base: 0.05, amp: 0.02, dp: 2, unit: 'mm', kind: 'vibration', range: '≤ 0.10mm' },
    ],
  },
  {
    match: /炉|烘|化成|干燥|电泳/,
    specs: [
      { label: '工作温度', base: 182, amp: 8, dp: 0, unit: '℃', kind: 'temp', range: '170–195℃' },
      { label: '温度均匀性', base: 3.2, amp: 0.8, dp: 1, unit: '℃', kind: 'temp', range: '≤ 5℃' },
      { label: '风机频率', base: 42, amp: 3, dp: 0, unit: 'Hz', kind: 'speed', range: '35–50Hz' },
      { label: '班次能耗', base: 86, amp: 8, dp: 0, unit: 'kWh', kind: 'energy', range: '—' },
    ],
  },
  {
    match: /压机|注塑/,
    specs: [
      { label: '成形压力', base: 780, amp: 40, dp: 0, unit: 'T', kind: 'pressure', range: '≤ 850T' },
      { label: '油温', base: 48, amp: 5, dp: 0, unit: '℃', kind: 'temp', range: '≤ 60℃' },
      { label: '循环节拍', base: 12.5, amp: 1.5, dp: 1, unit: 's', kind: 'cycle', range: '11–15s' },
      { label: '振动速度', base: 2.6, amp: 0.7, dp: 1, unit: 'mm/s', kind: 'vibration', range: '≤ 4.5mm/s' },
    ],
  },
  {
    match: /拧紧/,
    specs: [
      { label: '拧紧扭矩', base: 128, amp: 6, dp: 0, unit: 'Nm', kind: 'torque', range: '120–140Nm' },
      { label: '一次达标率', base: 99.1, amp: 0.6, dp: 1, unit: '%', kind: 'level', range: '≥ 98%' },
      { label: '循环节拍', base: 46, amp: 4, dp: 0, unit: 's', kind: 'cycle', range: '42–52s' },
      { label: '伺服电流', base: 12, amp: 2, dp: 1, unit: 'A', kind: 'current', range: '≤ 20A' },
    ],
  },
  {
    match: /AGV/,
    specs: [
      { label: '电池电量', base: 76, amp: 10, dp: 0, unit: '%', kind: 'level', range: '≥ 30%' },
      { label: '行驶速度', base: 1.2, amp: 0.3, dp: 1, unit: 'm/s', kind: 'speed', range: '≤ 1.8m/s' },
      { label: '今日任务', base: 26, amp: 6, dp: 0, unit: '单', kind: 'cycle', range: '—' },
      { label: '驱动温度', base: 44, amp: 5, dp: 0, unit: '℃', kind: 'temp', range: '≤ 65℃' },
    ],
  },
  {
    match: /泵|槽|加注|注液|风机|空调|清洗/,
    specs: [
      { label: '介质流量', base: 28, amp: 5, dp: 0, unit: 'L/min', kind: 'flow', range: '22–35L/min' },
      { label: '工作压力', base: 0.62, amp: 0.06, dp: 2, unit: 'MPa', kind: 'pressure', range: '0.5–0.75MPa' },
      { label: '液位', base: 72, amp: 8, dp: 0, unit: '%', kind: 'level', range: '≥ 40%' },
      { label: '介质温度', base: 46, amp: 6, dp: 0, unit: '℃', kind: 'temp', range: '≤ 60℃' },
    ],
  },
]
const DEFAULT_SPECS: ParamSpec[] = [
  { label: '主轴转速', base: 1450, amp: 80, dp: 0, unit: 'rpm', kind: 'speed', range: '1300–1600rpm' },
  { label: '电机温度', base: 58, amp: 8, dp: 0, unit: '℃', kind: 'temp', range: '≤ 80℃' },
  { label: '负载电流', base: 32, amp: 5, dp: 1, unit: 'A', kind: 'current', range: '≤ 45A' },
  { label: '振动速度', base: 2.4, amp: 0.7, dp: 1, unit: 'mm/s', kind: 'vibration', range: '≤ 4.5mm/s' },
]
function specsFor(name: string): ParamSpec[] {
  return PARAM_LIBRARY.find((e) => e.match.test(name))?.specs ?? DEFAULT_SPECS
}

function jitterF(base: number, amp: number, dp: number): number {
  return +(base + (Math.random() - 0.5) * amp).toFixed(dp)
}
function seriesOf(spec: ParamSpec, n = 12): number[] {
  return Array.from({ length: n }, () => jitterF(spec.base, spec.amp, spec.dp))
}

/** 状态修饰后的参数序列：报警设备温度类超限红、首参归零黄；停机首参归零；
 *  待机首参降到 30%；断线无数据（spark 空 → 图示虚线占位）。 */
export function paramSeriesFor(name: string, state: DeviceState): DeviceParamSeries[] {
  const specs = specsFor(name)
  const tempIdx = specs.findIndex((s) => s.kind === 'temp')
  return specs.map((spec, i) => {
    if (state === 'offline') {
      return { label: spec.label, value: null, unit: spec.unit, kind: spec.kind, range: spec.range, spark: [] }
    }
    let eff = spec
    let tone: 'warn' | 'bad' | undefined
    if (state === 'alarm') {
      if (i === tempIdx) {
        eff = { ...spec, base: +(spec.base * 1.5).toFixed(spec.dp), amp: spec.amp * 0.6 }
        tone = 'bad'
      } else if (i === 0) {
        eff = { ...spec, base: 0, amp: 0 }
        tone = 'warn'
      }
    } else if (i === 0 && state === 'down') {
      eff = { ...spec, base: 0, amp: 0 }
      tone = 'warn'
    } else if (i === 0 && state === 'idle') {
      eff = { ...spec, base: +(spec.base * 0.3).toFixed(spec.dp), amp: spec.amp * 0.4 }
    }
    const spark = seriesOf(eff)
    return {
      label: spec.label,
      value: spark[spark.length - 1],
      unit: spec.unit,
      kind: spec.kind,
      range: spec.range,
      spark,
      tone,
    }
  })
}

/** 格上简版：取前 2 个参数，值并入单位；断线为「—」。 */
function paramBriefs(name: string, state: DeviceState): DeviceParamBrief[] {
  return paramSeriesFor(name, state)
    .slice(0, 2)
    .map((p) => ({
      label: p.label,
      value: p.value === null ? '—' : `${p.value}${p.unit}`,
      kind: p.kind,
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
  let gi = 0
  const devices: DeviceCell[] = batches.flatMap((batch) =>
    batch.map((id) => {
      const d = byId.get(id)!
      const p = DEVICE_PROFILES[d.name]
      let state: DeviceState = p?.state ?? 'run'
      let block = p?.block
      if (!p && scatterIdle(gi)) {
        state = 'idle'
        block = '计划保养中'
      }
      gi += 1
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
        block,
        sourceFresh: p?.sourceFresh ?? true,
        params: paramBriefs(d.name, state),
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
            name: '卷绕机 1# 急停触发',
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
            name: '合装举升机 液压压力异常',
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
      device: '卷绕机 1#',
      issue: '急停复位与安全回路检查',
      progress: clamp(jitter(16, 6), 5, 30),
      stage: '已接单',
      overdue: false,
      awaitingConfirm: false,
    },
    {
      wo: 'WO-1929',
      device: '合装举升机',
      issue: '液压站压力异常排查',
      progress: clamp(jitter(62, 6), 45, 80),
      stage: '维修中',
      overdue: true,
      awaitingConfirm: false,
    },
    {
      wo: 'WO-1917',
      device: '800T 压机 1#',
      issue: '润滑系统补油',
      progress: 100,
      stage: '待验证',
      overdue: false,
      awaitingConfirm: true,
    },
    {
      wo: 'WO-1912',
      device: '涂胶机',
      issue: '胶泵密封件更换',
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
    { device: '800T 压机 1#', task: '月度精度校准', due: '今日 16:00', state: 'due' },
    { device: '空调送风机组', task: '过滤网更换', due: '超期 1 天', state: 'overdue' },
    { device: '拧紧工作站 3#', task: '导轨润滑', due: '已完成 11:20', state: 'done' },
  ]

  const inspections: InspectionRow[] = [
    { time: clock(jitter(30, 8)), device: '卷绕机 2#', item: '气压/温度点检', by: '孙立军', result: '合格' },
    { time: clock(jitter(55, 8)), device: '焊接机器人 R01', item: '焊接参数抽检', by: '王海涛', result: '合格' },
    { time: clock(jitter(85, 8)), device: '合装举升机', item: '液压油位点检', by: '赵敏', result: '异常' },
    { time: clock(jitter(110, 10)), device: '1000T 压机', item: '模具状态点检', by: '李国强', result: '合格' },
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
  const wc = WORK_CENTERS.find((w) => w.lineId === device.lineId)
  const manager = WORKSHOPS.find((w) => w.id === device.workshopId)?.managerName ?? '—'
  // 单机可靠性：有故障样本（报警/停机中）才有值，否则 null 显「—」
  const hasIssue = device.state === 'alarm' || device.state === 'down'
  return {
    device,
    workCenterName: wc?.name ?? '—',
    managerName: manager,
    params: paramSeriesFor(device.name, device.state),
    repairs: ov.repairs.filter((r) => r.device === device.name),
    pmTasks: ov.pmTasks.filter((t) => t.device === device.name),
    inspections: ov.inspections.filter((i) => i.device === device.name),
    mtbfHours: hasIssue ? clamp(jitter(52, 10), 24, 90) : null,
    mttrMinutes: hasIssue ? clamp(jitter(45, 10), 20, 80) : null,
  }
}

/** 参数快刷 tick（高频轮询专用）：只重算格上参数，不动状态/计数。
 *  deviceIds 传入「当前视野内」的设备集 —— 视野外不产生数据变化（性能约定，
 *  真实端点即按可见集订阅）；缺省为全量。 */
export function buildParamsTick(
  factoryId = 'F01',
  workshopIds: string[] | 'all' = 'all',
  deviceIds?: string[],
): DeviceParamsTick {
  const ov = buildEquipmentOverview(factoryId, workshopIds)
  const want = deviceIds ? new Set(deviceIds) : null
  return Object.fromEntries(
    ov.devices.filter((d) => !want || want.has(d.id)).map((d) => [d.id, d.params]),
  )
}
