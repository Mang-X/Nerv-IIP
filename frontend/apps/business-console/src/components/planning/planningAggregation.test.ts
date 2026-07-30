import { describe, expect, it } from 'vitest'

import {
  buildCoverageRows,
  buildRunSuggestionRows,
  buildTimePhasedRows,
  countsTowardCoverage,
  coveredDemandSkuCodes,
  phasedUomCodes,
  planningPeriodOf,
  topDemandSkuCodes,
} from './planningAggregation'

const demands = [
  {
    demandSourceId: 'd-1',
    skuCode: 'FG-A',
    uomCode: 'pcs',
    quantity: 10,
    dueDate: '2026-06-03',
    sourceStatus: 'active',
  },
  {
    demandSourceId: 'd-2',
    skuCode: 'FG-A',
    uomCode: 'pcs',
    quantity: 5,
    dueDate: '2026-06-04',
    sourceStatus: 'active',
  },
  {
    demandSourceId: 'd-3',
    skuCode: 'FG-B',
    uomCode: 'pcs',
    quantity: 8,
    dueDate: '2026-06-10',
    sourceStatus: 'active',
  },
  // 已取消的需求不得进入任何聚合。
  {
    demandSourceId: 'd-4',
    skuCode: 'FG-C',
    uomCode: 'pcs',
    quantity: 99,
    dueDate: '2026-06-10',
    sourceStatus: 'cancelled',
  },
]

const mpsBuckets = [
  { mpsId: 'm-1', skuCode: 'FG-A', uomCode: 'pcs', quantity: 12, bucketDate: '2026-06-01' },
  { mpsId: 'm-2', skuCode: 'FG-B', uomCode: 'pcs', quantity: 6, bucketDate: '2026-06-08' },
]

const suggestions = [
  {
    suggestionId: 's-1',
    runId: 'run-1',
    suggestionType: 'planned-work-order',
    skuCode: 'FG-A',
    uomCode: 'pcs',
    quantity: 4,
    requiredDate: '2026-06-02',
  },
  // 组件采购建议：单位是 kg，混入 pcs 序列时必须触发混合单位提示。
  {
    suggestionId: 's-2',
    runId: 'run-1',
    suggestionType: 'planned-purchase',
    skuCode: 'RM-X',
    uomCode: 'kg',
    quantity: 27.5,
    requiredDate: '2026-06-09',
  },
  // 调整类建议不是供给补充，不进数量序列，但进运行分布的「调整」计数。
  {
    suggestionId: 's-3',
    runId: 'run-1',
    suggestionType: 'reschedule-out',
    skuCode: 'FG-A',
    uomCode: 'pcs',
    quantity: 8,
    requiredDate: '2026-06-09',
  },
  {
    suggestionId: 's-4',
    runId: 'run-2',
    suggestionType: 'planned-work-order',
    skuCode: 'FG-B',
    uomCode: 'pcs',
    quantity: 3,
    requiredDate: '2026-06-09',
  },
]

/**
 * 覆盖口径专用样本：同一次运行 run-1 内，FG-A 的建议已被拒绝、FG-B 的仍待评审。
 * 拒绝后 FG-A 必须掉出覆盖；跨运行的 run-2 建议不得替 run-1 撑覆盖。
 */
const coverageSuggestions = [
  {
    suggestionId: 'c-1',
    runId: 'run-1',
    suggestionType: 'planned-work-order',
    skuCode: 'FG-A',
    uomCode: 'pcs',
    quantity: 4,
    requiredDate: '2026-06-02',
    status: 'rejected',
  },
  {
    suggestionId: 'c-2',
    runId: 'run-1',
    suggestionType: 'planned-work-order',
    skuCode: 'FG-B',
    uomCode: 'pcs',
    quantity: 6,
    requiredDate: '2026-06-09',
    status: 'open',
  },
  {
    suggestionId: 'c-3',
    runId: 'run-2',
    suggestionType: 'planned-work-order',
    skuCode: 'FG-A',
    uomCode: 'pcs',
    quantity: 9,
    requiredDate: '2026-06-02',
    status: 'accepted',
  },
]

describe('planningPeriodOf', () => {
  it('maps a date to its ISO week starting Monday', () => {
    // 2026-06-03 是周三，所在周的周一是 2026-06-01。
    expect(planningPeriodOf('2026-06-03', 'week')).toEqual({
      key: '2026-06-01',
      label: '26-06-01周',
    })
    expect(planningPeriodOf('2026-06-01', 'week')?.key).toBe('2026-06-01')
    // 周日归属上一个周一。
    expect(planningPeriodOf('2026-06-07', 'week')?.key).toBe('2026-06-01')
  })

  it('keeps the year in week labels so cross-year horizons stay distinguishable', () => {
    // 2026-01-01 是周四，所在周的周一落在上一年 12-29——标签必须带年份。
    expect(planningPeriodOf('2026-01-01', 'week')).toEqual({
      key: '2025-12-29',
      label: '25-12-29周',
    })
    expect(planningPeriodOf('2026-12-28', 'week')).toEqual({
      key: '2026-12-28',
      label: '26-12-28周',
    })
  })

  it('maps a date to its month and rejects invalid input', () => {
    expect(planningPeriodOf('2026-06-15T08:00:00Z', 'month')).toEqual({
      key: '2026-06',
      label: '2026-06',
    })
    expect(planningPeriodOf('', 'week')).toBeNull()
    expect(planningPeriodOf(undefined, 'month')).toBeNull()
  })
})

describe('buildTimePhasedRows', () => {
  it('aligns demand, MPS and one run of supply suggestions on the same weekly periods', () => {
    const rows = buildTimePhasedRows(demands, mpsBuckets, suggestions, 'week', null, 'run-1')

    // 原断言曾把 run-1 (27.5) + run-2 (3) 混算成 30.5——那是错的：
    // 后端 RunMrp 不关闭历史运行的 Open 建议，跨运行求和会随运行次数线性膨胀。
    // 建议序列必须锁定单次运行，06-08周只剩 run-1 的 27.5。
    expect(rows).toEqual([
      { period: '26-06-01周', demand: 15, mps: 12, suggestion: 4 },
      { period: '26-06-08周', demand: 8, mps: 6, suggestion: 27.5 },
    ])
  })

  it('counts no suggestions when no run is specified', () => {
    const rows = buildTimePhasedRows(demands, mpsBuckets, suggestions, 'week', null, '')

    expect(rows).toEqual([
      { period: '26-06-01周', demand: 15, mps: 12, suggestion: 0 },
      { period: '26-06-08周', demand: 8, mps: 6, suggestion: 0 },
    ])
  })

  it('filters by SKU scope and drops cancelled demand', () => {
    const rows = buildTimePhasedRows(
      demands,
      mpsBuckets,
      suggestions,
      'month',
      new Set(['FG-A']),
      'run-1',
    )

    expect(rows).toEqual([{ period: '2026-06', demand: 15, mps: 12, suggestion: 4 }])
  })
})

describe('phasedUomCodes', () => {
  it('collects units from demands, MPS and the selected run suggestions', () => {
    // 需求/MPS 全 pcs，但 run-1 的组件采购建议是 kg——只扫需求池会漏报。
    expect([...phasedUomCodes(demands, mpsBuckets, suggestions, null, 'run-1')].sort()).toEqual([
      'kg',
      'pcs',
    ])
  })

  it('ignores suggestions outside the selected run and SKUs outside scope', () => {
    expect([...phasedUomCodes(demands, mpsBuckets, suggestions, null, '')]).toEqual(['pcs'])
    expect([
      ...phasedUomCodes(demands, mpsBuckets, suggestions, new Set(['FG-A']), 'run-1'),
    ]).toEqual(['pcs'])
  })
})

describe('topDemandSkuCodes', () => {
  it('ranks by active demand quantity only', () => {
    expect(topDemandSkuCodes(demands, 2)).toEqual(['FG-A', 'FG-B'])
    expect(topDemandSkuCodes(demands, 1)).toEqual(['FG-A'])
  })
})

describe('buildCoverageRows', () => {
  it('counts demand SKUs vs SKUs already covered by supply suggestions per period', () => {
    const rows = buildCoverageRows(demands, suggestions, 'week', 'run-1')

    expect(rows).toEqual([
      { period: '26-06-01周', demandSkuCount: 1, coveredSkuCount: 1 },
      // FG-B 的建议属于 run-2，不能替 run-1 撑覆盖。
      { period: '26-06-08周', demandSkuCount: 1, coveredSkuCount: 0 },
    ])
  })

  it('reports uncovered SKUs when no supply suggestion exists', () => {
    const rows = buildCoverageRows(demands, [], 'month', 'run-1')

    expect(rows).toEqual([{ period: '2026-06', demandSkuCount: 2, coveredSkuCount: 0 }])
  })

  it('drops a rejected suggestion from coverage right away', () => {
    const rows = buildCoverageRows(demands, coverageSuggestions, 'month', 'run-1')

    // 需求物料 FG-A / FG-B 两个；FG-A 的建议已拒绝 → 只剩 FG-B 算覆盖。
    expect(rows).toEqual([{ period: '2026-06', demandSkuCount: 2, coveredSkuCount: 1 }])
  })

  it('keeps coverage locked to one run, exactly like the time-phased chart', () => {
    // run-2 里 FG-A 有已接受建议，但统计的是 run-1 → 不能算进 run-1 的覆盖。
    expect(coveredDemandSkuCodes(coverageSuggestions, 'run-1')).toEqual(new Set(['FG-B']))
    expect(coveredDemandSkuCodes(coverageSuggestions, 'run-2')).toEqual(new Set(['FG-A']))
    // 尚未运行 MRP：没有任何建议算数，不能凭历史建议画出覆盖。
    expect(coveredDemandSkuCodes(coverageSuggestions, '')).toEqual(new Set())
  })

  it('still counts suggestions whose status the facade did not return', () => {
    // 黑名单口径：status 缺失不等于作废，不能因为字段没回就把覆盖清零。
    expect(countsTowardCoverage(suggestions[0], 'run-1')).toBe(true)
    expect(countsTowardCoverage(coverageSuggestions[0], 'run-1')).toBe(false)
  })

  it('never counts a covered SKU that has no demand', () => {
    const demandSkus = new Set(['FG-B'])
    expect(coveredDemandSkuCodes(suggestions, 'run-1', demandSkus)).toEqual(new Set())
    expect(coveredDemandSkuCodes(coverageSuggestions, 'run-1', demandSkus)).toEqual(
      new Set(['FG-B']),
    )
  })
})

describe('buildRunSuggestionRows', () => {
  it('splits one run into production / purchase / adjustment counts per period', () => {
    const rows = buildRunSuggestionRows(suggestions, 'run-1', 'week')

    expect(rows).toEqual([
      { period: '26-06-01周', production: 1, purchase: 0, adjustment: 0 },
      { period: '26-06-08周', production: 0, purchase: 1, adjustment: 1 },
    ])
  })

  it('returns nothing when no run is selected', () => {
    expect(buildRunSuggestionRows(suggestions, '', 'week')).toEqual([])
  })
})
