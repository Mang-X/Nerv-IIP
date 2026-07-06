// 设备监控大屏数据契约（MAN-317，spec §三）。
// 🟠 状态分类计数/未恢复时长/PM 到期为前端聚合（无真实聚合端点，见 #570）；
// 接入后仅换 fetchers/equipment.ts。⚠️ 真实设备状态端点 deviceAssetIds ≤ 50/批。

/** 一期与连接器约定的标准状态词表（后端 state 为自由小写字符串） */
export type DeviceState = 'run' | 'idle' | 'down' | 'alarm' | 'offline'

export interface DeviceCell {
  id: string
  code: string
  name: string
  lineName: string
  state: DeviceState
  stateLabel: string
  /** 活动阻塞原因 ✅（overview activeBlocks），无则不显示 */
  block?: string
  /** IsSourceFresh ✅ —— false 即断线，防「假绿」 */
  sourceFresh: boolean
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
}

export interface RepairOrder {
  wo: string
  device: string
  issue: string
  /** 0–100 */
  progress: number
  stage: string
  /** 未关闭/超时 🟡 */
  overdue: boolean
  /** 报警已恢复待确认 ✅ */
  awaitingConfirm: boolean
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
