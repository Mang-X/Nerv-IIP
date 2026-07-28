// 设备监控 mock 聚合（MAN-317）：设备清单从 masterdata 真实汇总，状态画像稳定
// （不随轮询跳变），数量/进度类微抖。🟠 计数/时长等待 #570 真实聚合端点。
//
// ⚠️ 参数模板、报警阈值、点位中文名**逐条取自后端遥测种子**
// `backend/services/Business/IndustrialTelemetry/.../Seed/WorldHistoryDeviceSpec.cs`
// （8 个设备类别的 base/swing/阈值）与 `WorldHistoryControlCommandSpec.TagDisplayName`。
// 大屏上跳出来的「主轴温度 78℃ 超限」必须和 PC 控制台看到的是同一个阈值。
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
import { clock, jitter, seq } from './fixtures'
import type { DeviceCategory } from './masterdata'
import {
  DEFAULT_FACTORY_ID,
  deviceLabel,
  DEVICES,
  devicesByWorkshop,
  LINES,
  WORK_CENTERS,
  WORKSHOPS,
  workshopsByFactory,
} from './masterdata'

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

// 稳定状态画像（按设备编码，不随轮询跳变）；未列出的按散点规则/默认运行。
// 全厂当班只有一起红：机加车间 DEV-CNC-03 振动超限（阈值 6.5 mm/s）——
// 异常是例外，46 台设备同时冒出十几条报警本身就是假数据的味道。
interface DeviceProfile {
  state: DeviceState
  block?: string
  sourceFresh?: boolean
}
const DEVICE_PROFILES: Record<string, DeviceProfile> = {
  'DEV-CNC-03': { state: 'alarm', block: '振动超限 6.9 mm/s · 主轴轴承异常' },
  'DEV-CTG-02': { state: 'down', block: '等待维修 · 循环泵机械密封渗漏' },
  'DEV-ASM-05': { state: 'idle', block: '换型待机 · P1→S1 切换' },
  'DEV-ASM-06': { state: 'idle', block: '换型待机 · P1→S1 切换' },
  'DEV-AUX-06': { state: 'offline', sourceFresh: false },
}
/** 稳定散点：确定性索引落一台计划保养待机（46 台里恰好命中 DEV-GRD-03，轮询不跳变）。 */
export function scatterIdle(index: number): boolean {
  return index % 31 === 12
}

const STATE_LABELS: Record<DeviceState, string> = {
  run: '运行',
  idle: '待机',
  down: '停机',
  alarm: '报警',
  offline: '断线',
}

// —— 采集点位模板（按设备类别；base/swing/阈值与后端遥测种子逐条一致）——
interface ParamSpec {
  label: string
  base: number
  /** 正常波动幅（峰峰值） */
  amp: number
  dp: number
  unit: string
  kind: ParamKind
  range: string
  /** 该点位报警时的越限观测值；设备报警时用它替代 base（tone=bad） */
  alarmValue?: number
  /** 设备停机/待机时该点位归零（转速、节拍这类「动作量」） */
  stopsAtZero?: boolean
}

const PARAM_LIBRARY: Record<DeviceCategory, ParamSpec[]> = {
  cnc: [
    {
      label: '主轴温度',
      base: 52,
      amp: 9,
      dp: 0,
      unit: '℃',
      kind: 'temp',
      range: '≤ 78℃',
      alarmValue: 81,
    },
    {
      label: '振动',
      base: 2.6,
      amp: 0.9,
      dp: 1,
      unit: 'mm/s',
      kind: 'vibration',
      range: '≤ 6.5mm/s',
      alarmValue: 6.9,
    },
    {
      label: '主轴转速',
      base: 2400,
      amp: 500,
      dp: 0,
      unit: 'rpm',
      kind: 'speed',
      range: '≤ 3600rpm',
      stopsAtZero: true,
    },
  ],
  grinder: [
    {
      label: '振动',
      base: 2.4,
      amp: 0.8,
      dp: 1,
      unit: 'mm/s',
      kind: 'vibration',
      range: '≤ 5.5mm/s',
      alarmValue: 5.8,
    },
    {
      label: '砂轮转速',
      base: 1500,
      amp: 160,
      dp: 0,
      unit: 'rpm',
      kind: 'speed',
      range: '≤ 1900rpm',
      stopsAtZero: true,
    },
  ],
  'welding-robot': [
    {
      label: '焊接电流',
      base: 185,
      amp: 35,
      dp: 0,
      unit: 'A',
      kind: 'current',
      range: '≤ 280A',
      alarmValue: 296,
      stopsAtZero: true,
    },
    {
      label: '温度',
      base: 56,
      amp: 9,
      dp: 0,
      unit: '℃',
      kind: 'temp',
      range: '≤ 85℃',
      alarmValue: 89,
    },
  ],
  'assembly-station': [
    {
      label: '压装力',
      base: 12.5,
      amp: 2.5,
      dp: 1,
      unit: 'kN',
      kind: 'pressure',
      range: '≤ 17.5kN',
      alarmValue: 18.4,
    },
    {
      label: '节拍计数',
      base: 28,
      amp: 6,
      dp: 0,
      unit: '件/h',
      kind: 'cycle',
      range: '—',
      stopsAtZero: true,
    },
  ],
  'test-bench': [
    {
      label: '阻尼力',
      base: 980,
      amp: 140,
      dp: 0,
      unit: 'N',
      kind: 'torque',
      range: '≤ 1450N',
      alarmValue: 1512,
    },
  ],
  coating: [
    {
      label: '槽液温度',
      base: 29,
      amp: 2,
      dp: 1,
      unit: '℃',
      kind: 'temp',
      range: '≤ 34℃',
      alarmValue: 35.6,
    },
    {
      label: '槽液 PH',
      base: 6.2,
      amp: 0.25,
      dp: 2,
      unit: 'pH',
      kind: 'level',
      range: '≥ 5.6pH',
      alarmValue: 5.42,
    },
  ],
  'packaging-line': [
    {
      label: '节拍计数',
      base: 55,
      amp: 10,
      dp: 0,
      unit: '箱/h',
      kind: 'cycle',
      range: '—',
      stopsAtZero: true,
    },
  ],
  utility: [
    {
      label: '气源压力',
      base: 7.2,
      amp: 0.35,
      dp: 2,
      unit: 'bar',
      kind: 'pressure',
      range: '≥ 6.0bar',
      alarmValue: 5.72,
    },
    {
      label: '温度',
      base: 66,
      amp: 7,
      dp: 0,
      unit: '℃',
      kind: 'temp',
      range: '≤ 92℃',
      alarmValue: 95,
    },
  ],
}

const categoryByCode = new Map(DEVICES.map((d) => [d.code, d.category]))

function specsFor(deviceCode: string): ParamSpec[] {
  return PARAM_LIBRARY[categoryByCode.get(deviceCode) ?? 'cnc']
}

function jitterF(base: number, amp: number, dp: number): number {
  return +(base + (Math.random() - 0.5) * amp).toFixed(dp)
}
function seriesOf(spec: ParamSpec, base: number, amp: number, n = 12): number[] {
  return Array.from({ length: n }, () => jitterF(base, amp, spec.dp))
}

/** 状态修饰后的参数序列：报警设备的越限点位走 alarmValue（红），动作量归零（黄）；
 *  停机动作量归零；待机动作量降到 30%；断线无数据（spark 空 → 图示虚线占位）。 */
export function paramSeriesFor(deviceCode: string, state: DeviceState): DeviceParamSeries[] {
  const specs = specsFor(deviceCode)
  return specs.map((spec) => {
    if (state === 'offline') {
      return {
        label: spec.label,
        value: null,
        unit: spec.unit,
        kind: spec.kind,
        range: spec.range,
        spark: [],
      }
    }
    let base = spec.base
    let amp = spec.amp
    let tone: 'warn' | 'bad' | undefined
    if (state === 'alarm' && spec.alarmValue !== undefined) {
      base = spec.alarmValue
      amp = spec.amp * 0.4
      tone = 'bad'
    } else if (state === 'alarm' && spec.stopsAtZero) {
      base = 0
      amp = 0
      tone = 'warn'
    } else if (state === 'down' && spec.stopsAtZero) {
      base = 0
      amp = 0
      tone = 'warn'
    } else if (state === 'idle' && spec.stopsAtZero) {
      base = +(spec.base * 0.3).toFixed(spec.dp)
      amp = spec.amp * 0.4
    }
    const spark = seriesOf(spec, base, amp)
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
function paramBriefs(deviceCode: string, state: DeviceState): DeviceParamBrief[] {
  return paramSeriesFor(deviceCode, state)
    .slice(0, 2)
    .map((p) => ({
      label: p.label,
      value: p.value === null ? '—' : `${p.value}${p.unit}`,
      kind: p.kind,
      tone: p.tone,
    }))
}

export function buildEquipmentOverview(
  factoryId = DEFAULT_FACTORY_ID,
  workshopIds: string[] | 'all' = 'all',
): EquipmentOverview {
  const factoryWorkshops = workshopsByFactory(factoryId)
  const workshops =
    workshopIds === 'all'
      ? factoryWorkshops
      : factoryWorkshops.filter((w) => workshopIds.includes(w.id))
  const rawDevices = workshops.flatMap((w) => devicesByWorkshop(w.id))

  // 分批约束演练：真实端点每批 ≤50，逐批取状态后合并
  const batches = chunkIds(rawDevices.map((d) => d.id))
  const byId = new Map(rawDevices.map((d) => [d.id, d]))
  const lineNameOf = (lineId: string) => LINES.find((l) => l.id === lineId)?.name ?? lineId

  const workshopNameOf = (id: string) => WORKSHOPS.find((w) => w.id === id)?.shortName ?? id
  let gi = 0
  const devices: DeviceCell[] = batches.flatMap((batch) =>
    batch.map((id) => {
      const d = byId.get(id)!
      const p = DEVICE_PROFILES[d.code]
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
        // 同型号设备一条线上有多台（DEV-CNC-01/02/03 都是 CK6150）——
        // 显示名带编码，大屏上才能一眼看出报警的是哪一台。
        name: deviceLabel(d),
        lineId: d.lineId,
        lineName: lineNameOf(d.lineId),
        workshopId: d.workshopId,
        workshopName: workshopNameOf(d.workshopId),
        state,
        stateLabel: STATE_LABELS[state],
        block,
        sourceFresh: p?.sourceFresh ?? true,
        params: paramBriefs(d.code, state),
      }
    }),
  )

  const counts: StateCounts = { run: 0, idle: 0, down: 0, alarm: 0, offline: 0 }
  for (const d of devices) counts[d.state]++

  // —— 可见性过滤基础：scope 收窄后各档案面板同步收窄 ——
  const visibleWorkshopIds = new Set(workshops.map((w) => w.id))
  const visibleLineNames = new Set(
    LINES.filter((l) => visibleWorkshopIds.has(l.workshopId)).map((l) => l.name),
  )
  const visibleDeviceNames = new Set(devices.map((d) => d.name))

  // —— 未恢复报警表（级别·未恢复时长·已触发维修工单 ✅ 闭环）——
  // 维修工单走设定集 §9 的 `MWO-2026-####` 段（29 周共 120 张，当前号在 011x）。
  const hasAlarm = counts.alarm > 0
  const ALARM_POOL: {
    line: string
    level: 'sev' | 'gen'
    name: string
    minsAgo: number
    status: string
    acked: boolean
    ackBy?: string
  }[] = [
    {
      line: '精磨线',
      level: 'gen',
      name: 'DEV-GRD-02 数控外圆磨床 MK1332 振动接近上限',
      minsAgo: 112,
      status: '已确认 · 待砂轮动平衡',
      acked: true,
      ackBy: '李明辉',
    },
    {
      line: '缸筒一线',
      level: 'gen',
      name: 'DEV-WLD-01 六轴焊接机器人 焊接电流波动偏大',
      minsAgo: 143,
      status: `观察中 ${clamp(jitter(30, 8), 15, 50)} min`,
      acked: false,
    },
    {
      line: '包装线',
      level: 'gen',
      name: 'DEV-AUX-07 螺杆空压机 SA-37 气源压力偏低',
      minsAgo: 168,
      status: '待保养 · 空滤芯堵塞',
      acked: true,
      ackBy: '张玉兰',
    },
    {
      line: '后减装配二线',
      level: 'gen',
      name: 'DEV-ASM-10 减振器装配台 压装力接近上限',
      minsAgo: 226,
      status: '计划传感器标定',
      acked: true,
      ackBy: '李春梅',
    },
  ]
  const alarms: OpenAlarmRow[] = [
    ...(hasAlarm
      ? [
          {
            time: clock(jitter(26, 6)),
            line: '活塞杆一线',
            level: 'sev' as const,
            name: 'DEV-CNC-03 数控车床 CK6150 振动超限（6.9 / 阈值 6.5 mm/s）',
            wo: 'MWO-2026-0118',
            status: `未恢复 ${clamp(jitter(26, 6), 12, 45)} min`,
            // 刚触发、尚无人确认 → 未确认高亮 + 已升级（醒目）。
            acked: false,
            escalated: true,
          },
        ]
      : []),
    ...(counts.down > 0
      ? [
          {
            time: clock(jitter(96, 8)),
            line: '电泳涂装线',
            level: 'sev' as const,
            name: 'DEV-CTG-02 电泳槽 槽液温度越限 35.6℃（阈值 34℃）',
            wo: 'MWO-2026-0116',
            status: `维修中 ${clamp(jitter(96, 8), 80, 130)} min`,
            acked: true,
            ackBy: '刘秀英',
          },
        ]
      : []),
    ...ALARM_POOL.filter((a) => visibleLineNames.has(a.line)).map((a, i) => ({
      time: clock(a.minsAgo + jitter(2, 3)),
      line: a.line,
      level: a.level,
      name: a.name,
      wo: seq('MWO-2026', 114 - i),
      status: a.status,
      acked: a.acked,
      ackBy: a.ackBy,
    })),
  ]

  // —— 维修工单（状态机阶段 + 报修时刻/已历时/SLA + 责任人；按可见设备过滤）——
  // 责任人取 L0 §5 设备部：维修技师 EMP-043..046、设备主管 EMP-042、点检员 EMP-047。
  const REPAIR_POOL: RepairOrder[] = [
    {
      wo: 'MWO-2026-0118',
      device: 'DEV-CNC-03 数控车床 CK6150',
      issue: '主轴轴承振动排查',
      stage: '已派工',
      reportedAt: clock(26),
      elapsedMin: clamp(jitter(26, 4), 15, 40),
      etaText: '预计 2h 内定位',
      overdue: false,
      awaitingConfirm: false,
      assignee: '张红梅',
    },
    {
      wo: 'MWO-2026-0116',
      device: 'DEV-CTG-02 电泳槽',
      issue: '循环泵机械密封更换',
      stage: '维修中',
      reportedAt: clock(96),
      elapsedMin: clamp(jitter(96, 6), 80, 120),
      etaText: '原计划 2h · 已超',
      overdue: true,
      awaitingConfirm: false,
      assignee: '刘秀英',
    },
    {
      wo: 'MWO-2026-0113',
      device: 'DEV-AUX-06 冷冻式干燥机 CD-15',
      issue: 'Modbus 采集链路失联排查',
      stage: '维修中',
      reportedAt: clock(210),
      elapsedMin: clamp(jitter(210, 10), 190, 240),
      etaText: '备件到货后 2h',
      blockedBy: '待备件 · MRO-SEN-04 通讯模块',
      overdue: false,
      awaitingConfirm: false,
      assignee: '陈国庆',
    },
    {
      wo: 'MWO-2026-0110',
      device: 'DEV-GRD-02 数控外圆磨床 MK1332',
      issue: '砂轮主轴动平衡校正',
      stage: '待验证',
      reportedAt: clock(150),
      elapsedMin: clamp(jitter(150, 8), 130, 175),
      etaText: '待点检确认',
      overdue: false,
      awaitingConfirm: true,
      assignee: '杨小磊',
    },
    {
      wo: 'MWO-2026-0107',
      device: 'DEV-ASM-04 减振器装配台（气动压装）',
      issue: '压装力传感器标定',
      stage: '待验证',
      reportedAt: clock(320),
      elapsedMin: clamp(jitter(320, 12), 290, 350),
      etaText: '待质量复核',
      overdue: false,
      awaitingConfirm: false,
      assignee: '张红梅',
    },
  ]
  const repairs = REPAIR_POOL.filter((r) => visibleDeviceNames.has(r.device))

  // —— 可靠性 ——
  // MTBF 推导（设定集 §7）：46 台 × 174 工作日 × 16h 双班 ≈ 128,000 设备运行小时，
  // 29 周共 120 张维修工单 → ≈1,067 h/次。MTTR 取维修工单平均历时 ≈95 min。
  // 当日故障/完修数按 400 报警 / 120 维修摊到 174 个工作日（≈2.3 / ≈0.7 每日）。
  // availability 直接用「在运设备占比」（真实计数推导，不是拍的）。
  const smallSample = devices.length < 6
  const reliability: Reliability = {
    availability: devices.length > 0 ? Math.round((counts.run / devices.length) * 100) : 0,
    mtbfHours: smallSample ? null : clamp(jitter(1040, 80), 900, 1200),
    mttrMinutes: smallSample ? null : clamp(jitter(95, 20), 60, 140),
    failures: smallSample ? 0 : 2,
    repairs: smallSample ? 0 : 1,
  }

  const PM_POOL: PmTask[] = [
    {
      device: 'DEV-AUX-01 螺杆空压机 SA-75',
      task: '空滤芯更换',
      due: '超期 1 天',
      state: 'overdue',
    },
    { device: 'DEV-PKG-01 自动装箱线', task: '输送带张紧检查', due: '超期 2 天', state: 'overdue' },
    { device: 'DEV-CTG-03 固化炉', task: '炉温均匀性校验', due: '今日 16:00', state: 'due' },
    {
      device: 'DEV-GRD-03 数控外圆磨床 MK1332',
      task: '砂轮修整器保养',
      due: '今日 20:00',
      state: 'due',
    },
    {
      device: 'DEV-CNC-07 立式加工中心 VMC-850',
      task: '导轨润滑',
      due: '已完成 11:20',
      state: 'done',
    },
    { device: 'DEV-TST-02 电液伺服试验台', task: '力值标定', due: '已完成 09:40', state: 'done' },
  ]
  const pmTasks = PM_POOL.filter((t) => visibleDeviceNames.has(t.device))

  const INSPECTION_POOL: (Omit<InspectionRow, 'time'> & { minsAgo: number })[] = [
    {
      minsAgo: 28,
      device: 'DEV-CNC-01 数控车床 CK6150',
      item: '主轴温度/振动点检',
      by: '赵婷婷',
      result: '合格',
    },
    {
      minsAgo: 46,
      device: 'DEV-GRD-01 数控外圆磨床 MK1332',
      item: '砂轮转速点检',
      by: '赵婷婷',
      result: '合格',
    },
    {
      minsAgo: 63,
      device: 'DEV-ASM-01 减振器装配台（气动压装）',
      item: '压装力校验',
      by: '赵婷婷',
      result: '合格',
    },
    {
      minsAgo: 88,
      device: 'DEV-CTG-02 电泳槽',
      item: '槽液 PH 点检',
      by: '刘秀英',
      result: '异常',
    },
    {
      minsAgo: 112,
      device: 'DEV-TST-01 电液伺服试验台',
      item: '标定件阻尼力复测',
      by: '赵婷婷',
      result: '合格',
    },
    {
      minsAgo: 137,
      device: 'DEV-AUX-04 螺杆空压机 SA-55',
      item: '气源压力点检',
      by: '陈国庆',
      result: '合格',
    },
    {
      minsAgo: 164,
      device: 'DEV-WLD-02 六轴焊接机器人',
      item: '焊接电流抽检',
      by: '赵婷婷',
      result: '合格',
    },
    {
      minsAgo: 192,
      device: 'DEV-PKG-02 自动装箱线',
      item: '节拍计数核对',
      by: '赵婷婷',
      result: '合格',
    },
  ]
  const inspections: InspectionRow[] = INSPECTION_POOL.filter((i) =>
    visibleDeviceNames.has(i.device),
  ).map((i) => ({
    time: clock(i.minsAgo + jitter(2, 3)),
    device: i.device,
    item: i.item,
    by: i.by,
    result: i.result,
  }))

  return { factoryId, counts, devices, alarms, repairs, reliability, pmTasks, inspections }
}

/** 设备详情（点击按需取）：与全景墙同源画像 + 全参数趋势 + 该设备的保养维修档案。 */
export function buildDeviceDetail(
  deviceId: string,
  factoryId = DEFAULT_FACTORY_ID,
  workshopIds: string[] | 'all' = 'all',
): DeviceDetail | null {
  const ov = buildEquipmentOverview(factoryId, workshopIds)
  const device = ov.devices.find((d) => d.id === deviceId)
  if (!device) return null
  const wcId = DEVICES.find((d) => d.code === device.code)?.workCenterId
  const wc = WORK_CENTERS.find((w) => w.id === wcId)
  const manager = WORKSHOPS.find((w) => w.id === device.workshopId)?.managerName ?? '—'
  // 单机可靠性：有故障样本（报警/停机中）才有值，否则 null 显「—」
  const hasIssue = device.state === 'alarm' || device.state === 'down'
  return {
    device,
    workCenterName: wc?.name ?? '—',
    managerName: manager,
    params: paramSeriesFor(device.code, device.state),
    repairs: ov.repairs.filter((r) => r.device === device.name),
    pmTasks: ov.pmTasks.filter((t) => t.device === device.name),
    inspections: ov.inspections.filter((i) => i.device === device.name),
    mtbfHours: hasIssue ? clamp(jitter(680, 90), 480, 900) : null,
    mttrMinutes: hasIssue ? clamp(jitter(95, 20), 60, 140) : null,
    oee: {
      availability: null,
      performance: null,
      quality: null,
      rate: null,
      isDegraded: true,
      degradedReasons: ['mock-data'],
    },
  }
}

/** 参数快刷 tick（高频轮询专用）：只重算格上参数，不动状态/计数。
 *  deviceIds 传入「当前视野内」的设备集 —— 视野外不产生数据变化（性能约定，
 *  真实端点即按可见集订阅）；缺省为全量。 */
export function buildParamsTick(
  factoryId = DEFAULT_FACTORY_ID,
  workshopIds: string[] | 'all' = 'all',
  deviceIds?: string[],
): DeviceParamsTick {
  const ov = buildEquipmentOverview(factoryId, workshopIds)
  const want = deviceIds ? new Set(deviceIds) : null
  return Object.fromEntries(
    ov.devices.filter((d) => !want || want.has(d.id)).map((d) => [d.id, d.params]),
  )
}
