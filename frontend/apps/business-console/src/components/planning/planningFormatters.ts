import type {
  BusinessConsoleMrpPeggingItem,
  BusinessConsoleMrpRunItem,
  BusinessConsolePlanningSuggestionItem,
} from '@nerv-iip/api-client'
import type { StatusTone } from '@nerv-iip/ui'
import {
  isMesWorkOrderDownstream,
  normalizeReferenceToken,
} from '@/composables/planning/fulfillment'

export function suggestionsPerDemand(run: BusinessConsoleMrpRunItem): string {
  const demandCount = run.demandCount ?? 0
  if (demandCount <= 0) return '—'
  const suggestionCount = run.suggestionCount ?? 0
  const average = Intl.NumberFormat('zh-CN', { maximumFractionDigits: 2 }).format(
    suggestionCount / demandCount,
  )
  return `${average} 条/需求（${suggestionCount} ÷ ${demandCount}）`
}

// 运行的人读锚点：以「计划范围」作追溯标题，避免裸 GUID。
export function runHorizonLabel(run?: BusinessConsoleMrpRunItem | null): string {
  if (!run) return '选择一次运行'
  return `计划范围 ${formatDate(run.horizonStart)} ~ ${formatDate(run.horizonEnd)}`
}

// pegging 分型（成品净需求 / 组件展开）。
export function peggingTypeLabel(value?: string | null): { label: string; tone: StatusTone } {
  const map: Record<string, { label: string; tone: StatusTone }> = {
    'finished-good': { label: '成品净需求', tone: 'info' },
    'finished-good-net-requirement': { label: '成品净需求', tone: 'info' },
    component: { label: '组件展开', tone: 'neutral' },
    'component-net-requirement': { label: '组件展开', tone: 'neutral' },
  }
  return map[value ?? ''] ?? { label: '需求展开', tone: 'neutral' }
}

// 父项=组件 或 无组件 → 视为顶层（成品行，不缩进）；有不同组件 → 缩进。
export function isComponentRow(row: BusinessConsoleMrpPeggingItem): boolean {
  return !!row.componentSkuCode && row.componentSkuCode !== row.parentSkuCode
}

export function planningStatus(status?: string | null): { label: string; tone: StatusTone } {
  const s = (status ?? '').toLowerCase()
  if (s === 'accepted' || s === 'completed')
    return { label: s === 'accepted' ? '已接受' : '已完成', tone: 'success' }
  if (s === 'running' || s === 'inprogress') return { label: '运行中', tone: 'info' }
  // Created = 异步受理后的排队态（#1306）。
  if (s === 'created' || s === 'queued') return { label: '排队中', tone: 'info' }
  if (s === 'failed') return { label: '失败', tone: 'danger' }
  if (s === 'rejected') return { label: '已拒绝', tone: 'neutral' }
  if (s === 'superseded') return { label: '已被替代', tone: 'neutral' }
  if (s === 'open' || s === 'pending') return { label: '待评审', tone: 'warning' }
  return { label: status || '未知', tone: 'neutral' }
}

export function mpsStatus(status?: string | null): { label: string; tone: StatusTone } {
  const s = (status ?? '').toLowerCase()
  if (s === 'released') return { label: '已发布', tone: 'success' }
  if (s === 'reviewed') return { label: '已评审', tone: 'info' }
  if (s === 'draft') return { label: '草稿', tone: 'neutral' }
  return { label: status || '未知', tone: 'neutral' }
}

export function isMpsReleased(status?: string | null) {
  return status?.toLowerCase() === 'released'
}

export function demandTypeLabel(value?: string | null) {
  return (
    (
      { forecast: '预测', 'safety-stock': '安全库存', 'sales-order': '销售订单' } as Record<
        string,
        string
      >
    )[value ?? ''] ??
    (value || '未指定')
  )
}

export function suggestionTypeLabel(value?: string | null) {
  return (
    (
      {
        'planned-purchase': '采购建议',
        'planned-work-order': '生产建议',
        'reschedule-in': '提前调整',
        'reschedule-out': '延期调整',
        'release-date-past': '释放日已过',
        'negative-availability': '负可用',
        'overdue-receipt': '超期在途',
        cancel: '取消收货',
        'mps-sales-excess': '销售超出主计划',
      } as Record<string, string>
    )[value ?? ''] ??
    (value || '未指定')
  )
}

export function suggestionTypeTone(value?: string | null): StatusTone {
  if (value === 'planned-work-order') return 'info'
  if (value === 'planned-purchase') return 'neutral'
  if (
    value === 'reschedule-in' ||
    value === 'reschedule-out' ||
    value === 'release-date-past' ||
    value === 'overdue-receipt'
  )
    return 'warning'
  if (value === 'negative-availability') return 'danger'
  if (value === 'mps-sales-excess') return 'danger'
  if (value === 'cancel') return 'danger'
  return 'neutral'
}

export function isAcceptableSuggestion(value?: string | null) {
  return (
    value === 'planned-work-order' ||
    value === 'planned-purchase' ||
    value === 'reschedule-in' ||
    value === 'reschedule-out' ||
    value === 'cancel'
  )
}

export function reasonLabel(value?: string | null) {
  const map: Record<string, string> = {
    inventory_shortage: '库存不足',
    material_shortage: '物料不足',
    demand_pegging: '需求驱动',
    safety_stock: '安全库存',
    'net-requirement': '需求存在净缺口',
    'finished-good-net-requirement': '成品净需求',
    'component-net-requirement': '组件净需求',
    'safety-stock-replenishment': '安全库存低于下限，建议补货',
    'lead-time-insufficient': '提前期不足，释放日已过',
    'negative-availability': '可用量为负',
    'scheduled-receipt-overdue': '在途已超期',
    'sales-above-mps': '销售需求超出已发布主计划',
  }
  // 未知码一律降级为通用中文，绝不回显原始英文码。
  return map[value ?? ''] ?? '按计划规则形成'
}

export function sourceTypeLabel(value?: string | null) {
  const map: Record<string, string> = {
    sales: '销售来源',
    'sales-order': '销售来源',
    forecast: '预测来源',
    safety: '安全库存来源',
    'safety-stock': '安全库存来源',
    mps: 'MPS 来源',
    component: '组件展开',
    demand: '需求来源',
    'scheduled-receipt': '在途来源',
    'negative-availability': '负可用来源',
  }
  return map[(value ?? '').toLowerCase()] ?? '来源未分类'
}

export function isOpen(status?: string | null) {
  return status?.toLowerCase() === 'open'
}

export function downstreamLabel(service?: string | null, type?: string | null) {
  // 归一化后再比：写侧有两个生产者（种子写 `business-mes`、前端接受建议时写 `BusinessMes`），
  // 严格比字面量会让一半数据退化成泛称「下游单据」。
  if (isMesWorkOrderDownstream(service, type)) return 'MES 工单'
  if (
    normalizeReferenceToken(service) === 'businesserp' &&
    normalizeReferenceToken(type) === 'purchaserequisition'
  )
    return 'ERP 采购申请'
  if (
    normalizeReferenceToken(service) === 'businesserp' &&
    normalizeReferenceToken(type) === 'purchaseorderline'
  )
    return 'ERP 采购单行'
  return '下游单据'
}

export function downstreamStatus(status?: string): { label: string; tone: StatusTone } {
  const names: Record<string, string> = {
    created: '新建',
    queued: '排队中',
    ready: '可开工',
    released: '已下达',
    started: '已开工',
    inprogress: '执行中',
    running: '执行中',
    hold: '暂停',
    blocked: '阻塞',
    completed: '已完成',
    closed: '已关闭',
    cancelled: '已取消',
    open: '待处理',
    converted: '已转采购单',
    loading: '正在核对作业范围',
    restricted: '无权查看下游状态',
    partial: '前 400 条候选未定位到回执',
    unavailable: '状态读取失败',
    unknown: '状态未知',
  }
  const key = (status ?? 'unknown').toLowerCase()
  return {
    label: names[key] ?? status ?? '状态未知',
    tone:
      key === 'cancelled' || key === 'blocked' || key === 'unavailable'
        ? 'danger'
        : key === 'completed' || key === 'closed' || key === 'converted'
          ? 'success'
          : 'neutral',
  }
}

export function formatDate(value?: string | null) {
  return value ? value.slice(0, 10) : '-'
}

export function formatQuantity(value?: number | null, uom?: string | null) {
  return `${value ?? 0} ${uom ?? ''}`.trim()
}

/** 比率说人话：0.02 → 「2%」、1 → 「100%」；缺值显 —。计划员看的是百分比，不是小数码。 */
export function formatPercent(value?: number | null) {
  if (value === null || value === undefined) return '—'
  return `${Math.round(value * 10000) / 100}%`
}

/**
 * 后端把公式串成 `130 - 0 - 0 = 130; scrap/yield 0.02/1`（MrpCalculator.BuildFormula）。
 * `scrap/yield` 是英文码直出（#1418 顺带项），且信息与下方「废品率/良率」重复——
 * 展示时只取分号前的算式部分，比率交给下方中文行。存量建议无需重算即可生效。
 */
export function inputDegradationLabel(sources?: readonly string[] | null) {
  return sources && sources.length > 0
    ? sources.map(inputDegradationSourceLabel).join('、')
    : '正常'
}

function inputDegradationSourceLabel(source: string) {
  return (
    (
      {
        'scheduled-receipts': '在途到货',
        'master-data-planning-parameters': '主数据规划参数',
      } as Record<string, string>
    )[source] ?? source
  )
}

export function inputSourcesLabel(sources?: readonly string[] | null) {
  return sources && sources.length > 0 ? sources.map(inputSourceLabel).join('、') : '未记录'
}

function inputSourceLabel(source: string) {
  return (
    (
      {
        mps: 'MPS 主计划',
        'demand-source': '需求池',
        'sales-order': '销售需求',
        forecast: '预测',
        'safety-stock': '安全库存',
      } as Record<string, string>
    )[source] ?? source
  )
}

export function inputCoverageLabel(run: BusinessConsoleMrpRunItem) {
  const start = run.inputCoverageStart ?? run.horizonStart
  const end = run.inputCoverageEnd ?? run.horizonEnd
  return `${formatDate(start)} ~ ${formatDate(end)}`
}

export function parseVersionReference(reference?: string | null) {
  if (!reference) return {}
  const separator = reference.lastIndexOf(':')
  if (separator <= 0 || separator >= reference.length - 1) return {}
  return {
    bomCode: reference.slice(0, separator),
    revision: reference.slice(separator + 1),
  }
}

export function explanationRows(row: BusinessConsolePlanningSuggestionItem) {
  const explanation = row.netRequirementExplanation
  if (!explanation) return []
  const grossDemandLabel = explanation.primarySourceType === 'component' ? '组件毛需求' : '总需求'
  return [
    { label: grossDemandLabel, value: explanation.grossDemandQuantity },
    { label: '现有库存', value: explanation.onHandQuantity },
    { label: '预留', value: explanation.reservedQuantity },
    { label: '可抵扣库存', value: explanation.availableToNetQuantity },
    { label: '在途', value: explanation.scheduledReceiptQuantity },
    { label: '安全库存', value: explanation.safetyStockQuantity },
    { label: '净需求', value: explanation.netRequirementQuantity },
    { label: '建议量', value: explanation.plannedQuantity },
  ]
}
