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

/**
 * 已经作废的建议状态：不再代表任何补充方案——刚点完「拒绝」，覆盖率就不该还把这条算进去。
 *
 * 刻意写成**黑名单**而非「open/accepted 白名单」：facade 若某天回一个新状态（或压根没回
 * status），白名单会把全部建议静默判死、覆盖率一夜归零；黑名单最坏只是多算一条，
 * 不会把整块读数变成谎话。
 */
const DEAD_SUGGESTION_STATUSES = new Set(['rejected', 'cancelled', 'canceled', 'superseded'])

function isLiveSuggestion(suggestion: BusinessConsolePlanningSuggestionItem) {
  return !DEAD_SUGGESTION_STATUSES.has((suggestion.status ?? '').toLowerCase())
}

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

/** 供给型 + 状态仍然算数：三条序列（数量、单位、覆盖）共用同一条「这条建议还算数吗」。 */
function isSupplySuggestion(suggestion: BusinessConsolePlanningSuggestionItem) {
  return (
    SUPPLY_SUGGESTION_TYPES.has(suggestion.suggestionType ?? '') && isLiveSuggestion(suggestion)
  )
}

/**
 * 覆盖口径的**单一判据**：顶部覆盖率 KPI、需求池「覆盖」列、覆盖时段图共用一份，
 * 三处口径不会再各说各话。两个条件缺一不可：
 * 1. 仍然算数的供给型建议（调整/取消类只是修正既有收货，拒绝掉的也不构成补充方案）；
 * 2. 属于指定的那一次 MRP 运行——后端 RunMrp 不关闭历史运行的 Open 建议，
 *    跨运行统计会随运行次数虚高；这与时段对比图锁单次运行是同一条规矩。
 *    `runId` 为空（尚未运行 MRP）时无任何建议算数。
 */
export function countsTowardCoverage(
  suggestion: BusinessConsolePlanningSuggestionItem,
  runId: string,
): boolean {
  if (!runId || suggestion.runId !== runId) return false
  return isSupplySuggestion(suggestion)
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
 * 覆盖判定与顶部 KPI 完全同一判据（`countsTowardCoverage`）——物料级（该 SKU 在
 * 任意期间出现有效供给建议即视为已覆盖），计数无单位问题，可跨 SKU 汇总。
 */
export function buildCoverageRows(
  demands: readonly BusinessConsoleDemandSourceItem[],
  suggestions: readonly BusinessConsolePlanningSuggestionItem[],
  granularity: PlanningGranularity,
  suggestionRunId: string,
): CoverageRow[] {
  const coveredSkus = coveredDemandSkuCodes(suggestions, suggestionRunId)

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

/**
 * 指定运行下「已被有效供给建议覆盖」的物料集合。
 * 顶部覆盖率 KPI / 需求池覆盖列 / 覆盖时段图都读这一份，避免三处各写一遍过滤。
 * 可选 `demandSkuCodes`：只保留确实有需求的物料（KPI 分子不能超过分母）。
 */
export function coveredDemandSkuCodes(
  suggestions: readonly BusinessConsolePlanningSuggestionItem[],
  suggestionRunId: string,
  demandSkuCodes?: ReadonlySet<string>,
): Set<string> {
  const covered = new Set<string>()
  for (const suggestion of suggestions) {
    if (!countsTowardCoverage(suggestion, suggestionRunId)) continue
    const skuCode = suggestion.skuCode
    if (!skuCode) continue
    if (demandSkuCodes && !demandSkuCodes.has(skuCode)) continue
    covered.add(skuCode)
  }
  return covered
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
