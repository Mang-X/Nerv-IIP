import type {
  BusinessConsoleDemandSourceItem,
  BusinessConsoleErpDeliveryOrderItem,
  BusinessConsoleMesProductionPlanRow,
  BusinessConsoleMrpPeggingItem,
  BusinessConsolePlanningSuggestionItem,
} from '@nerv-iip/api-client'
import { describe, expect, it } from 'vitest'
import {
  classifyFulfillmentFailure,
  describeMrpSuggestion,
  describeUrgencyLevel,
  describeWorkOrderLink,
  FulfillmentNodeError,
  matchDeliveryOrders,
  matchDemandPeggings,
  matchDemandSource,
  matchPlanningSuggestion,
  matchProductionPlanRow,
  matchSuggestionWorkOrder,
  normalizeScope,
  peggingSuggestionIds,
  resolveRecordNode,
} from './useFulfillmentTimeline'

describe('classifyFulfillmentFailure', () => {
  it('maps 403 to restricted', () => {
    expect(classifyFulfillmentFailure(new FulfillmentNodeError(403))).toEqual({
      status: 'restricted',
    })
  })

  it('maps 409 to failed/conflict', () => {
    expect(classifyFulfillmentFailure(new FulfillmentNodeError(409))).toEqual({
      status: 'failed',
      failureKind: 'conflict',
    })
  })

  it('maps network and gateway-timeout to failed/timeout', () => {
    expect(classifyFulfillmentFailure(new FulfillmentNodeError('network'))).toEqual({
      status: 'failed',
      failureKind: 'timeout',
    })
    expect(classifyFulfillmentFailure(new FulfillmentNodeError(504))).toEqual({
      status: 'failed',
      failureKind: 'timeout',
    })
  })

  it('maps other errors to failed/error', () => {
    expect(classifyFulfillmentFailure(new FulfillmentNodeError(500))).toEqual({
      status: 'failed',
      failureKind: 'error',
    })
  })

  it('reads a bare status field defensively', () => {
    expect(classifyFulfillmentFailure({ status: 403 })).toEqual({ status: 'restricted' })
    expect(classifyFulfillmentFailure({ statusCode: 409 })).toEqual({
      status: 'failed',
      failureKind: 'conflict',
    })
  })
})

describe('normalizeScope', () => {
  it('trims and blanks empty scope', () => {
    expect(normalizeScope('  SO-1 ')).toBe('SO-1')
    expect(normalizeScope('   ')).toBeUndefined()
    expect(normalizeScope(null)).toBeUndefined()
    expect(normalizeScope(undefined)).toBeUndefined()
  })
})

describe('matchDemandSource', () => {
  const items: BusinessConsoleDemandSourceItem[] = [
    { demandSourceId: 'd1', sourceReference: 'SO-OTHER', sourceStatus: 'active' },
    { demandSourceId: 'd2', sourceReference: 'SO-1', sourceStatus: 'released' },
  ]

  it('matches by sourceReference === salesOrderNo', () => {
    expect(matchDemandSource(items, 'SO-1')?.demandSourceId).toBe('d2')
  })

  it('never guesses by similar codes and suppresses empty scope', () => {
    expect(matchDemandSource(items, 'SO-2')).toBeUndefined()
    expect(matchDemandSource(items, undefined)).toBeUndefined()
  })
})

// 合批场景（#1304 走查实证）：SO-A 与 SO-B 被合并成同一张工单 WO-20260731-000001，
// MRP 建议对两张订单各留一行 demand pegging，工单来源引用的首条需求引用是 SO-A。
const MERGED_SUGGESTION_ID = '0199e1f3-0000-7000-8000-000000000001'
const mergedPeggings: BusinessConsoleMrpPeggingItem[] = [
  {
    suggestionId: MERGED_SUGGESTION_ID,
    peggingType: 'demand',
    demandSourceReference: 'SO-A',
    parentSkuCode: 'SKU-FG-100',
    quantity: 60,
  },
  {
    suggestionId: MERGED_SUGGESTION_ID,
    peggingType: 'demand',
    demandSourceReference: 'SO-B',
    parentSkuCode: 'SKU-FG-100',
    quantity: 40,
  },
  {
    suggestionId: MERGED_SUGGESTION_ID,
    peggingType: 'scheduled-receipt',
    demandSourceReference: 'BusinessErp:PurchaseOrder:SO-A',
    parentSkuCode: 'SKU-FG-100',
    quantity: 10,
  },
]

const mergedSuggestions: BusinessConsolePlanningSuggestionItem[] = [
  {
    suggestionId: 'other-suggestion',
    skuCode: 'SKU-FG-200',
    quantity: 5,
    uomCode: 'PCS',
    status: 'accepted',
    downstreamService: 'BusinessMes',
    downstreamDocumentType: 'WorkOrder',
    downstreamDocumentId: 'WO-20260731-000009',
  },
  {
    suggestionId: MERGED_SUGGESTION_ID,
    skuCode: 'SKU-FG-100',
    quantity: 100,
    uomCode: 'PCS',
    status: 'accepted',
    downstreamService: 'BusinessMes',
    downstreamDocumentType: 'WorkOrder',
    downstreamDocumentId: 'WO-20260731-000001',
  },
]

const mergedPlanRows: BusinessConsoleMesProductionPlanRow[] = [
  {
    productionPlanId: MERGED_SUGGESTION_ID,
    sourceSystem: 'DemandPlanning',
    sourceDocumentType: 'PlanningSuggestion',
    sourceDocumentId: MERGED_SUGGESTION_ID,
    sourceDemandReference: 'SO-A',
    skuId: 'SKU-FG-100',
    plannedQuantity: 100,
    status: 'released',
  },
]

describe('MRP pegging → 建议 → MES 工单（含合批）', () => {
  it('只认 demand 类型 pegging，scheduled-receipt 的复合引用不参与匹配', () => {
    expect(matchDemandPeggings(mergedPeggings, 'SO-A')).toHaveLength(1)
    expect(matchDemandPeggings(mergedPeggings, 'BusinessErp:PurchaseOrder:SO-A')).toEqual([])
  })

  it('合批工单：两张订单各自都能命中同一个建议', () => {
    const fromA = peggingSuggestionIds(matchDemandPeggings(mergedPeggings, 'SO-A'))
    const fromB = peggingSuggestionIds(matchDemandPeggings(mergedPeggings, 'SO-B'))
    expect(fromA).toEqual([MERGED_SUGGESTION_ID])
    expect(fromB).toEqual([MERGED_SUGGESTION_ID])
  })

  it('空 scope 不匹配（不发请求的前置条件）', () => {
    expect(matchDemandPeggings(mergedPeggings, undefined)).toEqual([])
    expect(peggingSuggestionIds([])).toEqual([])
  })

  it('合批工单：两张订单都定位到同一张工单号', () => {
    for (const salesOrderNo of ['SO-A', 'SO-B']) {
      const ids = peggingSuggestionIds(matchDemandPeggings(mergedPeggings, salesOrderNo))
      const workOrder = matchSuggestionWorkOrder(mergedSuggestions, ids)
      expect(workOrder).toEqual({
        suggestionId: MERGED_SUGGESTION_ID,
        workOrderNo: 'WO-20260731-000001',
      })
      expect(matchPlanningSuggestion(mergedSuggestions, ids)?.skuCode).toBe('SKU-FG-100')
      expect(matchProductionPlanRow(mergedPlanRows, workOrder?.suggestionId)?.status).toBe(
        'released',
      )
    }
  })

  // 演示者一定会多跑几次 MRP：接受建议后再跑一次，本单会多出一条尚未被接受的新建议，
  // 而工单挂在旧建议上。只看最新一条就会把工单节点误判成空态。
  it('重跑 MRP：新建议未接受时，工单节点仍由旧建议点亮', () => {
    const NEW_SUGGESTION_ID = '0199e1f3-0000-7000-8000-000000000002'
    // 扫描窗内「新运行在前」
    const idsAfterRerun = [NEW_SUGGESTION_ID, MERGED_SUGGESTION_ID]
    const suggestions: BusinessConsolePlanningSuggestionItem[] = [
      ...mergedSuggestions,
      { suggestionId: NEW_SUGGESTION_ID, skuCode: 'SKU-FG-100', quantity: 100, status: 'open' },
    ]

    // 展示取最新那条建议……
    expect(matchPlanningSuggestion(suggestions, idsAfterRerun)?.suggestionId).toBe(
      NEW_SUGGESTION_ID,
    )
    // ……但工单沿列表往回找，仍然点亮。
    const workOrder = matchSuggestionWorkOrder(suggestions, idsAfterRerun)
    expect(workOrder).toEqual({
      suggestionId: MERGED_SUGGESTION_ID,
      workOrderNo: 'WO-20260731-000001',
    })
    // 生产计划行也跟着解析出的那条建议走，而不是最新那条。
    expect(matchProductionPlanRow(mergedPlanRows, workOrder?.suggestionId)?.status).toBe('released')
    expect(matchProductionPlanRow(mergedPlanRows, NEW_SUGGESTION_ID)).toBeUndefined()
  })

  it('下游引用码值大小写/分隔符两种口径都认', () => {
    expect(
      matchSuggestionWorkOrder(
        [
          {
            suggestionId: MERGED_SUGGESTION_ID,
            downstreamService: 'business-mes',
            downstreamDocumentType: 'work-order',
            downstreamDocumentId: 'WO-20260731-000001',
          },
        ],
        [MERGED_SUGGESTION_ID],
      )?.workOrderNo,
    ).toBe('WO-20260731-000001')
  })

  it('别的下游单据（采购申请）不冒充工单，未接受的建议如实空态', () => {
    const ids = [MERGED_SUGGESTION_ID]
    expect(
      matchSuggestionWorkOrder(
        [
          {
            suggestionId: MERGED_SUGGESTION_ID,
            downstreamService: 'BusinessErp',
            downstreamDocumentType: 'PurchaseRequisition',
            downstreamDocumentId: 'PR-20260731-000001',
          },
        ],
        ids,
      ),
    ).toBeUndefined()
    expect(
      matchSuggestionWorkOrder([{ suggestionId: MERGED_SUGGESTION_ID, status: 'open' }], ids),
    ).toBeUndefined()
    expect(matchSuggestionWorkOrder(mergedSuggestions, [])).toBeUndefined()
    expect(matchPlanningSuggestion(mergedSuggestions, [])).toBeUndefined()
  })

  it('合批工单的首条需求引用是别的订单号，不能拿来把本单排除掉', () => {
    expect(matchProductionPlanRow(mergedPlanRows, MERGED_SUGGESTION_ID)?.sourceDocumentId).toBe(
      MERGED_SUGGESTION_ID,
    )
  })

  // 行上没有工单号，兜底命中无从校验是不是解析出来的那张工单——宁可不贴状态，也不贴错状态。
  it('不按销售单号兜底贴状态：别的工单的行不会被贴到本节点', () => {
    const otherWorkOrderRow: BusinessConsoleMesProductionPlanRow = {
      productionPlanId: 'another-suggestion',
      sourceDocumentId: 'another-suggestion',
      sourceDemandReference: 'SO-A',
      skuId: 'SKU-FG-100',
      status: 'closed',
    }
    expect(matchProductionPlanRow([otherWorkOrderRow], MERGED_SUGGESTION_ID)).toBeUndefined()
    expect(matchProductionPlanRow(mergedPlanRows, undefined)).toBeUndefined()
  })
})

describe('节点文案：不显裸 GUID、合批如实说明', () => {
  it('MRP 建议用「物料 × 数量」自识别，绝不显 suggestionId', () => {
    const label = describeMrpSuggestion({
      pegging: mergedPeggings[0]!,
      suggestion: mergedSuggestions[1]!,
    })
    expect(label).toBe('SKU-FG-100 × 100 PCS')
    expect(label).not.toContain(MERGED_SUGGESTION_ID)
  })

  it('建议本体缺席时回落 pegging 上的物料与数量', () => {
    expect(describeMrpSuggestion({ pegging: mergedPeggings[1]! })).toBe('SKU-FG-100 × 40')
  })

  it('合批工单明说同时承接别的订单，非合批不加噪声', () => {
    const record = { workOrderNo: 'WO-20260731-000001', planRow: mergedPlanRows[0]! }
    expect(describeWorkOrderLink(record, 'SO-B')).toContain('该工单为合批工单，同时承接 SO-A')
    expect(describeWorkOrderLink(record, 'SO-A')).not.toContain('合批')
  })

  // #1418：抽屉曾把后端 level 原值摆成徽标，演示里就是一枚英文 `highrisk`。
  it('排程紧急度徽标走中文映射，绝不吐后端英文枚举', () => {
    expect(describeUrgencyLevel('highrisk')).toBe('高风险')
    expect(describeUrgencyLevel('urgent')).toBe('紧急')
    expect(describeUrgencyLevel('HighRisk')).toBe('高风险')
    expect(describeUrgencyLevel('highrisk')).not.toMatch(/[a-z]/i)
    expect(describeUrgencyLevel(null)).toBeUndefined()
  })
})

describe('matchDeliveryOrders', () => {
  const items: BusinessConsoleErpDeliveryOrderItem[] = [
    { deliveryOrderNo: 'DO-1', salesOrderNo: 'SO-1', status: 'released' },
    { deliveryOrderNo: 'DO-2', salesOrderNo: 'SO-OTHER', status: 'released' },
  ]

  it('filters by salesOrderNo', () => {
    expect(matchDeliveryOrders(items, 'SO-1').map((d) => d.deliveryOrderNo)).toEqual(['DO-1'])
  })

  it('returns empty for empty scope', () => {
    expect(matchDeliveryOrders(items, undefined)).toEqual([])
  })
})

describe('resolveRecordNode — four-state machine', () => {
  const base = {
    key: 'delivery-order' as const,
    title: '发货单',
    present: (record: { deliveryOrderNo?: string; status?: string }) => ({
      businessNo: record.deliveryOrderNo,
      detailStatus: record.status,
    }),
    pendingNote: '尚未产生规则说明',
    source: 'ERP · 发货单读面',
  }

  it('established: exposes business number and drill fields', () => {
    const node = resolveRecordNode({
      ...base,
      enabled: true,
      loading: false,
      error: undefined,
      record: { deliveryOrderNo: 'DO-1', status: 'released' },
    })
    expect(node.status).toBe('established')
    expect(node.businessNo).toBe('DO-1')
    expect(node.detailStatus).toBe('released')
  })

  it('pending (no scope): empty scope shows rule note, not established', () => {
    const node = resolveRecordNode({
      ...base,
      enabled: false,
      loading: false,
      error: undefined,
      record: undefined,
    })
    expect(node.status).toBe('pending')
    expect(node.ruleNote).toBe('尚未产生规则说明')
  })

  it('pending (fetched, no record): distinct empty state', () => {
    const node = resolveRecordNode({
      ...base,
      enabled: true,
      loading: false,
      error: undefined,
      record: undefined,
    })
    expect(node.status).toBe('pending')
    expect(node.ruleNote).toBe('尚未产生规则说明')
  })

  it('loading: while fetching with no record yet', () => {
    const node = resolveRecordNode({
      ...base,
      enabled: true,
      loading: true,
      error: undefined,
      record: undefined,
    })
    expect(node.status).toBe('loading')
  })

  it('restricted: a 403 on a single source does not leak data', () => {
    const node = resolveRecordNode({
      ...base,
      enabled: true,
      loading: false,
      error: new FulfillmentNodeError(403),
      record: undefined,
    })
    expect(node.status).toBe('restricted')
    expect(node.businessNo).toBeUndefined()
  })

  it('failed: a single-source error carries a distinguishable failure kind', () => {
    const conflict = resolveRecordNode({
      ...base,
      enabled: true,
      loading: false,
      error: new FulfillmentNodeError(409),
      record: undefined,
    })
    expect(conflict.status).toBe('failed')
    expect(conflict.failureKind).toBe('conflict')

    const timeout = resolveRecordNode({
      ...base,
      enabled: true,
      loading: false,
      error: new FulfillmentNodeError('network'),
      record: undefined,
    })
    expect(timeout.failureKind).toBe('timeout')
  })

  it('error wins even if a stale record is present (failure is not faked as empty)', () => {
    const node = resolveRecordNode({
      ...base,
      enabled: true,
      loading: false,
      error: new FulfillmentNodeError(500),
      record: { deliveryOrderNo: 'DO-1' },
    })
    expect(node.status).toBe('failed')
    expect(node.failureKind).toBe('error')
  })
})
