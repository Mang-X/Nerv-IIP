import {
  type WalkthroughContext,
  queryPath,
  SITE_CODE,
  INBOUND_INTENT_KEY,
  PURCHASE_ORDER_NO,
  INBOUND_LOCATION,
  LINE_SIDE_LOCATION,
} from './context'
import type { runProcurement } from './procurement'
import {
  type AuthorizedWorkPoolScope,
  selectAuthorizedWorkPoolScope,
  type AuthorizedWorkSiteScope,
  selectAuthorizedWorkSiteScope,
  buildAuthorizedWorkPoolAssignment,
} from '../issue1912-walkthrough-runtime'
import { expect } from '@playwright/test'
import {
  textOf,
  asRecord,
  dataOf,
  publicJson,
  inventoryStateFingerprint,
  inventoryMovementFingerprint,
} from '../issue1912-walkthrough-evidence'
import { NERV_1571_WMS_DEFAULT_PAGE_WINDOW_INPUT } from '../issue1912-wms-walkthrough-authority'
import {
  buildWmsInboundListQueryFacts,
  buildWmsInboundSelectionQueryFacts,
} from '../issue1912-wms-walkthrough-facts'

export async function runInbound(
  context: WalkthroughContext,
  procurement: Awaited<ReturnType<typeof runProcurement>>,
) {
  const { organizationId, environmentId } = context
  const { workerCall, workerRuntime, workerPollRows, workerCallExpecting, workerPollData } =
    context.session
  const { record, setup } = context.ledger
  const { proveWmsPageSafely } = context.proofs
  const { quoteLine, materialSku, materialUom, materialQuantity, purchaseReceiptNo } = procurement

  const receiptScopes = await workerCall(
    'GET',
    queryPath('/api/business-console/v1/wms/work-scopes/receipts', {
      organizationId,
      environmentId,
    }),
  )
  const receiptScope: AuthorizedWorkPoolScope | undefined = selectAuthorizedWorkPoolScope(
    receiptScopes.payload,
    SITE_CODE,
  )
  const receiptReadScope: AuthorizedWorkSiteScope | undefined = selectAuthorizedWorkSiteScope(
    receiptScopes.payload,
    SITE_CODE,
  )
  if (!receiptScope || !receiptReadScope)
    throw new Error('WMS receipt scope catalog returned no authorized read or work-pool scope.')
  expect(textOf(asRecord(dataOf(receiptScopes.payload)).actorPrincipalId)).toBe(
    workerRuntime.principalId,
  )
  const receiptScopeKind = textOf(receiptScope.scopeKind).trim()
  const receiptScopeId = textOf(receiptScope.scopeId).trim()
  const receiptPoolCode = textOf(receiptScope.poolCode).trim()
  const receiptReadScopeKind = textOf(receiptReadScope.scopeKind).trim()
  const receiptReadScopeId = textOf(receiptReadScope.scopeId).trim()
  const receiptReadSiteCode = textOf(receiptReadScope.siteCode).trim()
  expect(receiptPoolCode).not.toBe('')
  expect(textOf(receiptScope.siteCode)).toBe(SITE_CODE)
  expect(receiptReadScopeKind.toLowerCase()).toBe('site')
  expect(receiptReadScopeId).toBe(receiptReadSiteCode)
  expect(receiptReadSiteCode).toBe(SITE_CODE)
  setup.push({
    kind: 'wms-scope-catalog',
    actor: workerRuntime.actor,
    principalId: workerRuntime.principalId,
    operation: 'receipts',
    source: 'authorized WarehouseWorkScopeCatalogItem',
    scope: publicJson(receiptScope),
    readScope: publicJson(receiptReadScope),
    request: receiptScopes.summary,
  })
  const inbound = await workerCall('POST', '/api/business-console/v1/wms/inbound-orders', {
    organizationId,
    environmentId,
    idempotencyKey: INBOUND_INTENT_KEY,
    sourceDocumentType: 'purchase-order',
    sourceDocumentId: PURCHASE_ORDER_NO,
    siteCode: SITE_CODE,
    lines: [
      {
        lineNo: textOf(quoteLine.lineNo || '10'),
        skuCode: materialSku,
        uomCode: materialUom,
        receivedQuantity: materialQuantity,
        stagingLocationCode: INBOUND_LOCATION,
        lotNo: 'LOT-WALK-RM-001',
        serialNo: null,
        qualityStatus: 'unrestricted',
        ownerType: 'company',
        ownerId: null,
      },
    ],
  })
  const inboundOrderId = textOf(asRecord(dataOf(inbound.payload)).inboundOrderId)
  if (!inboundOrderId) throw new Error(`WMS inbound ${INBOUND_INTENT_KEY} did not return an ID.`)
  const inboundRow = await workerPollRows(
    '/api/business-console/v1/wms/inbound-orders',
    {
      organizationId,
      environmentId,
      inboundOrderId,
      scopeKind: receiptReadScopeKind,
      scopeId: receiptReadScopeId,
      siteCode: receiptReadSiteCode,
      skip: 0,
      take: 100,
    },
    (row) => textOf(row.inboundOrderId) === inboundOrderId,
  )
  const inboundOrderNo = textOf(inboundRow.match.inboundOrderNo)
  const inboundVersion = Number(inboundRow.match.version ?? 1)
  const noScopeInventoryQuery = {
    organizationId,
    environmentId,
    skuCode: materialSku,
    uomCode: materialUom,
    siteCode: SITE_CODE,
    locationCode: LINE_SIDE_LOCATION,
    lotNo: 'LOT-WALK-RM-001',
    qualityStatus: 'unrestricted',
    ownerType: 'company',
  }
  const inventoryBeforeNoScope = await workerCall(
    'GET',
    queryPath('/api/business-console/v1/inventory/availability', noScopeInventoryQuery),
  )
  const movementsBeforeNoScope = await workerCall(
    'GET',
    queryPath('/api/business-console/v1/inventory/movements', {
      organizationId,
      environmentId,
      sourceDocumentId: inboundOrderNo,
      page: 1,
      pageSize: 100,
    }),
  )
  const noScopeCompletion = await workerCallExpecting(
    'POST',
    queryPath(
      `/api/business-console/v1/wms/inbound-orders/${encodeURIComponent(inboundOrderId)}/complete`,
      { organizationId, environmentId },
    ),
    {
      idempotencyKey: `issue1912-${inboundOrderNo}-missing-scope`,
      lines: [{ lineNo: textOf(quoteLine.lineNo || '10'), lotNo: 'LOT-WALK-RM-001' }],
      expectedVersion: inboundVersion,
    },
    { code: 'missing-work-pool-assignment', message: 'missing-work-pool-assignment' },
  )
  const inboundAfterNoScope = await workerPollRows(
    '/api/business-console/v1/wms/inbound-orders',
    {
      organizationId,
      environmentId,
      keyword: inboundOrderNo,
      scopeKind: receiptReadScopeKind,
      scopeId: receiptReadScopeId,
      siteCode: receiptReadSiteCode,
      skip: 0,
      take: 100,
    },
    (row) => textOf(row.inboundOrderNo) === inboundOrderNo,
  )
  expect(Number(inboundAfterNoScope.match.version ?? 0)).toBe(inboundVersion)
  expect(textOf(inboundAfterNoScope.match.status).toLowerCase()).not.toBe('completed')
  const inventoryAfterNoScope = await workerCall(
    'GET',
    queryPath('/api/business-console/v1/inventory/availability', noScopeInventoryQuery),
  )
  const movementsAfterNoScope = await workerCall(
    'GET',
    queryPath('/api/business-console/v1/inventory/movements', {
      organizationId,
      environmentId,
      sourceDocumentId: inboundOrderNo,
      page: 1,
      pageSize: 100,
    }),
  )
  const inventoryBeforeNoScopeFingerprint = inventoryStateFingerprint(
    inventoryBeforeNoScope.payload,
  )
  const inventoryAfterNoScopeFingerprint = inventoryStateFingerprint(inventoryAfterNoScope.payload)
  const movementsBeforeNoScopeFingerprint = inventoryMovementFingerprint(
    movementsBeforeNoScope.payload,
  )
  const movementsAfterNoScopeFingerprint = inventoryMovementFingerprint(
    movementsAfterNoScope.payload,
  )
  expect(inventoryAfterNoScopeFingerprint).toEqual(inventoryBeforeNoScopeFingerprint)
  expect(movementsAfterNoScopeFingerprint).toEqual(movementsBeforeNoScopeFingerprint)
  const noScopeSideEffectProbe = {
    unchanged:
      JSON.stringify(inventoryAfterNoScopeFingerprint) ===
        JSON.stringify(inventoryBeforeNoScopeFingerprint) &&
      JSON.stringify(movementsAfterNoScopeFingerprint) ===
        JSON.stringify(movementsBeforeNoScopeFingerprint),
    inventoryAvailability: {
      path: inventoryBeforeNoScope.summary.path,
      before: inventoryBeforeNoScope.publicPayload,
      after: inventoryAfterNoScope.publicPayload,
      beforeFingerprint: inventoryBeforeNoScopeFingerprint,
      afterFingerprint: inventoryAfterNoScopeFingerprint,
    },
    inventoryMovements: {
      path: movementsBeforeNoScope.summary.path,
      before: movementsBeforeNoScope.publicPayload,
      after: movementsAfterNoScope.publicPayload,
      beforeFingerprint: movementsBeforeNoScopeFingerprint,
      afterFingerprint: movementsAfterNoScopeFingerprint,
    },
  }
  setup.push({
    kind: 'wms-no-scope-fail-closed',
    actor: workerRuntime.actor,
    principalId: workerRuntime.principalId,
    operation: 'inbound-complete',
    request: noScopeCompletion.summary,
    response: noScopeCompletion.publicPayload,
    publicError: noScopeCompletion.publicError,
    before: { version: inboundVersion, status: textOf(inboundRow.match.status) },
    after: {
      version: Number(inboundAfterNoScope.match.version ?? 0),
      status: textOf(inboundAfterNoScope.match.status),
    },
    sideEffect: false,
    sideEffectProbe: noScopeSideEffectProbe,
    scope: 'not-supplied',
  })
  const inboundAssignmentPlan = buildAuthorizedWorkPoolAssignment(
    {
      actor: workerRuntime.actor,
      principalId: workerRuntime.principalId,
    },
    receiptScope,
    inboundOrderId,
    `issue1912-${inboundOrderNo}-assignment`,
    inboundVersion,
  )
  if (!inboundAssignmentPlan.called) {
    throw new Error(
      `WMS inbound ${inboundOrderNo} cannot be assigned without an authorized work-pool scope: ${inboundAssignmentPlan.reason}`,
    )
  }
  const inboundAssignment = await workerCall(
    'POST',
    queryPath(
      `/api/business-console/v1/wms/inbound-orders/${encodeURIComponent(inboundAssignmentPlan.request.resourceId)}/assignment`,
      { organizationId, environmentId },
    ),
    inboundAssignmentPlan.request.body,
  )
  const inboundAssignmentData = asRecord(dataOf(inboundAssignment.payload))
  expect(textOf(inboundAssignmentData.resourceCategory)).toBe('inbound')
  expect(textOf(inboundAssignmentData.resourceId)).toBe(inboundOrderId)
  expect(textOf(inboundAssignmentData.siteCode)).toBe(receiptScope.siteCode)
  expect(textOf(inboundAssignmentData.poolCode)).toBe(receiptPoolCode)
  expect(textOf(inboundAssignmentData.operatorPrincipalId)).toBe(workerRuntime.principalId)
  expect(textOf(inboundAssignmentData.assignedByPrincipalId)).toBe(workerRuntime.principalId)
  const assignedInbound = await workerPollRows(
    '/api/business-console/v1/wms/inbound-orders',
    {
      organizationId,
      environmentId,
      keyword: inboundOrderNo,
      scopeKind: receiptScopeKind,
      scopeId: receiptScopeId,
      skip: 0,
      take: 100,
    },
    (row) =>
      textOf(row.inboundOrderNo) === inboundOrderNo &&
      textOf(row.assignedPoolCode) === receiptPoolCode &&
      textOf(row.assignedOperatorUserId) === workerRuntime.principalId,
  )
  const assignedInboundVersion = Number(assignedInbound.match.version ?? 0)
  expect(assignedInboundVersion).toBeGreaterThan(inboundVersion)
  setup.push({
    kind: 'wms-assignment',
    actor: workerRuntime.actor,
    principalId: workerRuntime.principalId,
    operation: 'inbound-order',
    resourceId: inboundOrderId,
    scope: publicJson(receiptScope),
    request: inboundAssignment.summary,
    response: inboundAssignment.publicPayload,
    bound: {
      poolCode: textOf(assignedInbound.match.assignedPoolCode),
      operatorPrincipalId: textOf(assignedInbound.match.assignedOperatorUserId),
      version: assignedInboundVersion,
    },
  })
  const putaway = await workerCall(
    'POST',
    queryPath(
      `/api/business-console/v1/wms/inbound-orders/${encodeURIComponent(inboundOrderId)}/putaway-tasks`,
      { organizationId, environmentId },
    ),
    {
      idempotencyKey: 'PUT-WALK-001',
      lineNo: textOf(quoteLine.lineNo || '10'),
      fromLocationCode: INBOUND_LOCATION,
      toLocationCode: LINE_SIDE_LOCATION,
      quantity: materialQuantity,
    },
  )
  const completedInbound = await workerCall(
    'POST',
    queryPath(
      `/api/business-console/v1/wms/inbound-orders/${encodeURIComponent(inboundOrderId)}/complete`,
      { organizationId, environmentId },
    ),
    {
      idempotencyKey: `issue1912-${inboundOrderNo}-complete`,
      lines: [{ lineNo: textOf(quoteLine.lineNo || '10'), lotNo: 'LOT-WALK-RM-001' }],
      scopeKind: receiptScopeKind,
      scopeId: receiptScopeId,
      expectedVersion: assignedInboundVersion,
    },
  )
  const inventory = await workerPollData(
    '/api/business-console/v1/inventory/availability',
    {
      organizationId,
      environmentId,
      skuCode: materialSku,
      uomCode: materialUom,
      siteCode: SITE_CODE,
      locationCode: LINE_SIDE_LOCATION,
      lotNo: 'LOT-WALK-RM-001',
      qualityStatus: 'unrestricted',
      ownerType: 'company',
    },
    (data) => Number(data.availableQuantity ?? data.onHandQuantity ?? 0) >= materialQuantity,
  )
  const movement = await workerPollRows(
    '/api/business-console/v1/inventory/movements',
    { organizationId, environmentId, sourceDocumentId: inboundOrderNo, page: 1, pageSize: 100 },
    (row) =>
      row.sourceService === 'wms' &&
      row.sourceDocumentId === inboundOrderNo &&
      row.movementType === 'inbound' &&
      row.skuCode === materialSku &&
      row.uomCode === materialUom &&
      row.siteCode === SITE_CODE &&
      row.locationCode === LINE_SIDE_LOCATION &&
      row.lotNo === 'LOT-WALK-RM-001' &&
      Number(row.quantity) === materialQuantity,
  )
  expect(textOf(movement.match.movementId)).toMatch(/\S/)
  const inboundQueryFacts = {
    organizationId,
    environmentId,
    scopeKind: receiptScopeKind,
    scopeId: receiptScopeId,
    siteCode: receiptReadSiteCode,
    keyword: inboundOrderNo,
    pageWindow: NERV_1571_WMS_DEFAULT_PAGE_WINDOW_INPUT,
  }
  const inboundKeywordQuery = buildWmsInboundListQueryFacts(inboundQueryFacts)
  const inboundListQuery = buildWmsInboundSelectionQueryFacts(inboundQueryFacts)
  const inboundUi = await proveWmsPageSafely('receipt-inbound-inventory', {
    actor: 'wms-worker',
    route: '/wms/inbound',
    filterLabel: '关键字搜索',
    emptyText: '暂无入库单。收货作业产生入库单后会出现在这里。',
    screenshotName: '07-wms-inbound.png',
    wms: {
      kind: 'inbound',
      selection: {
        scope: {
          label: '作业范围',
          option: receiptScope.displayName,
          scopeKind: inboundListQuery.scopeKind,
          scopeId: inboundListQuery.scopeId,
        },
        site: { label: '工厂', optionCode: inboundListQuery.siteCode },
      },
      pageWindow: inboundQueryFacts.pageWindow,
      query: {
        kind: 'inbound',
        listPath: '/api/business-console/v1/wms/inbound-orders',
        selectionQuery: inboundListQuery,
        keywordQuery: inboundKeywordQuery,
        forbiddenQueryKeys: [],
      },
    },
  })
  record({
    node: 'receipt-inbound-inventory',
    sourceObject: purchaseReceiptNo,
    downstreamObject: inboundOrderNo,
    stableKey: `${purchaseReceiptNo} -> ${inboundOrderNo} -> ${textOf(movement.match.movementId)}`,
    automationMode: 'mixed',
    request: inbound.summary,
    responseOrLog: {
      inbound: inbound.publicPayload,
      putaway: putaway.publicPayload,
      completion: completedInbound.publicPayload,
      movement: publicJson(movement.match),
      inventory: publicJson(inventory.data),
      ui: inboundUi,
    },
    conclusion: 'runtime-confirmed',
    demoWording: `收货单 ${purchaseReceiptNo} 和入库单 ${inboundOrderNo} 对应采购单 ${PURCHASE_ORDER_NO}。仓储页面显示该入库单，库存流水按入库单号回读，批次 LOT-WALK-RM-001 在 ${SITE_CODE}/${LINE_SIDE_LOCATION} 可用。`,
    responsibilityIssue: null,
  })
}
