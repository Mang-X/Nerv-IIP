import type {
  BusinessConsoleDemandSourceItem,
  BusinessConsoleMpsBucketItem,
  BusinessConsolePlanningSuggestionItem,
} from '@nerv-iip/api-client'

/**
 * MRP 时段视图的纯聚合逻辑：把需求池 / MPS / 计划建议三个集合
 * 按同一套期间键（周/月）对齐，供图表组件直接消费。
 * 只做只读派生，不发请求、不改任何输入。
 */

export type PlanningGranularity = 'week' | 'month'

/** 供给型建议（会形成补充量的两类）；调整/取消类不计入“建议补充”数量。 */
const SUPPLY_SUGGESTION_TYPES = new Set(['planned-work-order', 'planned-purchase'])

export interface PlanningPeriod {
  /** 稳定排序键：周＝周一 ISO 日期，月＝YYYY-MM。 */
  key: string
  /** 图表 x 轴标签（短横轴场景下可读优先）。 */
  label: string
}

export function planningPeriodOf(
  date: string | null | undefined,
  granularity: PlanningGranularity,
): PlanningPeriod | null {
  const day = (date ?? '').slice(0, 10)
  if (!/^\d{4}-\d{2}-\d{2}$/.test(day)) return null

  if (granularity === 'month') {
    const key = day.slice(0, 7)
    return { key, label: key }
  }

  const parsed = new Date(`${day}T00:00:00Z`)
  if (Number.isNaN(parsed.getTime())) return null
  // 周一作为周起点（getUTCDay: 0=周日）。
  parsed.setUTCDate(parsed.getUTCDate() - ((parsed.getUTCDay() + 6) % 7))
  const key = parsed.toISOString().slice(0, 10)
  // 标签带两位年份：计划范围可能跨年，纯 MM-DD 会把去年 12 月与今年 12 月画成同名期间。
  return { key, label: `${key.slice(2)}周` }
}

function isActiveDemand(demand: BusinessConsoleDemandSourceItem) {
  return demand.sourceStatus?.toLowerCase() !== 'cancelled'
}

function isSupplySuggestion(suggestion: BusinessConsolePlanningSuggestionItem) {
  return SUPPLY_SUGGESTION_TYPES.has(suggestion.suggestionType ?? '')
}

function inScope(skuCode: string | null | undefined, scope: ReadonlySet<string> | null) {
  if (!scope) return true
  return !!skuCode && scope.has(skuCode)
}

export interface TimePhasedRow extends Record<string, number | string> {
  period: string
  demand: number
  mps: number
  suggestion: number
}

/**
 * 毛需求 / 主计划 / 建议补充三条序列按期间对齐。
 * scope 为 null 表示不过滤 SKU；数量为直接相加（混合单位时仅作趋势参考，
 * 由调用方负责在 UI 上作出提示）。
 *
 * 建议序列必须限定单次运行（suggestionRunId）：后端 RunMrp 不关闭历史运行的
 * Open 建议，跨运行求和会随运行次数线性膨胀；未指定运行时建议序列为 0。
 */
export function buildTimePhasedRows(
  demands: readonly BusinessConsoleDemandSourceItem[],
  mpsBuckets: readonly BusinessConsoleMpsBucketItem[],
  suggestions: readonly BusinessConsolePlanningSuggestionItem[],
  granularity: PlanningGranularity,
  scope: ReadonlySet<string> | null,
  suggestionRunId: string,
): TimePhasedRow[] {
  const byKey = new Map<string, TimePhasedRow & { key: string }>()
  const rowOf = (period: PlanningPeriod) => {
    let row = byKey.get(period.key)
    if (!row) {
      row = { key: period.key, period: period.label, demand: 0, mps: 0, suggestion: 0 }
      byKey.set(period.key, row)
    }
    return row
  }

  for (const demand of demands) {
    if (!isActiveDemand(demand) || !inScope(demand.skuCode, scope)) continue
    const period = planningPeriodOf(demand.dueDate, granularity)
    if (period) rowOf(period).demand += demand.quantity ?? 0
  }
  for (const bucket of mpsBuckets) {
    if (!inScope(bucket.skuCode, scope)) continue
    const period = planningPeriodOf(bucket.bucketDate, granularity)
    if (period) rowOf(period).mps += bucket.quantity ?? 0
  }
  for (const suggestion of suggestions) {
    if (!suggestionRunId || suggestion.runId !== suggestionRunId) continue
    if (!isSupplySuggestion(suggestion) || !inScope(suggestion.skuCode, scope)) continue
    const period = planningPeriodOf(suggestion.requiredDate, granularity)
    if (period) rowOf(period).suggestion += suggestion.quantity ?? 0
  }

  return [...byKey.values()]
    .sort((a, b) => a.key.localeCompare(b.key))
    .map(({ key: _key, ...row }) => row)
}

/**
 * 时段视图实际参与相加的计量单位集合（与 buildTimePhasedRows 同一套过滤：
 * 未取消需求 + scope 内 MPS + 指定运行的供给型建议）。
 * 超过一种单位时数量相加只剩趋势意义，由 UI 提示。
 */
export function phasedUomCodes(
  demands: readonly BusinessConsoleDemandSourceItem[],
  mpsBuckets: readonly BusinessConsoleMpsBucketItem[],
  suggestions: readonly BusinessConsolePlanningSuggestionItem[],
  scope: ReadonlySet<string> | null,
  suggestionRunId: string,
): Set<string> {
  const uoms = new Set<string>()
  for (const demand of demands) {
    if (!isActiveDemand(demand) || !inScope(demand.skuCode, scope)) continue
    if (demand.uomCode) uoms.add(demand.uomCode)
  }
  for (const bucket of mpsBuckets) {
    if (!inScope(bucket.skuCode, scope)) continue
    if (bucket.uomCode) uoms.add(bucket.uomCode)
  }
  for (const suggestion of suggestions) {
    if (!suggestionRunId || suggestion.runId !== suggestionRunId) continue
    if (!isSupplySuggestion(suggestion) || !inScope(suggestion.skuCode, scope)) continue
    if (suggestion.uomCode) uoms.add(suggestion.uomCode)
  }
  return uoms
}

/** 按（未取消）需求量排序的 Top N 需求物料。 */
export function topDemandSkuCodes(
  demands: readonly BusinessConsoleDemandSourceItem[],
  limit: number,
): string[] {
  const totals = new Map<string, number>()
  for (const demand of demands) {
    if (!isActiveDemand(demand) || !demand.skuCode) continue
    totals.set(demand.skuCode, (totals.get(demand.skuCode) ?? 0) + (demand.quantity ?? 0))
  }
  return [...totals.entries()]
    .sort((a, b) => b[1] - a[1])
    .slice(0, limit)
    .map(([code]) => code)
}

export interface CoverageRow extends Record<string, number | string> {
  period: string
  demandSkuCount: number
  coveredSkuCount: number
}

/**
 * 需求覆盖时段展开：每个期间「有需求的物料数」vs「其中已生成供给建议的物料数」。
 * 覆盖判定与 KPI 口径一致——物料级（该 SKU 在任意期间出现供给建议即视为已覆盖），
 * 计数无单位问题，可跨 SKU 汇总。
 */
export function buildCoverageRows(
  demands: readonly BusinessConsoleDemandSourceItem[],
  suggestions: readonly BusinessConsolePlanningSuggestionItem[],
  granularity: PlanningGranularity,
): CoverageRow[] {
  const coveredSkus = new Set<string>()
  for (const suggestion of suggestions) {
    if (isSupplySuggestion(suggestion) && suggestion.skuCode) coveredSkus.add(suggestion.skuCode)
  }

  const byKey = new Map<string, { key: string; period: string; skus: Set<string> }>()
  for (const demand of demands) {
    if (!isActiveDemand(demand) || !demand.skuCode) continue
    const period = planningPeriodOf(demand.dueDate, granularity)
    if (!period) continue
    let entry = byKey.get(period.key)
    if (!entry) {
      entry = { key: period.key, period: period.label, skus: new Set() }
      byKey.set(period.key, entry)
    }
    entry.skus.add(demand.skuCode)
  }

  return [...byKey.values()]
    .sort((a, b) => a.key.localeCompare(b.key))
    .map((entry) => ({
      period: entry.period,
      demandSkuCount: entry.skus.size,
      coveredSkuCount: [...entry.skus].filter((sku) => coveredSkus.has(sku)).length,
    }))
}

export interface RunSuggestionRow extends Record<string, number | string> {
  period: string
  production: number
  purchase: number
  adjustment: number
}

/**
 * 单次 MRP 运行的建议分布：按目标期间 × 建议类型（生产 / 采购 / 调整异常）计条数。
 * 条数无单位问题；数量跨物料相加会失真，故这里刻意用条数。
 */
export function buildRunSuggestionRows(
  suggestions: readonly BusinessConsolePlanningSuggestionItem[],
  runId: string,
  granularity: PlanningGranularity,
): RunSuggestionRow[] {
  const byKey = new Map<string, RunSuggestionRow & { key: string }>()
  for (const suggestion of suggestions) {
    if (!runId || suggestion.runId !== runId) continue
    const period = planningPeriodOf(suggestion.requiredDate, granularity)
    if (!period) continue
    let row = byKey.get(period.key)
    if (!row) {
      row = { key: period.key, period: period.label, production: 0, purchase: 0, adjustment: 0 }
      byKey.set(period.key, row)
    }
    if (suggestion.suggestionType === 'planned-work-order') row.production += 1
    else if (suggestion.suggestionType === 'planned-purchase') row.purchase += 1
    else row.adjustment += 1
  }

  return [...byKey.values()]
    .sort((a, b) => a.key.localeCompare(b.key))
    .map(({ key: _key, ...row }) => row)
}
