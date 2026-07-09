// 设备监控大屏数据契约（MAN-317，spec §三）。
// 🟠 状态分类计数/未恢复时长/PM 到期为前端聚合（无真实聚合端点，见 #570）；
// 接入后仅换 fetchers/equipment.ts。⚠️ 真实设备状态端点 deviceAssetIds ≤ 50/批。

/** 一期与连接器约定的标准状态词表（后端 state 为自由小写字符串） */
export type DeviceState = 'run' | 'idle' | 'down' | 'alarm' | 'offline'

/** 参数类型 —— 驱动图表配色与图标（温度/压力/转速/电流/振动/流量/液位/节拍/能耗/扭矩） */
export type ParamKind =
  | 'temp'
  | 'pressure'
  | 'speed'
  | 'current'
  | 'vibration'
  | 'flow'
  | 'level'
  | 'cycle'
  | 'energy'
  | 'torque'

/** 格上简版关键参数（🟠 演示数据流，historian/实时采集接入待 #570） */
export interface DeviceParamBrief {
  label: string
  /** 已带单位的展示值；断线设备为「—」 */
  value: string
  kind: ParamKind
  tone?: 'warn' | 'bad'
}

export interface DeviceCell {
  id: string
  code: string
  name: string
  lineId: string
  lineName: string
  workshopId: string
  workshopName: string
  state: DeviceState
  stateLabel: string
  /** 活动阻塞原因 ✅（overview activeBlocks），无则不显示 */
  block?: string
  /** IsSourceFresh ✅ —— false 即断线，防「假绿」 */
  sourceFresh: boolean
  /** 格上显示的 2 个关键参数 */
  params: DeviceParamBrief[]
}

/** 五态互斥，和恒等于设备总数 */
export interface StateCounts {
  run: number
  idle: number
  down: number
  alarm: number
  offline: number
}

/** 未恢复报警行：级别 · 未恢复时长 🟡（并入 status 文案）· 已触发工单 ✅（闭环） */
export interface OpenAlarmRow {
  time: string
  line: string
  level: 'sev' | 'gen'
  name: string
  wo: string
  status: string
  /** #686 响应状态 —— 指挥中心最关心"有没有人响应"：已确认为 true（未确认高亮）。 */
  acked?: boolean
  /** 确认人（acked 时展示）。 */
  ackBy?: string
  /** #686 升级 —— 未及时确认被升级，列表醒目标识。 */
  escalated?: boolean
}

/** 维修状态机阶段（现实衡量：状态流转 + 时间，非拍脑袋的百分比进度） */
export const REPAIR_STAGES = ['已派工', '维修中', '待验证', '已关闭'] as const
export type RepairStage = (typeof REPAIR_STAGES)[number]

export interface RepairOrder {
  wo: string
  device: string
  issue: string
  stage: RepairStage
  /** 报修时刻 HH:mm */
  reportedAt: string
  /** 已历时（报修至今，分钟）🟡 */
  elapsedMin: number
  /** 预计完成 / SLA 文本，如「预计 17:30」「备件到货后 2h」 */
  etaText: string
  /** 阻塞原因（如 待备件 · 送风机轴承），维修中被卡时给出 */
  blockedBy?: string
  /** 超 SLA 🟡 */
  overdue: boolean
  /** 报警已恢复待确认 ✅ */
  awaitingConfirm: boolean
  /** 维修责任人 */
  assignee: string
}

export interface Reliability {
  /** 时间稼动率 0–100 —— 渲染必须标注「≈可用率 · 非完整 OEE」 */
  availability: number
  /** ✅ 无样本 null，页面显「—」 */
  mtbfHours: number | null
  mttrMinutes: number | null
  failures: number
  repairs: number
}

export interface PmTask {
  device: string
  task: string
  due: string
  state: 'due' | 'overdue' | 'done'
}

export interface InspectionRow {
  time: string
  device: string
  item: string
  by: string
  result: '合格' | '异常'
}

export interface EquipmentOverview {
  factoryId: string
  counts: StateCounts
  devices: DeviceCell[]
  alarms: OpenAlarmRow[]
  repairs: RepairOrder[]
  reliability: Reliability
  pmTasks: PmTask[]
  inspections: InspectionRow[]
}

// —— 设备详情（点击设备格按需取数，形状对齐未来单设备端点）——

/** 详情页参数：当前值 + 近 12 点趋势；断线设备 spark 为空（图示虚线占位） */
export interface DeviceParamSeries {
  label: string
  /** 数值；断线为 null，页面显「—」 */
  value: number | null
  unit: string
  kind: ParamKind
  /** 正常范围文字，如「≤ 75℃」「120–140Nm」 */
  range: string
  spark: number[]
  tone?: 'warn' | 'bad'
}

/** 参数快刷 tick：deviceId → 格上简版参数（高频轮询，只刷参数不刷状态） */
export type DeviceParamsTick = Record<string, DeviceParamBrief[]>

export interface DeviceDetail {
  device: DeviceCell
  workCenterName: string
  managerName: string
  /** 4 个关键参数带趋势 🟠 演示数据流，historian 待 #570 */
  params: DeviceParamSeries[]
  /** 该设备的维修单 / PM / 点检（无则空数组，页面显「暂无」） */
  repairs: RepairOrder[]
  pmTasks: PmTask[]
  inspections: InspectionRow[]
  /** 单机可靠性：无故障样本 → null 显「—」 */
  mtbfHours: number | null
  mttrMinutes: number | null
}
