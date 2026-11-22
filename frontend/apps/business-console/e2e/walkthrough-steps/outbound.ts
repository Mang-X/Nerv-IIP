import {
  type WalkthroughContext,
  DELIVERY_ORDER_NO,
  SALES_ORDER_NO,
  QUANTITY,
  FINISHED_GOODS_LOCATION,
  queryPath,
  SITE_CODE,
  FINISHED_SKU,
} from './context'
import {
  textOf,
  publicJson,
  asRecord,
  dataOf,
  type JsonRecord,
} from '../issue1912-walkthrough-evidence'
import {
  type AuthorizedWorkPoolScope,
  selectAuthorizedWorkPoolScope,
  type AuthorizedWorkSiteScope,
  selectAuthorizedWorkSiteScope,
  buildAuthorizedWorkPoolAssignment,
  executeWalkthroughPicking,
} from '../issue1912-walkthrough-runtime'
import { expect } from '@playwright/test'
import { NERV_1571_WMS_DEFAULT_PAGE_WINDOW_INPUT } from '../issue1912-wms-walkthrough-authority'
import {
  buildWmsOutboundListQueryFacts,
  buildWmsOutboundSelectionQueryFacts,
} from '../issue1912-wms-walkthrough-facts'

export async function runOutbound(context: WalkthroughContext, producedLotNo: string) {
  const { organizationId, environmentId, erpListQuery } = context
  const { call, pollRows, workerCall, workerRuntime, workerPollRows } = context.session
  const { record, setup } = context.ledger
  const { provePageSafely, proveWmsPageSafely } = context.proofs

  const delivery = await call('POST', '/api/business-console/v1/erp/sales/delivery-orders', {
    organizationId,
    environmentId,
    deliveryOrderNo: DELIVERY_ORDER_NO,
    salesOrderNo: SALES_ORDER_NO,
    lines: [
      {
        salesOrderLineNo: '10',
        quantity: QUANTITY,
        locationCode: FINISHED_GOODS_LOCATION,
        lotNo: producedLotNo,
      },
    ],
    idempotencyKey: `issue1912-${DELIVERY_ORDER_NO}`,
  })
  const deliveryRow = await pollRows(
    '/api/business-console/v1/erp/sales/delivery-orders',
    { organizationId, environmentId, keyword: DELIVERY_ORDER_NO, skip: 0, take: 100 },
    (row) => textOf(row.deliveryOrderNo) === DELIVERY_ORDER_NO,
  )
  const deliveryUi = await provePageSafely('sales-order-delivery', {
    route: '/erp/sales/deliveries',
    listPath: '/api/business-console/v1/erp/sales/delivery-orders',
    filterLabel: '发货关键字',
    expectedListQuery: erpListQuery(),
    stableText: DELIVERY_ORDER_NO,
    emptyText: '还没有发货单',
    screenshotName: '16-sales-delivery.png',
  })
  record({
    node: 'sales-order-delivery',
    sourceObject: SALES_ORDER_NO,
    downstreamObject: DELIVERY_ORDER_NO,
    stableKey: `${SALES_ORDER_NO} -> ${DELIVERY_ORDER_NO}`,
    automationMode: 'manual',
    request: delivery.summary,
    responseOrLog: { delivery: publicJson(deliveryRow.match), ui: deliveryUi },
    conclusion: 'runtime-confirmed',
    demoWording:
      '成品库存有正数后，公开 ERP 发货释放从同一 SO 生成固定 DO，销售发货页面渲染稳定业务编号。',
    responsibilityIssue: null,
  })

  const shipmentScopes = await workerCall(
    'GET',
    queryPath('/api/business-console/v1/wms/work-scopes/shipments', {
      organizationId,
      environmentId,
    }),
  )
  const shipmentScope: AuthorizedWorkPoolScope | undefined = selectAuthorizedWorkPoolScope(
    shipmentScopes.payload,
    SITE_CODE,
  )
  const shipmentReadScope: AuthorizedWorkSiteScope | undefined = selectAuthorizedWorkSiteScope(
    shipmentScopes.payload,
    SITE_CODE,
  )
  if (!shipmentScope || !shipmentReadScope)
    throw new Error('WMS shipment scope catalog returned no authorized read or work-pool scope.')
  expect(textOf(asRecord(dataOf(shipmentScopes.payload)).actorPrincipalId)).toBe(
    workerRuntime.principalId,
  )
  const shipmentScopeKind = textOf(shipmentScope.scopeKind).trim()
  const shipmentScopeId = textOf(shipmentScope.scopeId).trim()
  const shipmentPoolCode = textOf(shipmentScope.poolCode).trim()
  const shipmentReadScopeKind = textOf(shipmentReadScope.scopeKind).trim()
  const shipmentReadScopeId = textOf(shipmentReadScope.scopeId).trim()
  const shipmentReadSiteCode = textOf(shipmentReadScope.siteCode).trim()
  expect(shipmentPoolCode).not.toBe('')
  expect(textOf(shipmentScope.siteCode)).toBe(SITE_CODE)
  expect(shipmentReadScopeKind.toLowerCase()).toBe('site')
  expect(shipmentReadScopeId).toBe(shipmentReadSiteCode)
  expect(shipmentReadSiteCode).toBe(SITE_CODE)
  setup.push({
    kind: 'wms-scope-catalog',
    actor: workerRuntime.actor,
    principalId: workerRuntime.principalId,
    operation: 'shipments',
    source: 'authorized WarehouseWorkScopeCatalogItem',
    scope: publicJson(shipmentScope),
    readScope: publicJson(shipmentReadScope),
    request: shipmentScopes.summary,
  })
  const outbound = await workerPollRows(
    '/api/business-console/v1/wms/outbound-orders',
    {
      organizationId,
      environmentId,
      keyword: DELIVERY_ORDER_NO,
      scopeKind: shipmentReadScopeKind,
      scopeId: shipmentReadScopeId,
      siteCode: shipmentReadSiteCode,
      skip: 0,
      take: 100,
    },
    (row) => textOf(row.outboundOrderNo) === DELIVERY_ORDER_NO,
    180_000,
  )
  const outboundId = textOf(outbound.match.outboundOrderId)
  const outboundVersion = Number(outbound.match.version ?? 1)
  const outboundAssignmentPlan = buildAuthorizedWorkPoolAssignment(
    {
      actor: workerRuntime.actor,
      principalId: workerRuntime.principalId,
    },
    shipmentScope,
    outboundId,
    `issue1912-${DELIVERY_ORDER_NO}-assignment`,
    outboundVersion,
  )
  if (!outboundAssignmentPlan.called) {
    throw new Error(
      `WMS outbound ${DELIVERY_ORDER_NO} cannot be assigned without an authorized work-pool scope: ${outboundAssignmentPlan.reason}`,
    )
  }
  const outboundAssignment = await workerCall(
    'POST',
    queryPath(
      `/api/business-console/v1/wms/outbound-orders/${encodeURIComponent(outboundAssignmentPlan.request.resourceId)}/assignment`,
      { organizationId, environmentId },
    ),
    outboundAssignmentPlan.request.body,
  )
  const outboundAssignmentData = asRecord(dataOf(outboundAssignment.payload))
  expect(textOf(outboundAssignmentData.resourceCategory)).toBe('outbound')
  expect(textOf(outboundAssignmentData.resourceId)).toBe(outboundId)
  expect(textOf(outboundAssignmentData.siteCode)).toBe(shipmentScope.siteCode)
  expect(textOf(outboundAssignmentData.poolCode)).toBe(shipmentPoolCode)
  expect(textOf(outboundAssignmentData.operatorPrincipalId)).toBe(workerRuntime.principalId)
  expect(textOf(outboundAssignmentData.assignedByPrincipalId)).toBe(workerRuntime.principalId)
  const assignedOutbound = await workerPollRows(
    '/api/business-console/v1/wms/outbound-orders',
    {
      organizationId,
      environmentId,
      keyword: DELIVERY_ORDER_NO,
      scopeKind: shipmentScopeKind,
      scopeId: shipmentScopeId,
      skip: 0,
      take: 100,
    },
    (row) =>
      textOf(row.outboundOrderNo) === DELIVERY_ORDER_NO &&
      textOf(row.assignedPoolCode) === shipmentPoolCode &&
      textOf(row.assignedOperatorUserId) === workerRuntime.principalId,
    180_000,
  )
  const assignedOutboundVersion = Number(assignedOutbound.match.version ?? 0)
  expect(assignedOutboundVersion).toBeGreaterThan(outboundVersion)
  setup.push({
    kind: 'wms-assignment',
    actor: workerRuntime.actor,
    principalId: workerRuntime.principalId,
    operation: 'outbound-order',
    resourceId: outboundId,
    scope: publicJson(shipmentScope),
    request: outboundAssignment.summary,
    response: outboundAssignment.publicPayload,
    bound: {
      poolCode: textOf(assignedOutbound.match.assignedPoolCode),
      operatorPrincipalId: textOf(assignedOutbound.match.assignedOperatorUserId),
      version: assignedOutboundVersion,
    },
  })
  const outboundQueryFacts = {
    organizationId,
    environmentId,
    scopeKind: shipmentScopeKind,
    scopeId: shipmentScopeId,
    keyword: DELIVERY_ORDER_NO,
    pageWindow: NERV_1571_WMS_DEFAULT_PAGE_WINDOW_INPUT,
  }
  const outboundKeywordQuery = buildWmsOutboundListQueryFacts(outboundQueryFacts)
  const outboundListQuery = buildWmsOutboundSelectionQueryFacts(outboundQueryFacts)
  const outboundUi = await proveWmsPageSafely('delivery-wms-outbound', {
    actor: 'wms-worker',
    route: '/wms/outbound',
    filterLabel: '关键字搜索',
    emptyText: '暂无出库单。发货作业产生出库单后会出现在这里。',
    screenshotName: '17-wms-outbound.png',
    wms: {
      kind: 'outbound',
      selection: {
        scope: {
          label: '作业范围',
          option: shipmentScope.displayName,
          scopeKind: outboundListQuery.scopeKind,
          scopeId: outboundListQuery.scopeId,
        },
      },
      pageWindow: outboundQueryFacts.pageWindow,
      query: {
        kind: 'outbound',
        listPath: '/api/business-console/v1/wms/outbound-orders',
        selectionQuery: outboundListQuery,
        keywordQuery: outboundKeywordQuery,
        forbiddenQueryKeys: ['siteCode'],
      },
    },
  })
  record({
    node: 'delivery-wms-outbound',
    sourceObject: DELIVERY_ORDER_NO,
    downstreamObject: textOf(assignedOutbound.match.outboundOrderId),
    stableKey: `${DELIVERY_ORDER_NO} -> ${textOf(assignedOutbound.match.outboundOrderNo)}`,
    automationMode: 'automatic',
    request: outbound.call.summary,
    responseOrLog: { outbound: publicJson(assignedOutbound.match), ui: outboundUi },
    conclusion: 'runtime-confirmed',
    demoWording:
      'ERP 发货释放跨 Redis 生成 WMS 出库单，授权作业池绑定后真实出库页面以 HTTP 200 渲染 DO-WALK-001。',
    responsibilityIssue: null,
  })
  const outboundLines = assignedOutbound.match.lines as JsonRecord[]
  expect(outboundLines).toHaveLength(1)
  const pickingLine = outboundLines[0]!
  expect(pickingLine.skuCode).toBe(FINISHED_SKU)
  expect(pickingLine.requestedQuantity).toBe(QUANTITY)
  const pickingScopeQuery = {
    organizationId,
    environmentId,
    scopeKind: shipmentScopeKind,
    scopeId: shipmentScopeId,
  }
  const picking = await executeWalkthroughPicking(
    {
      outboundOrderId: outboundId,
      intentKey: `PICK-${DELIVERY_ORDER_NO}`,
      lineNo: textOf(pickingLine.lineNo),
      fromLocationCode: textOf(pickingLine.locationCode),
      toLocationCode: FINISHED_GOODS_LOCATION,
      quantity: QUANTITY,
      scopeKind: shipmentScopeKind,
      scopeId: shipmentScopeId,
    },
    async (path, body) => {
      const response = await workerCall('POST', queryPath(path, pickingScopeQuery), body)
      setup.push({
        kind: 'wms-picking-execution',
        request: response.summary,
        response: response.publicPayload,
      })
      return asRecord(dataOf(response.payload))
    },
    async (warehouseTaskId) => {
      const task = await workerPollRows(
        '/api/business-console/v1/wms/picking-tasks',
        {
          ...pickingScopeQuery,
          keyword: textOf(outbound.match.outboundOrderNo),
          skip: 0,
          take: 100,
        },
        (row) => textOf(row.warehouseTaskId) === warehouseTaskId,
      )
      expect(task.match.assignedPoolCode).toBe(shipmentPoolCode)
      expect(task.match.assignedOperatorUserId).toBe(workerRuntime.principalId)
      return { version: Number(task.match.version) }
    },
  )
  expect(textOf(picking.status).toLowerCase()).toBe('completed')
  expect(picking.executedQuantity).toBe(QUANTITY)
  const pickedOutbound = await workerPollRows(
    '/api/business-console/v1/wms/outbound-orders',
    { ...pickingScopeQuery, keyword: DELIVERY_ORDER_NO, skip: 0, take: 100 },
    (row) =>
      textOf(row.outboundOrderId) === outboundId && Number(row.version) > assignedOutboundVersion,
  )
  const workerPage = workerRuntime.page
  await workerPage
    .getByRole('button', { name: `完成复核 ${DELIVERY_ORDER_NO}`, exact: true })
    .click()
  const review = workerPage.getByRole('dialog', { name: '出库复核' })
  await expect(review).toContainText(DELIVERY_ORDER_NO)
  await expect(review.getByRole('checkbox', { name: '复核通过' })).toBeChecked()
  const completionPath = `/api/business-console/v1/wms/outbound-orders/${encodeURIComponent(outboundId)}/complete`
  const [completedOutbound] = await Promise.all([
    workerPage.waitForResponse(
      (response) =>
        response.request().method() === 'POST' &&
        new URL(response.url()).pathname === completionPath,
    ),
    review.getByRole('button', { name: '提交复核' }).click(),
  ])
  expect(completedOutbound.ok()).toBe(true)
  const completionBody = completedOutbound.request().postDataJSON()
  expect(completionBody).toMatchObject({
    passed: true,
    scopeKind: shipmentScopeKind,
    scopeId: shipmentScopeId,
    expectedVersion: pickedOutbound.match.version,
  })
  const completedDelivery = await pollRows(
    '/api/business-console/v1/erp/sales/delivery-orders',
    { organizationId, environmentId, keyword: DELIVERY_ORDER_NO, skip: 0, take: 100 },
    (row) =>
      textOf(row.deliveryOrderNo) === DELIVERY_ORDER_NO &&
      textOf(row.status).toLowerCase() === 'completed',
    180_000,
  )
  record({
    node: 'wms-completed-erp-delivery',
    sourceObject: outboundId,
    downstreamObject: DELIVERY_ORDER_NO,
    stableKey: `${outboundId} -> ${DELIVERY_ORDER_NO} -> completed`,
    automationMode: 'mixed',
    request: {
      actor: workerRuntime.actor,
      principalId: workerRuntime.principalId,
      method: 'POST',
      path: completionPath,
      status: completedOutbound.status(),
      body: publicJson(completionBody),
      correlationId: completedOutbound.headers()['x-correlation-id'] ?? null,
    },
    responseOrLog: {
      picking: publicJson(picking),
      completedOutbound: publicJson(await completedOutbound.json()),
      delivery: publicJson(completedDelivery.match),
      ui: await provePageSafely('wms-completed-erp-delivery', {
        route: '/erp/sales/deliveries',
        listPath: '/api/business-console/v1/erp/sales/delivery-orders',
        filterLabel: '发货关键字',
        expectedListQuery: erpListQuery(),
        stableText: DELIVERY_ORDER_NO,
        emptyText: '还没有发货单',
        screenshotName: '17b-completed-sales-delivery.png',
      }),
    },
    conclusion: 'runtime-confirmed',
    demoWording: 'WMS 出库在真实作业范围内完成并以公开 ERP 读面证明对应 DO 已 completed。',
    responsibilityIssue: null,
  })
  const receivable = await pollRows(
    '/api/business-console/v1/erp/finance/receivables',
    { organizationId, environmentId, keyword: DELIVERY_ORDER_NO, skip: 0, take: 100 },
    (row) => textOf(row.sourceDocumentNo) === DELIVERY_ORDER_NO,
    180_000,
  )
  const receivableNo = textOf(receivable.match.receivableNo || receivable.match.accountReceivableNo)
  if (!receivableNo)
    throw new Error(`Delivery ${DELIVERY_ORDER_NO} produced no stable receivable number.`)
  const arUi = await provePageSafely('erp-account-receivable', {
    route: '/erp/finance/ar-ap',
    listPath: '/api/business-console/v1/erp/finance/receivables',
    filterLabel: '应收关键字',
    expectedListQuery: erpListQuery(),
    stableText: DELIVERY_ORDER_NO,
    emptyText: '还没有应收账款。销售出货或手工登记后会在这里形成应收。',
    screenshotName: '18-account-receivable.png',
  })
  record({
    node: 'erp-account-receivable',
    sourceObject: DELIVERY_ORDER_NO,
    downstreamObject: receivableNo,
    stableKey: `${DELIVERY_ORDER_NO} -> ${receivableNo}`,
    automationMode: 'automatic',
    request: receivable.call.summary,
    responseOrLog: { receivable: publicJson(receivable.match), ui: arUi },
    conclusion: 'runtime-confirmed',
    demoWording: '只有完成 WMS 出库后，ERP 应收读面以 HTTP 200 渲染由同一 DO 生成的稳定应收单号。',
    responsibilityIssue: null,
  })
}
