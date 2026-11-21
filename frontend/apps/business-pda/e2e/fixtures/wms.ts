import { nowUtc, envelope, fulfillJson, confirmedOperation, listEnvelope } from './http'
import { principal, workerProfile } from './auth'
import { type Route } from '@playwright/test'

// Realistic WMS row shapes mirroring the real api-client item types — just enough
// fields for the PDA pages to render business codes + Chinese status (no raw codes/GUIDs).
const inboundOrders = [
  {
    inboundOrderId: 'in-1',
    inboundOrderNo: 'IN-1',
    status: 'open',
    qualityGateStatus: 'not-required',
    version: 1,
    createdAtUtc: nowUtc,
  },
  {
    inboundOrderId: 'in-2',
    inboundOrderNo: 'IN-2',
    status: 'open',
    version: 1,
    createdAtUtc: nowUtc,
  },
]

const outboundOrders = [
  { outboundOrderId: 'out-1', outboundOrderNo: 'OUT-1', status: 'pending', createdAtUtc: nowUtc },
  { outboundOrderId: 'out-2', outboundOrderNo: 'OUT-2', status: 'pending', createdAtUtc: nowUtc },
]

interface WarehouseTaskFixture {
  warehouseTaskId: string
  taskNo: string
  taskType: 'picking' | 'putaway'
  sourceOrderNo: string
  skuCode: string
  fromLocationCode: string
  toLocationCode: string
  plannedQuantity: number
  executedQuantity?: number
  uomCode?: string
  status: string
  version?: number
  assignedOperatorUserId?: string
  assignedPoolCode?: string
  siteCode?: string
  lotNo?: string | null
  allowedActions?: string[]
  blockReasons?: string[]
  createdAtUtc: string
}

const pickingTasks: WarehouseTaskFixture[] = [
  {
    warehouseTaskId: 'wt-pk-1',
    taskNo: 'PK-1',
    taskType: 'picking',
    sourceOrderNo: 'OUT-1',
    skuCode: 'SKU-1',
    fromLocationCode: 'A1',
    toLocationCode: 'B2',
    plannedQuantity: 10,
    status: 'Open',
    assignedOperatorUserId: principal.principalId,
    assignedPoolCode: 'POOL-001',
    siteCode: 'SITE-001',
    createdAtUtc: nowUtc,
  },
  {
    warehouseTaskId: 'wt-pk-exec-1',
    taskNo: 'PK-EXEC-1',
    taskType: 'picking',
    sourceOrderNo: 'OUT-2',
    skuCode: 'SKU-2',
    fromLocationCode: 'A2',
    toLocationCode: 'B3',
    plannedQuantity: 10,
    executedQuantity: 2,
    uomCode: 'EA',
    status: 'InProgress',
    version: 2,
    assignedOperatorUserId: principal.principalId,
    assignedPoolCode: 'POOL-001',
    siteCode: 'SITE-001',
    allowedActions: ['progress', 'exception', 'complete'],
    blockReasons: [],
    createdAtUtc: nowUtc,
  },
]

const putawayTasks: WarehouseTaskFixture[] = [
  {
    warehouseTaskId: 'wt-pa-1',
    taskNo: 'PA-1',
    taskType: 'putaway',
    sourceOrderNo: 'IN-1',
    skuCode: 'SKU-1',
    fromLocationCode: 'A1',
    toLocationCode: 'B2',
    plannedQuantity: 10,
    status: 'Open',
    assignedOperatorUserId: principal.principalId,
    assignedPoolCode: 'POOL-001',
    siteCode: 'SITE-001',
    createdAtUtc: nowUtc,
  },
  {
    warehouseTaskId: 'wt-pa-exec-1',
    taskNo: 'PA-EXEC-1',
    taskType: 'putaway',
    sourceOrderNo: 'IN-2',
    skuCode: 'SKU-2',
    fromLocationCode: 'STG-02',
    toLocationCode: 'A3',
    plannedQuantity: 10,
    executedQuantity: 2,
    uomCode: 'EA',
    status: 'InProgress',
    version: 2,
    assignedOperatorUserId: principal.principalId,
    assignedPoolCode: 'POOL-001',
    siteCode: 'SITE-001',
    allowedActions: ['progress', 'exception', 'complete'],
    blockReasons: [],
    createdAtUtc: nowUtc,
  },
]

// 收货单完整行投影（收货完成的 fail-closed 门禁依赖它：行数 0 → 判「不完整」禁止提交）。
const receivingQualityGates = [
  {
    inboundOrderId: 'in-1',
    inboundOrderLineId: 'in-1-l1',
    inboundOrderNo: 'IN-1',
    lineNo: '1',
    skuCode: 'SKU-1',
    uomCode: 'EA',
    receivedQuantity: 10,
    stagingLocationCode: 'STG-01',
    lotNo: null,
    qualityStatus: 'passed',
    qualityGateStatus: 'not-required',
  },
]

const countExecutions = [
  {
    countExecutionId: 'ce-1',
    countNo: 'CN-1',
    skuCode: 'SKU-1',
    locationCode: 'A1',
    expectedQuantity: 100,
    status: 'Open',
    version: 1,
    createdAtUtc: nowUtc,
  },
]

function warehouseTaskListEnvelope(requestUrl: URL, items: WarehouseTaskFixture[]) {
  let filtered = items
  const scopeKind = (requestUrl.searchParams.get('scopeKind') ?? '').trim()
  const scopeId = (requestUrl.searchParams.get('scopeId') ?? '').trim()
  if (!scopeKind || !scopeId) {
    filtered = []
  } else if (scopeKind === 'self') {
    filtered = filtered.filter((item) => item.assignedOperatorUserId === scopeId)
  } else if (scopeKind === 'work-pool') {
    filtered = filtered.filter((item) => item.assignedPoolCode === scopeId)
  } else if (scopeKind === 'site') {
    filtered = filtered.filter((item) => item.siteCode === scopeId)
  } else {
    filtered = []
  }

  const status = (requestUrl.searchParams.get('status') ?? '').trim().toLowerCase()
  if (status) filtered = filtered.filter((item) => item.status.toLowerCase() === status)

  const locationCode = (requestUrl.searchParams.get('locationCode') ?? '').trim()
  if (locationCode) {
    filtered = filtered.filter(
      (item) => item.fromLocationCode === locationCode || item.toLocationCode === locationCode,
    )
  }

  const lotNo = (requestUrl.searchParams.get('lotNo') ?? '').trim()
  if (lotNo) filtered = filtered.filter((item) => item.lotNo === lotNo)

  const keyword = (requestUrl.searchParams.get('keyword') ?? '').trim().toUpperCase()
  if (keyword) {
    filtered = filtered.filter((item) =>
      [item.taskNo, item.sourceOrderNo, item.skuCode].some((value) =>
        value.toUpperCase().includes(keyword),
      ),
    )
  }

  const skip = Math.max(0, Number(requestUrl.searchParams.get('skip') ?? 0) || 0)
  const requestedTake = Number(requestUrl.searchParams.get('take') ?? 100)
  const take =
    requestedTake <= 0 || !Number.isFinite(requestedTake)
      ? 100
      : Math.min(500, Math.floor(requestedTake))
  return envelope({ items: filtered.slice(skip, skip + take), total: filtered.length })
}
export function routeWms(route: Route, requestUrl: URL, pathname: string, isPost: boolean) {
  if (/\/wms\/work-scopes\/(receipts|shipments|counts)$/.test(pathname)) {
    return fulfillJson(
      route,
      envelope({
        actorPrincipalId: principal.principalId,
        items: [
          {
            scopeKind: 'self',
            scopeId: principal.principalId,
            displayName: workerProfile.displayName,
          },
          {
            scopeKind: 'work-pool',
            scopeId: 'POOL-001',
            displayName: '一号仓作业池',
          },
          { scopeKind: 'site', scopeId: 'SITE-001', displayName: '一号仓' },
        ],
      }),
    )
  }

  if (/\/wms\/operational-candidates\/(receipts|shipments|counts)$/.test(pathname)) {
    return fulfillJson(
      route,
      envelope({
        scopeKind: requestUrl.searchParams.get('scopeKind'),
        scopeId: requestUrl.searchParams.get('scopeId'),
        locations: [],
        lots: [],
        asOfUtc: nowUtc,
        freshnessUtc: nowUtc,
        truncated: false,
      }),
    )
  }

  // complete endpoints (POST .../{id}/complete) — match before the list paths.
  if (isPost && /\/wms\/inbound-orders\/[^/]+\/complete$/.test(pathname)) {
    const inboundOrderId = pathname.split('/').at(-2) ?? ''
    const body = route.request().postDataJSON() as { idempotencyKey: string }
    return fulfillJson(
      route,
      envelope({
        inboundOrderId,
        operationReceipt: confirmedOperation(
          'wms.inbound-order.complete',
          inboundOrderId,
          body.idempotencyKey,
          'completed',
        ),
      }),
    )
  }
  if (isPost && /\/wms\/outbound-orders\/[^/]+\/complete$/.test(pathname)) {
    return fulfillJson(route, envelope({}))
  }
  if (isPost && /\/wms\/count-executions\/[^/]+\/complete$/.test(pathname)) {
    const countExecutionId = pathname.split('/').at(-2) ?? ''
    const body = route.request().postDataJSON() as { idempotencyKey: string }
    return fulfillJson(
      route,
      envelope({
        countExecutionId,
        operationReceipt: confirmedOperation(
          'wms.count-execution.complete',
          countExecutionId,
          body.idempotencyKey,
          'completed',
        ),
      }),
    )
  }

  // list endpoints (GET).
  if (pathname.endsWith('/wms/inbound-orders')) {
    return fulfillJson(route, listEnvelope(inboundOrders))
  }
  if (pathname.endsWith('/wms/outbound-orders')) {
    return fulfillJson(route, listEnvelope(outboundOrders))
  }
  if (pathname.endsWith('/wms/picking-tasks')) {
    return fulfillJson(route, warehouseTaskListEnvelope(requestUrl, pickingTasks))
  }
  if (pathname.endsWith('/wms/putaway-tasks')) {
    return fulfillJson(route, warehouseTaskListEnvelope(requestUrl, putawayTasks))
  }
  if (pathname.endsWith('/wms/count-executions')) {
    return fulfillJson(route, listEnvelope(countExecutions))
  }
  if (pathname.endsWith('/wms/receiving-quality-gates')) {
    return fulfillJson(route, listEnvelope(receivingQualityGates))
  }
}
