// 产线监控大屏数据契约（MAN-316，spec §四）。
// 一期诚实定位「产线监控屏（含异常醒目提示）」，非真安灯（闭环待 MAN-322）。
// 🟡 产量/节拍/达成为前端按标准工时反推聚合；🟠 待 #570 真实端点。
import type { DeviceState } from '@/data/contracts/equipment'

/** 产线三态：绿=正常作业 / 黄=需关注（换型/停机待修/待机）/ 红=设备报警 */
export type LineState = 'run' | 'attention' | 'alarm'

/** /line 选择器：每线一张迷你监控卡（红线置顶） */
export interface LineSummaryCard {
  id: string
  name: string
  workshopName: string
  state: LineState
  stateLabel: string
  /** 数据源断线设备数 —— >0 显示失联角标（防假绿） */
  offlineDevices: number
  /** 当班达成率 0–100 🟡 */
  achievement: number
  /** 节拍偏差 %（正=落后，负=超前）🟡 */
  taktDeviationPct: number
  /** 当前工单号（无在制为 undefined） */
  currentWo?: string
  /** 异常一句话（有事才有，红/黄字） */
  alert?: string
}

/** 工序状态机步骤 */
export interface WoStep {
  name: string
  state: 'done' | 'doing' | 'todo'
}

/** 当前工单（✅ 按 workCenter 归并） */
export interface CurrentWo {
  code: string
  product: string
  qtyPlan: number
  qtyDone: number
  /** 在制 WIP ✅ */
  wip: number
  /** 距交付（分钟，工单 DueUtc）✅ */
  dueInMin: number
  steps: WoStep[]
  /** 线边齐套（单工单 🟡） */
  kitting: 'ok' | 'short'
}

/** /line/[id] 单线大屏 */
export interface LineBoard {
  lineId: string
  lineName: string
  workshopName: string
  state: LineState
  stateLabel: string
  offlineDevices: number
  /** 即时停机/报警横幅 —— 有事才有（异常是例外） */
  banner?: { level: 'alarm' | 'downtime'; text: string; since: string }
  /** 班次：当班剩余按真实时钟推算 🟡 */
  shift: { name: string; range: string; remainingMin: number; elapsedMin: number }
  /** 当班产量：按标准节拍反推计划、达成率勾稽 🟡 */
  output: { good: number; scrap: number; rework: number; plan: number; achievement: number }
  /** 节拍：标准 vs 实际（落后红）🟡 */
  takt: { standardSec: number; actualSec: number; deviationPct: number }
  /** 小时产量趋势（近 12 小时）🟡 */
  hourly: number[]
  wo?: CurrentWo
  /** 该线设备带（与设备屏同源画像） */
  devices: { id: string; name: string; state: DeviceState; stateLabel: string }[]
}
