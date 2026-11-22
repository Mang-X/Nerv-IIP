import {
  type WalkthroughContext,
  queryPath,
  SALES_QUOTATION_NO,
  SALES_ORDER_NO,
  SITE_CODE,
  FINISHED_SKU,
} from './context'
import {
  rowsOf,
  textOf,
  publicJson,
  dateOnly,
  asRecord,
  dataOf,
} from '../issue1912-walkthrough-evidence'

export async function runPlanning(context: WalkthroughContext) {
  const { organizationId, environmentId, erpListQuery, generatedAtUtc } = context
  const { call, pollRows } = context.session
  const { record } = context.ledger
  const { provePageSafely } = context.proofs

  const salesQuotation = await call(
    'GET',
    queryPath('/api/business-console/v1/erp/sales/quotations', {
      organizationId,
      environmentId,
      keyword: SALES_QUOTATION_NO,
      skip: 0,
      take: 100,
    }),
  )
  const quotationRow = rowsOf(salesQuotation.payload).find(
    (row) => textOf(row.quotationNo) === SALES_QUOTATION_NO,
  )
  if (!quotationRow || textOf(quotationRow.status).toLowerCase() !== 'approved')
    throw new Error(`Seed sales quotation ${SALES_QUOTATION_NO} was not approved.`)
  const salesQuotationUi = await provePageSafely('sales-quotation-sales-order', {
    route: '/erp/sales/quotations',
    listPath: '/api/business-console/v1/erp/sales/quotations',
    filterLabel: '报价单关键字',
    expectedListQuery: erpListQuery(),
    stableText: SALES_QUOTATION_NO,
    emptyText: '还没有报价单。可从销售机会或客户需求创建报价。',
    screenshotName: '08-sales-quotation.png',
  })
  const salesOrder = await call('POST', '/api/business-console/v1/erp/sales/sales-orders', {
    organizationId,
    environmentId,
    salesOrderNo: SALES_ORDER_NO,
    quotationNo: SALES_QUOTATION_NO,
    siteCode: SITE_CODE,
    idempotencyKey: `issue1912-${SALES_ORDER_NO}`,
  })
  const salesOrderRow = await pollRows(
    '/api/business-console/v1/erp/sales/sales-orders',
    {
      organizationId,
      environmentId,
      keyword: SALES_ORDER_NO,
      skip: 0,
      take: 100,
    },
    (row) => textOf(row.salesOrderNo) === SALES_ORDER_NO,
  )
  const salesOrderUi = await provePageSafely('sales-quotation-sales-order', {
    route: `/erp/sales/orders?keyword=${encodeURIComponent(SALES_ORDER_NO)}`,
    listPath: '/api/business-console/v1/erp/sales/sales-orders',
    filterLabel: '销售订单关键字',
    filterResponseMode: 'server',
    expectedListQuery: erpListQuery(),
    stableText: SALES_ORDER_NO,
    emptyText: '还没有销售订单。批准报价后可在这里生成订单。',
    screenshotName: '09-sales-order.png',
  })
  record({
    node: 'sales-quotation-sales-order',
    sourceObject: SALES_QUOTATION_NO,
    downstreamObject: SALES_ORDER_NO,
    stableKey: `${SALES_QUOTATION_NO} -> ${SALES_ORDER_NO}`,
    automationMode: 'manual',
    request: salesOrder.summary,
    responseOrLog: {
      quotation: publicJson(quotationRow),
      salesOrder: publicJson(salesOrderRow.match),
      ui: [salesQuotationUi, salesOrderUi],
    },
    conclusion: 'runtime-confirmed',
    demoWording:
      '销售报价页面证明 QUO-WALK-001 已批准，销售订单页面以 HTTP 200 渲染稳定 SO-WALK-001 行。',
    responsibilityIssue: null,
  })

  const demand = await pollRows(
    '/api/business-console/v1/planning/demands',
    { organizationId, environmentId },
    (row) => textOf(row.sourceReference) === SALES_ORDER_NO,
  )
  const demandUi = await provePageSafely('sales-order-demand', {
    route: '/planning',
    listPath: '/api/business-console/v1/planning/demands',
    filterLabel: '需求池关键字',
    filterResponseMode: 'client',
    stableText: SALES_ORDER_NO,
    emptyText: '当前范围没有计划需求。',
    screenshotName: '10-planning-demand.png',
  })
  record({
    node: 'sales-order-demand',
    sourceObject: SALES_ORDER_NO,
    downstreamObject: textOf(demand.match.demandSourceId),
    stableKey: `${SALES_ORDER_NO} -> ${textOf(demand.match.demandSourceId)}`,
    automationMode: 'automatic',
    request: demand.call.summary,
    responseOrLog: { demand: publicJson(demand.match), ui: demandUi },
    conclusion: 'runtime-confirmed',
    demoWording: '销售订单跨 Redis 后在 Planning 页面需求池出现同一 SO-WALK-001 来源行。',
    responsibilityIssue: null,
  })

  const horizonStart = dateOnly(new Date(generatedAtUtc.getTime() - 86_400_000))
  const mrp = await call('POST', '/api/business-console/v1/planning/mrp-runs', {
    organizationId,
    environmentId,
    horizonStart,
    horizonEnd: '2100-01-01',
  })
  const runId = textOf(asRecord(dataOf(mrp.payload)).runId)
  if (!runId) throw new Error('MRP run did not return a runId.')
  const pegging = await pollRows(
    `/api/business-console/v1/planning/mrp-runs/${encodeURIComponent(runId)}/pegging`,
    {
      organizationId,
      environmentId,
    },
    (row) => textOf(row.demandSourceReference) === SALES_ORDER_NO,
    120_000,
  )
  const suggestion = await pollRows(
    '/api/business-console/v1/planning/suggestions',
    { organizationId, environmentId },
    (row) =>
      textOf(row.runId) === runId &&
      textOf(row.suggestionType) === 'planned-work-order' &&
      textOf(row.skuCode) === FINISHED_SKU,
    120_000,
  )
  const suggestionUi = await provePageSafely('demand-mrp-suggestion', {
    route: '/planning',
    listPath: '/api/business-console/v1/planning/suggestions',
    stableText: FINISHED_SKU,
    tabText: /计划建议/,
    reuseCurrentRoute: true,
    refreshListBeforeProof: true,
    emptyText: '当前范围没有计划建议。',
    screenshotName: '11-planning-suggestion.png',
  })
  record({
    node: 'demand-mrp-suggestion',
    sourceObject: SALES_ORDER_NO,
    downstreamObject: textOf(suggestion.match.suggestionId),
    stableKey: `${SALES_ORDER_NO} -> ${runId} -> ${FINISHED_SKU}`,
    automationMode: 'manual',
    request: mrp.summary,
    responseOrLog: {
      mrp: mrp.publicPayload,
      pegging: publicJson(pegging.match),
      suggestion: publicJson(suggestion.match),
      ui: suggestionUi,
    },
    conclusion: 'runtime-confirmed',
    demoWording:
      'MRP 使用包含 2099-12-31 种子需求日期的公开窗口，保留 SO-WALK-001 pegging 并在真实计划建议页展示成品生产建议。',
    responsibilityIssue: null,
  })

  const mesReadContext = await call(
    'GET',
    queryPath('/api/business-console/v1/me/work-context', {
      organizationId,
      environmentId,
      permissionCode: 'business.mes.work-orders.read',
    }),
  )
  const mesReadData = asRecord(dataOf(mesReadContext.payload))
  const mesReadScope = asRecord(
    mesReadData.selectedScope ??
      (Array.isArray(mesReadData.authorizedScopes) ? mesReadData.authorizedScopes[0] : null),
  )
  const mesScopeKind = textOf(mesReadScope.kind ?? mesReadScope.scopeKind)
  const mesScopeId = textOf(mesReadScope.id ?? mesReadScope.scopeId)
  if (!mesScopeKind || !mesScopeId)
    throw new Error('MES work-order read context returned no authorized scope.')
  const accepted = await call(
    'POST',
    queryPath(
      `/api/business-console/v1/planning/suggestions/${encodeURIComponent(textOf(suggestion.match.suggestionId))}/accept`,
      { organizationId, environmentId },
    ),
    {
      downstreamService: 'BusinessMes',
      downstreamDocumentType: 'WorkOrder',
      downstreamDocumentId: null,
      idempotencyKey: `issue1912-accept-${textOf(suggestion.match.suggestionId)}`,
    },
  )
  const acceptedData = asRecord(dataOf(accepted.payload))
  const workOrderId = textOf(acceptedData.downstreamDocumentId)
  if (!workOrderId) throw new Error('Planning suggestion acceptance returned no MES work-order ID.')
  const workOrderDetail = await call(
    'GET',
    queryPath(`/api/business-console/v1/mes/work-orders/${encodeURIComponent(workOrderId)}`, {
      organizationId,
      environmentId,
      scopeKind: mesScopeKind,
      scopeId: mesScopeId,
    }),
  )
  const workOrder = asRecord(dataOf(workOrderDetail.payload))
  const sourcePlanReference = asRecord(workOrder.sourcePlanReference)
  if (textOf(sourcePlanReference.sourceDemandReference) !== SALES_ORDER_NO)
    throw new Error(`MES work order ${workOrderId} did not preserve ${SALES_ORDER_NO}.`)
  const workOrderNo = textOf(workOrder.workOrderNo || workOrderId)
  const workOrderUi = await provePageSafely('mrp-suggestion-mes-work-order', {
    route: `/mes/work-orders?keyword=${encodeURIComponent(workOrderNo)}`,
    listPath: '/api/business-console/v1/mes/work-orders',
    stableText: workOrderNo,
    emptyText: '当前筛选下没有工单。正常生产请先进入生产计划转工单，急单只处理临时插单。',
    screenshotName: '12-mes-work-order.png',
  })
  record({
    node: 'mrp-suggestion-mes-work-order',
    sourceObject: textOf(suggestion.match.suggestionId),
    downstreamObject: workOrderId,
    stableKey: `${SALES_ORDER_NO} -> ${workOrderNo} -> ${workOrderId}`,
    automationMode: 'automatic',
    request: accepted.summary,
    responseOrLog: { workOrder: publicJson(workOrder), ui: workOrderUi },
    conclusion: 'runtime-confirmed',
    demoWording:
      '计划建议接受后真实 MES 工单页渲染了生成的工单业务号，并通过详情公开关联回 SO-WALK-001。',
    responsibilityIssue: null,
  })

  return { workOrderId, workOrderNo }
}
