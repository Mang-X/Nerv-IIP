// 产线监控大屏数据契约（MAN-316，spec §四）。
// 一期诚实定位「产线监控屏（含异常醒目提示）」，非真安灯（闭环待 MAN-322）。
// 🟡 产量/节拍/达成为前端按标准工时反推聚合；🟠 待 #570 真实端点。
import type { DeviceParamSeries, DeviceState } from '@/data/contracts/equipment'

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
  /** 当班产量（良品/计划）🟡 */
  output: { good: number; plan: number }
  /** 该线设备状态点排（与设备屏同源） */
  deviceDots: DeviceState[]
  /** 小时产量迷你趋势（近 12h）🟡 */
  hourly: number[]
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

/** 安灯呼叫记录（一期演示形态；呼叫-响应闭环 待 MAN-322） */
export interface AndonCall {
  time: string
  station: string
  type: string
  response: string
  state: '响应中' | '已关闭'
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
  /** 当班班组（线长 + 在岗人数）🟡 */
  crew: { leader: string; operators: number }
  /** 当班产量：按标准节拍反推计划、达成率勾稽 🟡 */
  output: { good: number; scrap: number; rework: number; plan: number; achievement: number }
  /** 一次合格率 FPY 0–100（良品/完工）🟡 */
  fpy: number
  /** 当班停机统计 🟡 */
  downtime: { count: number; totalMin: number }
  /** 节拍：标准 vs 实际（落后红）🟡 */
  takt: { standardSec: number; actualSec: number; deviationPct: number }
  /** 产线 OEE（班内推算：可用率=停机推 / 性能率=节拍推 / 良品率=FPY）🟡 待 #570 校准 */
  oee: { overall: number; availability: number; performance: number; quality: number }
  /** 近 24h 每小时 OEE 0–100（热力图 4×6）；索引 0 = 24h 前 🟡 */
  hourlyOee: number[]
  /** 小时产量趋势（近 12 小时）+ 每点时刻标签 + 节拍产能参考 🟡 */
  hourly: number[]
  hourLabels: string[]
  planPerHour: number
  /** 近 30 天日产量趋势（含周末排产低谷）🟡 */
  daily30: { output: number[]; plan: number[]; labels: string[] }
  wo?: CurrentWo
  /** 安灯呼叫记录（正常线为空 —— 异常是例外）；闭环 待 MAN-322 */
  andon: AndonCall[]
  /** 该线设备带（与设备屏同源画像；param 为首参摘要，params 为折叠详情 4 项带趋势） */
  devices: {
    id: string
    name: string
    state: DeviceState
    stateLabel: string
    param?: string
    params: DeviceParamSeries[]
  }[]
}
