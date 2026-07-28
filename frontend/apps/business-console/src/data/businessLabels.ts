/**
 * 业务码值 → 中文显示名的集中词表。
 *
 * 为什么集中：这些码值散落在多个域的读面里（审批、库存、质量、MES、WMS、编码规则…），
 * 以前每页各自 `labels[x] ?? x` 兜底，词表漏一个就把英文码印到界面上。集中一份后
 * 补词只需改这里，不用满仓库找。
 *
 * 约定：
 * - key 一律小写；查表前用 `normalizeCode` 归一（兼容后端 kebab-case 与 PascalCase 两种写法）。
 * - 查不到时由调用方决定说法，`labelFor` 默认原样返回，避免把「没值」和「词表漏了」混为一谈。
 * - 状态徽标（NvStatusBadge）的通用状态词表在 `@nerv-iip/ui` 内，这里只放它覆盖不到的业务码值。
 */

/** 把 `PartiallyReceived` / `partially-received` / `PARTIALLY_RECEIVED` 归一成 `partially-received`。 */
export function normalizeCode(value?: string | null): string {
  if (!value) return ''
  return value
    .trim()
    .replace(/([a-z\d])([A-Z])/g, '$1-$2')
    .replace(/[\s_]+/g, '-')
    .toLowerCase()
}

/** 按词表取中文名；查不到默认原样返回（调用方可传 fallback 改为占位）。 */
export function labelFor(
  dictionary: Readonly<Record<string, string>>,
  value?: string | null,
  fallback?: string,
): string {
  if (!value) return fallback ?? ''
  return dictionary[normalizeCode(value)] ?? fallback ?? value
}

/** 审批单据类型（审批中心 documentType）。 */
export const DOCUMENT_TYPE_LABELS: Readonly<Record<string, string>> = {
  'purchase-order': '采购订单',
  'purchase-requisition': '采购申请',
  'purchase-receipt': '采购收货',
  'sales-order': '销售订单',
  'sales-quotation': '销售报价',
  'delivery-order': '发货单',
  'work-order': '生产工单',
  'ncr-disposition': '不合格品处置',
  'engineering-change-order': '工程变更',
  'inventory-count': '库存盘点',
  'maintenance-work-order': '维修工单',
  // 以下为后端实际在用、此前漏登记的类型（已回溯 backend 各服务的 documentType 字面量核实）。
  'sales-shipment': '销售出货',
  'finished-goods-receipt': '完工入库',
  'material-issue': '生产领料',
  'operation-task': '工序任务',
  'work-center': '工作中心',
  sop: '作业指导书',
}

/** 审批决定。 */
export const APPROVAL_DECISION_LABELS: Readonly<Record<string, string>> = {
  approve: '同意',
  approved: '已同意',
  reject: '驳回',
  rejected: '已驳回',
  abstain: '弃权',
  delegate: '转办',
  withdraw: '撤回',
  // 后端 Approval 域实际下发的还有 return；前端快捷操作另有 resolve。
  return: '退回',
  returned: '已退回',
  resolve: '处理',
}

/**
 * 库存质量状态。
 *
 * 权威取值只有 4 个（`Inventory.Domain/AggregatesModel/StockQualityStatus.cs`）：
 * unrestricted / quality / restricted / blocked——写入时 Normalize，读面回的必是其一。
 * 别名（qualified/available → unrestricted，inspection/quality-inspection → quality，
 * conditional-release → restricted，rejected → blocked）一并登记，供直接展示别名的场合兜底。
 *
 * `quarantine` 是**质量域**的说法：`InspectionRecord.StockReleaseDimension.SourceQualityStatus`
 * 存的就是它（`Quality/.../Seed/WorldHistorySeedService.cs:32`），表示「已进质量冻结库位、等判定」。
 * 质量域这条路径不走 Inventory 的 Normalize，所以它不在上面那四个规范值里，但确实会上屏。
 * 两个域对同一语义用了不同码值，属跨域词汇不一致，已登记为后端缺口——在它统一之前这里必须收着。
 */
export const QUALITY_STATUS_LABELS: Readonly<Record<string, string>> = {
  unrestricted: '非限制使用',
  qualified: '非限制使用',
  available: '非限制使用',
  quality: '待检',
  inspection: '待检',
  'quality-inspection': '待检',
  restricted: '受限使用',
  'conditional-release': '条件放行',
  blocked: '冻结',
  rejected: '已拒收',
  // 质量域专有（见上方说明）；译「隔离」而非「检验隔离」，避免与 quality=待检 混淆。
  quarantine: '隔离',
}

/** 库存货主类型（ownerType）。 */
export const STOCK_OWNER_TYPE_LABELS: Readonly<Record<string, string>> = {
  company: '本公司',
  customer: '客户寄售',
  supplier: '供应商寄售',
  subcontractor: '外协方',
}

// 不合格品处置方式**不在这里**：受控值与显示函数都在
// `@/composables/useQualityPickerCatalog` 的 QUALITY_DISPOSITION_OPTIONS /
// qualityDispositionLabel，那份还会把词表外的历史自由文本显式标成「未知处置：xxx」。
// 单一事实源，别在本文件另起一份。

/** 追溯节点类型（MES 追溯图 nodeType）。 */
export const TRACE_NODE_TYPE_LABELS: Readonly<Record<string, string>> = {
  'work-order': '生产工单',
  'operation-task': '工序任务',
  'material-lot': '投入批次',
  'produced-lot': '产出批次',
  'received-lot': '收货批次',
  'produced-serial': '产出序列号',
  'consumed-serial': '消耗序列号',
  shipment: '发货',
  inspection: '检验记录',
}

/** 报工候选挂起原因（TelemetryProductionReportCandidate.suspensionReason）。 */
export const REPORT_SUSPENSION_REASON_LABELS: Readonly<Record<string, string>> = {
  'active-alarm': '设备存在未处理报警',
  'no-work-center-mapping': '设备未绑定工作中心',
  'no-current-work-order': '工作中心当前无在制工单',
  'no-operation-task': '工作中心当前无进行中的工序任务',
  'signal-stale': '采集信号已过期',
}

/** 质量数据来源类型（sourceType）。 */
export const QUALITY_SOURCE_TYPE_LABELS: Readonly<Record<string, string>> = {
  'in-process': '过程检验',
  operation: '工序检验',
  'mes-operation': '工序报工',
  incoming: '来料检验',
  final: '成品检验',
  audit: '审核抽检',
}

/** 人员技能熟练度（人员技能矩阵 level）。 */
export const SKILL_LEVEL_LABELS: Readonly<Record<string, string>> = {
  trainee: '实习',
  junior: '初级',
  intermediate: '中级',
  senior: '高级',
  expert: '专家',
  certified: '持证',
}

/** 编码规则版本状态。 */
export const CODE_RULE_VERSION_LABELS: Readonly<Record<string, string>> = {
  draft: '草稿',
  published: '已发布',
  superseded: '已被替代',
  archived: '已归档',
}

/**
 * 徽标色调。与 `@nerv-iip/ui` 的 `StatusTone` 同构，这里重声明是为了让本模块保持
 * 纯数据（不依赖组件库），传给 `NvStatusBadge :tone` 时结构兼容。
 */
export type BusinessStatusTone = 'success' | 'warning' | 'danger' | 'info' | 'neutral'

/**
 * 库存质量状态的语义色调。UI 包通用状态表只认得 `available` / `blocked`，
 * 台账真正回的 `unrestricted` / `quality` / `restricted` 落不到色调上，全成中性灰。
 * 说法本身用 `QUALITY_STATUS_LABELS`（含别名），这里只补色调。
 */
export const STOCK_LEDGER_QUALITY_STATUS_TONES: Readonly<Record<string, BusinessStatusTone>> = {
  unrestricted: 'success',
  qualified: 'success',
  available: 'success',
  quality: 'warning',
  inspection: 'warning',
  'quality-inspection': 'warning',
  restricted: 'warning',
  'conditional-release': 'warning',
  blocked: 'danger',
  rejected: 'danger',
}

/**
 * 库存台账口径的货主类型。Inventory 服务的规范值是
 * `company / customer / supplier / production / maintenance`（`StockOwnerType`）。
 */
export const STOCK_LEDGER_OWNER_TYPE_LABELS: Readonly<Record<string, string>> = {
  ...STOCK_OWNER_TYPE_LABELS,
  production: '生产领用',
  maintenance: '维修备件',
}

/**
 * WMS 单据 / 任务状态。后端一律 `Status.ToString()`，即 PascalCase（`InventoryPostingFailed`），
 * 而 UI 包的通用状态表只按小写整串查，多词状态一律查不到、直接把英文印到界面上。
 * 这里补齐 WMS 侧全部枚举（入库单 / 出库单 / 仓库任务 / 盘点执行 / WCS 任务 / 补发 / 退供 / 移动请求）。
 */
export const WMS_STATUS_LABELS: Readonly<Record<string, string>> = {
  open: '待处理',
  completed: '已完成',
  cancelled: '已取消',
  closed: '已关闭',
  failed: '执行失败',
  pending: '待处理',
  posted: '已过账',
  dispatched: '已下发',
  'pending-quality-check': '待质检放行',
  'inventory-posting-pending': '库存过账中',
  'inventory-posting-failed': '库存过账失败',
}

/** WMS 状态的语义色调（UI 包通用表覆盖不到的多词状态）。 */
export const WMS_STATUS_TONES: Readonly<Record<string, BusinessStatusTone>> = {
  'pending-quality-check': 'warning',
  'inventory-posting-pending': 'warning',
  'inventory-posting-failed': 'danger',
}

/** 取 WMS 状态的语义色调；没有特别声明就交回给通用表推断。 */
export function wmsStatusTone(value?: string | null): BusinessStatusTone | undefined {
  return WMS_STATUS_TONES[normalizeCode(value)]
}

/**
 * WCS 适配器类型（`adapterType`）——仓储自动化设备的接入类型，
 * 后端是自由文本技术标识，这里给出已接入类型的中文说法，未收录时只显技术标识。
 */
export const WCS_ADAPTER_TYPE_LABELS: Readonly<Record<string, string>> = {
  agv: 'AGV 搬运机器人',
  rgv: 'RGV 环形穿梭车',
  'stacker-crane': '堆垛机',
  asrs: '自动化立体库',
  conveyor: '输送线',
  shuttle: '四向穿梭车',
  sorter: '分拣机',
  robot: '机械臂',
  manual: '人工作业',
}

/** 库存移动类型（`movementType`）。 */
export const INVENTORY_MOVEMENT_TYPE_LABELS: Readonly<Record<string, string>> = {
  receipt: '入库',
  issue: '出库',
  transfer: '调拨',
  adjustment: '调整',
}

/**
 * 采集点位的工程单位（`unitCode`）。这是设备侧的物理量单位，与主数据计量单位
 * （件 / 千克，走 `useMasterDataDisplayNames().formatUom`）不是一回事，所以单独一份。
 */
export const TELEMETRY_UNIT_LABELS: Readonly<Record<string, string>> = {
  degc: '摄氏度',
  degf: '华氏度',
  k: '开尔文',
  'mm-s': '毫米每秒',
  rpm: '转每分',
  a: '安培',
  v: '伏特',
  kw: '千瓦',
  kwh: '千瓦时',
  kn: '千牛',
  n: '牛',
  nm: '牛米',
  bar: '巴',
  kpa: '千帕',
  mpa: '兆帕',
  ph: 'pH 值',
  count: '次',
  pct: '百分比',
  '%': '百分比',
  mm: '毫米',
  m: '米',
  s: '秒',
  hz: '赫兹',
  'l-min': '升每分',
  'm3-h': '立方米每小时',
}

/**
 * 工程单位归一：`degC` / `mm/s` / `m3·h` 这类写法不能走 `normalizeCode`
 * （它会把 `degC` 拆成 `deg-c`），这里只做小写 + 分隔符统一。
 */
function normalizeUnitCode(code?: string | null): string {
  if (!code) return ''
  return code
    .trim()
    .toLowerCase()
    .replace(/[/\\·*\s_]+/g, '-')
}

/** 工程单位展示串：「摄氏度 (degC)」；名录里没有就只显技术单位。 */
export function formatTelemetryUnit(code?: string | null, fallback = '无'): string {
  if (!code) return fallback
  const name = TELEMETRY_UNIT_LABELS[normalizeUnitCode(code)]
  return name && name !== code ? `${name} (${code})` : code
}

const DURATION_UNIT_LABELS: Readonly<Record<string, string>> = {
  ms: '毫秒',
  s: '秒',
  m: '分钟',
  h: '小时',
  d: '天',
}

function formatDurationToken(token?: string): string | undefined {
  if (!token) return undefined
  const match = /^(\d+)(ms|s|m|h|d)$/.exec(token.trim())
  if (!match) return undefined
  return `${match[1]} ${DURATION_UNIT_LABELS[match[2]!]}`
}

/**
 * 采样策略（`samplingPolicy`）说人话。
 *
 * 后端是结构化配置串而不是枚举：`sample-2s`，或
 * `bucket=30s;raw=7d;hourly=90d;daily=730d`（采样桶 + 三级保留期）。
 * 直接摆到界面上没人看得懂，这里翻成「每 2 秒采样」「每 30 秒采样 · 原始保留 7 天…」。
 */
export function formatSamplingPolicy(value?: string | null, fallback = '未标注'): string {
  const raw = (value ?? '').trim().toLowerCase()
  if (!raw) return fallback

  const RETENTION_PREFIXES: ReadonlyArray<readonly [string, string]> = [
    ['raw=', '原始保留'],
    ['hourly=', '小时汇总保留'],
    ['daily=', '日汇总保留'],
  ]

  let bucket: string | undefined
  const retentions: string[] = []
  const parts = raw
    .split(';')
    .map((p) => p.trim())
    .filter(Boolean)
  for (const part of parts) {
    if (part.startsWith('bucket=')) {
      bucket = part.slice('bucket='.length)
      continue
    }
    const retention = RETENTION_PREFIXES.find(([prefix]) => part.startsWith(prefix))
    if (retention) {
      const token = part.slice(retention[0].length)
      retentions.push(`${retention[1]} ${formatDurationToken(token) ?? token}`)
      continue
    }
    if (!bucket) bucket = part
  }

  const bucketToken = bucket?.startsWith('sample-') ? bucket.slice('sample-'.length) : bucket
  const bucketText = formatDurationToken(bucketToken)
  // 解析不出采样桶时只能如实回显配置串——它是时长配置而非英文码值，不硬造中文。
  const head = bucketText ? `每 ${bucketText}采样` : raw
  return retentions.length ? `${head} · ${retentions.join(' · ')}` : head
}

/**
 * ISO 8601 周期（点检 / 保养计划的 `intervalIso`，如 `P7D` / `P1M`）说人话。
 * 解析不出就回退到 `fallback`，不把 `P7D` 印到界面上。
 */
export function formatIsoInterval(value?: string | null, fallback = '未设置'): string {
  const raw = (value ?? '').trim().toUpperCase()
  if (!raw) return fallback
  const match = /^P(?:(\d+)Y)?(?:(\d+)M)?(?:(\d+)W)?(?:(\d+)D)?$/.exec(raw)
  if (!match || !match.slice(1).some(Boolean)) return fallback
  const [, years, months, weeks, days] = match
  const parts: string[] = []
  if (years) parts.push(`${years} 年`)
  if (months) parts.push(`${months} 个月`)
  if (weeks) parts.push(`${weeks} 周`)
  if (days) parts.push(`${days} 天`)
  return `每 ${parts.join(' ')}`
}

/**
 * MES 生产准备检查区域码（foundation 的 areaCode、生产驾驶舱阻塞项的 areaCode 同源）。
 * 两页共用一份，避免驾驶舱印裸码而检查页印中文。
 */
export const MES_READINESS_AREA_LABELS: Readonly<Record<string, string>> = {
  masterdata: '主数据',
  'master-data': '主数据',
  engineering: '工程',
  inventory: '库存',
  material: '物料',
  quality: '质量',
  capacity: '产能',
  equipment: '设备',
  routing: '工艺路线',
  bom: '物料清单',
}

/**
 * MES 质量项处理状态（「质量与不良」列表 status）。
 * 与通用状态徽标词表分开：同一个 `open` 在停机语境是「未恢复」，在质量项语境是「待处理」。
 */
export const MES_QUALITY_ITEM_STATUS_LABELS: Readonly<Record<string, string>> = {
  open: '待处理',
  'rework-pending': '待返工',
  'scrap-accepted': '报废已受理',
  'return-accepted': '退回已受理',
  'disposition-accepted': '处置已受理',
  closed: '已关闭',
}

/** MES 班次交接状态（open 在交接语境是「待接班」而非通用的「待处理」）。 */
export const MES_HANDOVER_STATUS_LABELS: Readonly<Record<string, string>> = {
  open: '待接班',
  accepted: '已接班',
}

/** 不合格品报告（NCR）状态，与后端 NonconformanceReport 的三个状态字一一对应。 */
export const NCR_STATUS_LABELS: Readonly<Record<string, string>> = {
  open: '待处置',
  'disposition-in-progress': '处置中',
  closed: '已关闭',
  // 纠正措施（CAPA）走同一列展示时会出现的状态字
  'effectiveness-verified': '有效性已验证',
}

/** 规则排程给每条工序分配写的原因（后端 RuleScheduler 只产出这两种）。 */
export const RULE_SCHEDULE_REASON_LABELS: Readonly<Record<string, string>> = {
  'in-progress-preserved': '在制工序保留原时段',
  'rule-sequenced': '按优先级与交期排序',
}

/** 遥测报工候选状态（无挂起原因时展示，来自 TelemetryProductionReportCandidate.status）。 */
export const TELEMETRY_CANDIDATE_STATUS_LABELS: Readonly<Record<string, string>> = {
  'pending-confirmation': '待确认',
  draft: '草稿',
  promoted: '已转正',
  dismissed: '已忽略',
  suspended: '已挂起',
}
