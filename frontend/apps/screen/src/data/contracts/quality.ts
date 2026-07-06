// 质量看板大屏数据契约（MAN-319，spec §六）。
// 数据现实（🟠 多数聚合待 #570）：Quality 域零聚合 API（仅 6 个明细 GET）；
// 检验/NCR 无产线维度（经 source_document→工单→工作中心前端映射）；CAPA/MRB 零查询端点；
// ⚠️ 缺陷码 Quality(reason_code) 与 MES(defect_code) 口径不统一 —— mock 用统一语义名，
// 真实接入时需在 fetchers/quality.ts 做映射表归一。

/** NCR 处置 SLA（小时）：超过即超期红标（演示阈值；SLA 管理待 #570） */
export const NCR_SLA_HOURS = 48
/** 不良率红线阈值 %（演示值；阈值管理待 #570） */
export const DEFECT_RED_LINE_PCT = 1.5

/** NCR 处置状态机：待评审 → 处置中（返工/让步接收/报废/退供）→ 待验证（验证过 = 关闭出板） */
export type NcrStatus = 'review' | 'disposing' | 'verify'

export type NcrDisposition = '返工' | '让步接收' | '报废' | '退供'

/** NCR 待处置行（看板内均为未关闭单；NCR 无独立关闭时间，关闭即离板） */
export interface NcrRow {
  /** NCR 编号 ✅ */
  code: string
  /** 来源类型：产线（过程/成品）或供应商（来料） */
  sourceType: 'line' | 'supplier'
  /** 产线名或供应商名 🟡 经 source_document→工单→工作中心映射（NCR 无产线维度） */
  source: string
  /** 来源产线 id（供应商行无）——与 masterdata/产线屏同源 */
  lineId?: string
  /** 关联单据 ✅ source_document：产线行为工单 WO-xxxx，来料行为检验单 IQC-xxxx */
  sourceDoc: string
  /** 工单产品（产线行；与产线屏 LINE_PROFILES 同源）🟡 */
  product?: string
  /** 缺陷语义名 🟠 reason_code ↔ MES defect_code 口径不统一，待 #570 映射 */
  defect: string
  /** 不合格数量 ✅ */
  qty: number
  /** 龄期（小时，created_at → now 推算）🟡 */
  ageHours: number
  /** 超期 = 龄期 > SLA（48h）🟡 */
  overdue: boolean
  status: NcrStatus
  /** 待评审 / 处置中 / 待验证 */
  statusLabel: string
  /** 处置方式（处置中才有）✅ */
  disposition?: NcrDisposition
}

/** 缺陷帕累托项（近 7 天窗口）🟠 缺陷聚合无端点，且缺陷码口径不统一（文件头注） */
export interface ParetoItem {
  defect: string
  /** 主要来源产线（与 masterdata 同源） */
  lineName: string
  /** 当期数量（件） */
  count: number
  /** 占当期缺陷总数 %（Σ ≤ 100，降序） */
  pct: number
}

/** 检验分层（来料 IQC / 过程 IPQC / 成品 FQC）——三层合格率与积压同源自这一组对象 */
export interface InspectionLayer {
  key: 'iqc' | 'ipqc' | 'fqc'
  /** 来料检 / 过程检 / 成品检 */
  label: string
  /** IQC / IPQC / FQC */
  code: string
  /** 今日判定批次 🟡 检验单按 source 前端聚合 */
  lotsDone: number
  /** 今日合格批次 🟡 */
  lotsPassed: number
  /** 今日应检批次 🟡 */
  lotsDue: number
  /** 昨日结转待检 🟡 */
  carryOver: number
  /** 积压 = 应检 − 已判 + 结转 🟡 */
  backlog: number
  /** 最老待检龄期（小时）🟡 */
  oldestHours: number
  /** 积压最多的来源（过程检里电芯线偏多 —— 与产线屏同一故事）🟡 */
  backlogTop?: { name: string; count: number }
  /** 今日未过批次的最大来源（异常是例外：正常线不上榜）🟡 */
  failedTop?: { name: string; count: number }
  /** 当日检验件数 🟠 件级无聚合端点 */
  pieceInspected: number
  /** 当日不良件数 🟠 */
  pieceDefects: number
  /** 批次合格率 % = lotsPassed / lotsDone 🟡 */
  passRate: number
  /** 件不良率 % = pieceDefects / pieceInspected 🟠 */
  pieceDefectPct: number
}

export interface QualityKpis {
  /** 当日批次合格率 %（快照）= Σ合格批 / Σ判定批 🟡；趋势 🟠 */
  batchPassRate: number
  batchPassed: number
  batchTotal: number
  /** 整体不良率 %（件口径）= Σ不良件 / Σ检验件 —— 与批口径互补自洽 🟠 */
  defectRatePct: number
  /** 不良率红线阈值 %（演示 1.5；阈值管理待 #570） */
  redLinePct: number
  /** 待处置 NCR = ncrs.length ✅ */
  openNcr: number
  /** 超期 NCR = 龄期 > SLA 行数 🟡 */
  overdueNcr: number
  /** 检验积压 = Σ 三层 backlog 🟡 */
  inspectionBacklog: number
  /** 积压最老龄期（小时）🟡 */
  backlogOldestHours: number
  /** 条件放行在途（让步接收处置中 + 检验中放行单）🟡 */
  conditionalRelease: number
  /** MRB 待评审 = 待评审 NCR 数 🟠 MRB/CAPA 零查询端点，mock 以待评审 NCR 代口径 */
  mrbPending: number
}

/** 近 30 天不良率趋势 🟠（labels M/D；lots 为当日判定批次，周日检验量低谷） */
export interface DefectTrend30 {
  ratePct: number[]
  lots: number[]
  labels: string[]
}

/** 今日近 12h 不良率趋势（整点滚动）🟡 */
export interface DefectTrend12h {
  ratePct: number[]
  labels: string[]
}

export interface QualityBoard {
  factoryId: string
  kpis: QualityKpis
  /** 待处置看板（龄期降序，超期自然置顶） */
  ncrs: NcrRow[]
  /** TOP5 降序；pct 分母为 paretoTotal */
  pareto: ParetoItem[]
  /** 当期（近 7 天）缺陷总件数（含长尾） */
  paretoTotal: number
  /** 来料 / 过程 / 成品 三层（合格率与积压同源） */
  layers: InspectionLayer[]
  trend30: DefectTrend30
  trend12h: DefectTrend12h
}
