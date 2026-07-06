// 车间总览大屏数据契约（MAN-315，spec §三）。
// 车间主任「当班作战室」：单车间下钻 —— 产出够不够 / 线在不在转 / 料齐不齐 /
// 质量稳不稳 / 设备有没有趴窝 / 班组有没有掉链子 / 哪里在求救，一屏判断。
// 产线区直接同源复用产线屏 LineSummaryCard（buildLineCards 过滤本车间，数字精确一致）。
// 🟡 产量/达成/齐套/停机为前端按线聚合推算；🟠 车间维度无真实聚合端点，待 #570。
// ⚠️ 人员维度铁律：平台**有**班组花名册/技能矩阵/班次交接，**没有**考勤在岗/报工到人/
//   人效 —— 本契约只表达计划口径，不为缺口造假数。
import type { LineState, LineSummaryCard } from '@/data/contracts/line'

/** 当班产出（Σ 本车间各线；勾稽：actual = Σ lines.output.good）🟡 */
export interface WorkshopOutput {
  actual: number
  plan: number
  /** 达成率 0–100 = round(actual / plan × 100) */
  achievement: number
}

/** 当班累计曲线（班次开始 → 现在逐点累计；末点与 output 精确一致）🟡 */
export interface ShiftCurve {
  actual: number[]
  plan: number[]
  /** 每点时刻标签（整点 HH:00，末点为当前 HH:mm） */
  labels: string[]
}

/** 产线状态计数（状态墙汇总） */
export interface LineStateCounts {
  run: number
  attention: number
  alarm: number
}

/** 设备状态计数（与设备屏同源画像归并） */
export interface DeviceCounts {
  total: number
  run: number
  idle: number
  down: number
  alarm: number
  offline: number
}

/** 当班停机（非计划停机 + 换型；计划保养不计入异常）🟡 */
export interface DowntimeInfo {
  count: number
  totalMin: number
}

/** 停机/报警事件（与设备屏画像同源；warn=未恢复预警，info=换型/失联） */
export interface WorkshopEvent {
  id: string
  time: string
  level: 'alarm' | 'downtime' | 'warn' | 'info'
  lineName: string
  text: string
  status: string
}

/** 缺料行（线边齐套缺口 Top；wo/需求量与产线屏当前工单同源）🟡 */
export interface ShortageItem {
  material: string
  code: string
  lineName: string
  wo: string
  requiredQty: number
  shortQty: number
  eta: string
}

/** 物料齐套（线上在产工单口径，单工单简化 🟡） */
export interface KittingInfo {
  /** 齐套率 0–100 = 齐套线 / 在产线 */
  rate: number
  /** 在产工单（每线当前一单，与产线屏一致） */
  woActive: number
  /** 阻塞工单 = 缺料线 + 停摆（报警）线 */
  woBlocked: number
  shortages: ShortageItem[]
}

/** NCR 待办行 ✅ */
export interface NcrItem {
  code: string
  lineName: string
  text: string
  status: string
}

/** 当班质量（报废/返修沿产线屏同族口径推算；FPY = 良品/完工）🟡 */
export interface QualityInfo {
  scrap: number
  rework: number
  fpy: number
  ncr: NcrItem[]
}

/** 工单交付预警（临期/超期；大多数车间为空 —— 异常是例外）🟡 */
export interface WoAlert {
  code: string
  product: string
  lineName: string
  kind: 'overdue' | 'dueSoon'
  dueText: string
}

/** 当班班组（✅ 花名册/班次/交接为平台真实能力；⚠️ 在岗/人效为数据缺口，不展示） */
export interface CrewInfo {
  teamName: string
  leader: string
  /** 计划应到（花名册口径，非考勤实到） */
  headcountPlanned: number
  /** 关键工位技能覆盖率 0–100（技能矩阵）🟡 */
  skillCoverage: number
  /** 上一班交接遗留问题数 ✅ */
  handoverIssues: number
  /** 遗留问题摘要（有才有） */
  handoverNote?: string
}

/** /workshop/[id] 车间总览大屏 */
export interface WorkshopBoard {
  workshopId: string
  workshopName: string
  managerName: string
  /** 车间态 = 线状态归并（任一线红 → 红；任一线黄 → 黄；否则绿） */
  state: LineState
  stateLabel: string
  /** 数据源断线设备数（Σ 各线失联，防假绿） */
  offlineDevices: number
  /** 班次：当班剩余按真实时钟推算 🟡 */
  shift: { name: string; range: string; remainingMin: number; elapsedMin: number }
  output: WorkshopOutput
  /** 本车间产线卡（与产线屏 buildLineCards 同源，红线置顶） */
  lines: LineSummaryCard[]
  lineStates: LineStateCounts
  shiftCurve: ShiftCurve
  devices: DeviceCounts
  downtime: DowntimeInfo
  /** 当前停机/报警事件流（正常车间为空 —— 空态 = 健康） */
  events: WorkshopEvent[]
  kitting: KittingInfo
  quality: QualityInfo
  woAlerts: WoAlert[]
  crew: CrewInfo
}
