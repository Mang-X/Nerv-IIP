import { readFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

const scenarioSource = readFileSync(
  resolve(dirname(fileURLToPath(import.meta.url)), '../e2e/leader-demo-main-chain.spec.ts'),
  'utf8',
)

function sourceBetween(start: string, end: string): string {
  const startIndex = scenarioSource.indexOf(start)
  const endIndex = scenarioSource.indexOf(end, startIndex)

  expect(startIndex, `scenario should contain ${start}`).toBeGreaterThanOrEqual(0)
  expect(endIndex, `scenario should contain ${end} after ${start}`).toBeGreaterThan(startIndex)
  return scenarioSource.slice(startIndex, endIndex)
}

function expectScopedQuery(source: string, endpoint: string): void {
  const endpointIndex = source.indexOf(endpoint)
  const queryStartIndex = source.lastIndexOf('queryPath(', endpointIndex)
  const nextQueryIndex = source.indexOf('queryPath(', endpointIndex + endpoint.length)

  expect(endpointIndex, `source should contain ${endpoint}`).toBeGreaterThanOrEqual(0)
  expect(queryStartIndex, `${endpoint} should be wrapped in queryPath`).toBeGreaterThanOrEqual(0)
  expect(
    source.slice(queryStartIndex, nextQueryIndex >= 0 ? nextQueryIndex : source.length),
  ).toMatch(/\{\s*organizationId,\s*environmentId\s*\}/)
}

describe('leader demo main-chain public prerequisites', () => {
  it('retries an idempotent create once after a server error and audits both attempts', () => {
    const createFlow = sourceBetween(
      'const create = async (path: string, body: JsonRecord) => {',
      'let prerequisitesReady = true',
    )
    const costRateFlow = sourceBetween('const workCenterCostRatePath =', 'const configuredRateId =')

    expect(createFlow).toContain('const idempotencyKey = textOf(body.idempotencyKey).trim()')
    expect(createFlow.match(/call\('POST', path, body\)/g)).toHaveLength(2)
    expect(createFlow).toContain(
      'if (!(error instanceof PublicCallError) || error.status < 500 || !idempotencyKey)',
    )
    expect(createFlow).toContain('await page.waitForTimeout(1_000)')
    expect(createFlow).toContain('attempt: 1')
    expect(createFlow).toContain("outcome: 'server-error'")
    expect(createFlow).toContain('request: error.request')
    expect(createFlow).toContain('status: error.status')
    expect(createFlow).toContain('payload: publicJson(error.payload)')
    expect(createFlow).toContain('attempt: 2')
    expect(createFlow).toContain("outcome: 'success'")
    expect(createFlow).toContain('request: replay.summary')
    expect(createFlow).toContain('response: replay.publicPayload')
    expect(createFlow).toContain('idempotencyKey')
    expect(costRateFlow).toContain(
      "const configuredRate = await call('POST', workCenterCostRatePath",
    )
    expect(costRateFlow).not.toContain('create(workCenterCostRatePath')
  })

  it('audits replay HTTP and non-HTTP failures before identity-preserving rethrow', () => {
    const createFlow = sourceBetween(
      'const create = async (path: string, body: JsonRecord) => {',
      'let prerequisitesReady = true',
    )

    expect(createFlow.match(/call\('POST', path, body\)/g)).toHaveLength(2)
    expect(createFlow).toContain('catch (replayError)')
    expect(createFlow).toContain('replayError instanceof PublicCallError')
    expect(createFlow).toContain(
      "outcome: replayError.status >= 500 ? 'server-error' : 'client-error'",
    )
    expect(createFlow).toContain('request: replayError.request')
    expect(createFlow).toContain('status: replayError.status')
    expect(createFlow).toContain('payload: publicJson(replayError.payload)')
    expect(createFlow).toContain("retry: { idempotencyKey, attempt: 2, outcome: 'non-http-error' }")
    expect(createFlow).toContain('request: null')
    expect(createFlow).toContain(
      'errorType: replayError instanceof Error ? replayError.name : typeof replayError',
    )
    expect(createFlow).toContain(
      'error: safeText(replayError instanceof Error ? replayError.message : replayError)',
    )
    expect(createFlow).toContain('throw replayError')
  })

  it('establishes raw material through public ERP, approval, WMS, and Inventory facts', () => {
    const supplyFlow = sourceBetween(
      "const approvalTemplateCode = 'erp-purchase-order-release'",
      'let salesOrderCreated = false',
    )

    expect(supplyFlow).not.toContain('/api/business-console/v1/inventory/movements')
    expect(supplyFlow).toContain('/api/business-console/v1/approval/templates')
    expect(supplyFlow).toContain('/api/business-console/v1/erp/procurement/purchase-orders')
    expect(supplyFlow).toContain('/api/business-console/v1/approval/chains/${encodeURIComponent')
    expect(supplyFlow).toContain('/api/business-console/v1/wms/inbound-orders')
    expect(supplyFlow).toContain('/putaway-tasks')
    expect(supplyFlow).toContain('/complete')
    expect(supplyFlow).toContain('/api/business-console/v1/inventory/availability')
    expect(supplyFlow).toContain("textOf(row.status).trim().toLowerCase() === 'released'")
    expect(supplyFlow).toContain('availableQuantity === rawMaterialQuantity')
    expect(supplyFlow).toContain('receivedQuantity === rawMaterialQuantity')
  })

  it('replays stable procurement and receiving requests without multiplying facts', () => {
    const supplyFlow = sourceBetween(
      "const approvalTemplateCode = 'erp-purchase-order-release'",
      'let salesOrderCreated = false',
    )

    expect(supplyFlow.match(/create\(purchaseOrderPath, purchaseOrderRequest\)/g)).toHaveLength(2)
    expect(supplyFlow.match(/create\(wmsInboundPath, wmsInboundRequest\)/g)).toHaveLength(2)
    expect(supplyFlow.match(/create\(putawayPath, putawayRequest\)/g)).toHaveLength(2)
    expect(supplyFlow.match(/create\(completeInboundPath, completeInboundRequest\)/g)).toHaveLength(
      2,
    )
    expect(supplyFlow).toContain('idempotencyKey: `purchase-order-${suffix}`')
    expect(supplyFlow).toContain('idempotencyKey: `complete-inbound-${suffix}`')
    expect(supplyFlow).toContain(
      'textOf(completedInboundReplay.requestId).trim() === completedInboundRequestId',
    )
    expect(supplyFlow).toContain(
      'WMS inbound completion replay did not return the original movement request.',
    )
    expect(supplyFlow).toContain('replayConfirmed: completeInboundReplayConfirmed')
  })

  it('observes exact run-scoped Inventory availability before accepting the MES work order', () => {
    const availabilityIndex = scenarioSource.indexOf(
      '/api/business-console/v1/inventory/availability',
    )
    const acceptIndex = scenarioSource.indexOf(
      '/planning/suggestions/${encodeURIComponent(textOf(suggestion.suggestionId))}/accept',
    )

    expect(availabilityIndex).toBeGreaterThanOrEqual(0)
    expect(acceptIndex).toBeGreaterThan(availabilityIndex)
    expect(scenarioSource).toContain('skuCode: materialSku')
    expect(scenarioSource).toContain("const materialSiteCode = 'production'")
    expect(scenarioSource).toContain('siteCode: materialSiteCode')
    expect(scenarioSource).toContain('lotNo: rawMaterialLotNo')
    expect(scenarioSource).toContain("qualityStatus: 'unrestricted'")
    expect(scenarioSource).toContain("ownerType: 'company'")
  })

  it('accepts the run-scoped MRP suggestion with the required business context query', () => {
    const acceptCall = sourceBetween('const accepted = await call(', 'workOrderId = textOf(')

    expectScopedQuery(
      acceptCall,
      '/planning/suggestions/${encodeURIComponent(textOf(suggestion.suggestionId))}/accept`',
    )
    expect(acceptCall).toContain('idempotencyKey: `accept-wo-${suffix}`')
  })

  it('records the accepted MES work order, then releases it before reading routing-derived tasks', () => {
    const acceptedWorkOrderFlow = sourceBetween(
      'workOrderId = textOf(asRecord(dataOf(accepted.payload)).downstreamDocumentId)',
      'let scheduleReleased = false',
    )
    const releaseIndex = acceptedWorkOrderFlow.indexOf(
      '/mes/work-orders/${encodeURIComponent(workOrderId)}/release',
    )
    const acceptedNodeIndex = acceptedWorkOrderFlow.indexOf("node: 'mrp-suggestion-mes-work-order'")
    const operationTaskIndex = acceptedWorkOrderFlow.indexOf('operationTask =')

    expect(releaseIndex).toBeGreaterThanOrEqual(0)
    expect(acceptedNodeIndex).toBeGreaterThanOrEqual(0)
    expect(releaseIndex).toBeGreaterThan(acceptedNodeIndex)
    expect(operationTaskIndex).toBeGreaterThan(releaseIndex)
    expectScopedQuery(
      acceptedWorkOrderFlow,
      '/mes/work-orders/${encodeURIComponent(workOrderId)}/release`',
    )
    expect(acceptedWorkOrderFlow).toContain('idempotencyKey: `release-wo-${suffix}`')
  })

  it('keeps the five-minute costing basis free of setup-time inflation', () => {
    const standardOperation = sourceBetween(
      "await create('/api/business-console/v1/engineering/standard-operations'",
      "await create('/api/business-console/v1/engineering/engineering-boms/release'",
    )

    expect(standardOperation).toContain('standardSetupMinutes: 0')
    expect(standardOperation).toContain('standardRunMinutes: operationDurationMinutes')
  })

  it('maps the run-scoped work center to a real device asset with fresh availability before scheduling', () => {
    const equipmentFlow = sourceBetween("let deviceAssetId = ''", "let productionReportId = ''")
    const registerIndex = equipmentFlow.indexOf(
      '/api/business-console/v1/master-data/device-assets',
    )
    const recordStateIndex = equipmentFlow.indexOf('/api/business-console/v1/telemetry/samples')
    const auditStateIndex = equipmentFlow.indexOf(
      '/api/business-console/v1/equipment/devices/${encodeURIComponent(deviceAssetId)}',
    )
    const deviceLookup = sourceBetween('const deviceList = await call(', 'const deviceResources =')

    expect(registerIndex).toBeGreaterThanOrEqual(0)
    expect(recordStateIndex).toBeGreaterThan(registerIndex)
    expect(auditStateIndex).toBeGreaterThan(recordStateIndex)
    expect(deviceLookup).toMatch(
      /queryPath\([\s\S]*?\/master-data\/device-assets[\s\S]*?\{\s*organizationId,\s*environmentId,\s*workCenterCode,\s*keyword:\s*deviceCode,/,
    )
    expect(equipmentFlow).toContain('deviceAssetId = textOf(device.deviceAssetId)')
    expect(equipmentFlow).toContain("deviceState: 'available'")
    expect(equipmentFlow).toContain(
      "textOf(currentState.currentState).trim().toLowerCase() !== 'available'",
    )

    const schedulingFlow = sourceBetween(
      'let scheduleReleased = false',
      "let productionReportId = ''",
    )
    expect(schedulingFlow).toContain('eligibleResourceIds: [deviceAssetId]')
    expect(schedulingFlow).toContain('primaryResourceId: deviceAssetId')
    expect(schedulingFlow).toContain('resourceId: deviceAssetId')
    expect(schedulingFlow).not.toContain('eligibleResourceIds: [workCenterCode]')
    expect(schedulingFlow).not.toContain('primaryResourceId: workCenterCode')
    expect(schedulingFlow).not.toContain('resourceId: workCenterCode')
    expect(schedulingFlow).toContain(
      "markFailure('mes-work-order-schedule-plan', error, 'manual', '#1040')",
    )
  })

  it('starts the run-scoped MES operation task with the required business context query', () => {
    const productionFlow = sourceBetween("let productionReportId = ''", 'if (productionReportId) {')

    expect(productionFlow).not.toContain(
      '/mes/work-orders/${encodeURIComponent(workOrderId)}/release',
    )
    expectScopedQuery(productionFlow, '/mes/operation-tasks/${encodeURIComponent(taskId)}/start`')
    expect(productionFlow).toContain('idempotencyKey: `start-task-${suffix}`')
    expect(productionFlow).toContain("queryPath('/api/business-console/v1/mes/work-orders'")
    expect(productionFlow).toContain('Number(costBasisWorkOrder.quantity ?? 0)')
    expect(productionFlow).toContain('durationTicks / ticksPerHour')
    expect(productionFlow).toContain('theoreticalRatePerHour !== expectedTheoreticalRatePerHour')
    expect(productionFlow).toContain('expectedLaborCost !== finishedGoodsCapitalizedCost')
  })

  it('configures and audits the run-scoped ERP work-center cost rate before production reporting', () => {
    const costFlow = sourceBetween('const workCenterCostRatePath =', "let productionReportId = ''")
    const configureIndex = scenarioSource.indexOf('const workCenterCostRatePath =')
    const workCenterIndex = scenarioSource.indexOf(
      "await create('/api/business-console/v1/master-data/work-centers'",
    )
    const productionReportIndex = scenarioSource.indexOf(
      "'/api/business-console/v1/mes/production-reports'",
    )

    expect(configureIndex).toBeGreaterThan(workCenterIndex)
    expect(productionReportIndex).toBeGreaterThan(configureIndex)
    expect(costFlow).toContain("'/api/business-console/v1/erp/finance/work-center-cost-rates'")
    expect(costFlow).toContain('hourlyRate: workCenterHourlyRate')
    expect(costFlow).toContain("currencyCode: 'CNY'")
    expect(costFlow).toContain('effectiveFromUtc: rateEffectiveFromUtc.toISOString()')
    expect(costFlow).toContain('effectiveToUtc: rateEffectiveToUtc.toISOString()')
    expect(costFlow).toContain('reason: workCenterCostRateReason')
    expect(costFlow).toContain('const rateAuditCall = await call(')
    expect(costFlow).toContain("'GET',")
    expect(costFlow).toContain('workCenterId: workCenterCode')
    expect(costFlow).toContain('atUtc: rateAuditAtUtc.toISOString()')
    expect(costFlow).toContain('rateAudit.currentEffectiveRevision === 1')
    expect(costFlow).toContain('textOf(currentRate.changedBy) === expectedRateActor')
    expect(costFlow).toContain('currentRate.isEffectiveAtUtc === true')
    expect(costFlow).toContain('currentRate.isCurrentEffectiveRevision === true')
    expect(costFlow).toContain("node: 'erp-work-center-cost-rate'")
  })

  it('polls exact finished-goods Inventory availability with a bounded public wait', () => {
    const receiptFlow = sourceBetween("let receiptRequestNo = ''", "let wmsOutboundId = ''")
    const finishedGoodsReceiptRequest = sourceBetween(
      'const receipt = await call(',
      'receiptRequestNo =',
    )
    const availabilityCall = sourceBetween(
      'const availability = await pollData(',
      "node: 'finished-goods-receipt-inventory-posting'",
    )

    expect(finishedGoodsReceiptRequest).not.toMatch(/\bunitCost\s*:/)
    expect(receiptFlow).toContain('const availability = await pollData(')
    expect(receiptFlow).toContain("'/api/business-console/v1/inventory/availability'")
    expect(receiptFlow).toContain('skuCode: finishedSku')
    expect(receiptFlow).toContain('siteCode: finishedGoodsSiteCode')
    expect(receiptFlow).toContain('locationCode: finishedGoodsLocationCode')
    expect(receiptFlow).toContain('lotNo: producedLotNo')
    expect(receiptFlow).toContain('(data) => Number(data.onHandQuantity ?? 0) > 0')
    expect(availabilityCall).toContain('120_000')
    expect(receiptFlow).toContain('poll: availability.poll')
    expect(receiptFlow).not.toMatch(/\(\s*\)\s*=>\s*false/)
    expect(receiptFlow).not.toMatch(/pollRows\([\s\S]*?producedLotNo[\s\S]*?,\s*1,?\s*\)/)
  })

  it('keeps the last public request, correlation, and response when bounded polling times out', () => {
    const pollingFlow = sourceBetween(
      'class PollTimeoutError extends Error',
      'const markFailure = (',
    )
    const failureFlow = sourceBetween('const markFailure = (', 'try {\n    await page.goto')

    expect(pollingFlow).toContain('readonly request: JsonRecord | null')
    expect(pollingFlow).toContain('readonly lastData: JsonRecord')
    expect(pollingFlow).toContain('readonly poll: JsonRecord')
    expect(pollingFlow).toContain('throw new PollTimeoutError(')
    expect(failureFlow).toContain('error instanceof PollTimeoutError')
    expect(failureFlow).toContain(
      'request: pollFailure?.request ?? callFailure?.request ?? current.request',
    )
    expect(failureFlow).toContain('lastData: publicJson(pollFailure.lastData)')
    expect(failureFlow).toContain('poll: pollFailure.poll')
  })

  it('preserves audit metadata for bounded row polling success and timeout', () => {
    const rowPollingFlow = sourceBetween('const pollRows = async (', 'const pollData = async (')

    expect(rowPollingFlow).toContain('const startedAt = Date.now()')
    expect(rowPollingFlow).toContain('let attempts = 0')
    expect(rowPollingFlow).toContain('let lastRequest: JsonRecord | null = null')
    expect(rowPollingFlow).toContain('attempts += 1')
    expect(rowPollingFlow).toContain('lastRequest = response.summary')
    expect(rowPollingFlow).toContain(
      'poll: { attempts, elapsedMs: Date.now() - startedAt, timeoutMs }',
    )
    expect(rowPollingFlow).toContain('throw new PollTimeoutError(')
    expect(rowPollingFlow).toContain('{ items: lastRows }')
  })

  it('retries a transient 404 within the polling budget and preserves its public evidence', () => {
    const pollingFlow = sourceBetween('const pollData = async (', 'const markFailure = (')
    const failureFlow = sourceBetween('const markFailure = (', 'try {\n    await page.goto')

    expect(pollingFlow).toContain('error instanceof PublicCallError && error.status === 404')
    expect(pollingFlow).toContain('lastRequest = error.request')
    expect(pollingFlow).toContain('lastData = asRecord(error.payload)')
    expect(failureFlow).toContain(
      'const callFailure = error instanceof PublicCallError ? error : null',
    )
    expect(failureFlow).toContain('callFailure?.request')
    expect(failureFlow).toContain('response: publicJson(callFailure.payload)')
  })

  it('keeps polling unknown Inventory link statuses and stops only on explicit terminal states', () => {
    const receiptFlow = sourceBetween("let receiptRequestNo = ''", "let wmsOutboundId = ''")

    expect(receiptFlow).toContain(
      "const terminalStatuses = new Set(['posted', 'postingfailed', 'qualityrestricted'])",
    )
    expect(receiptFlow).toContain('return terminalStatuses.has(status)')
    expect(receiptFlow).not.toContain("status !== 'notposted' && status !== 'partiallyposted'")
  })

  it('proves the receipt Inventory link through the real public facade and exact source keys', () => {
    const receiptFlow = sourceBetween("let receiptRequestNo = ''", "let wmsOutboundId = ''")
    const finalAcceptance = sourceBetween(
      'const unacceptableEntries = entries.filter(',
      "expect(entries.some((entry) => entry.conclusion === 'runtime-confirmed'))",
    )

    expect(receiptFlow).toContain('const inventoryLink = await pollData(')
    expect(receiptFlow).toContain(
      '/mes/finished-goods-receipt-requests/${encodeURIComponent(receiptRequestNo)}/inventory-link`',
    )
    const inventoryLinkCall = sourceBetween(
      'const inventoryLink = await pollData(',
      'const link = inventoryLink.data',
    )
    expect(inventoryLinkCall).toContain('organizationId')
    expect(inventoryLinkCall).toContain('environmentId')
    expect(inventoryLinkCall).toContain('workOrderId')
    expect(receiptFlow).toContain("textOf(link.linkStatus).trim().toLowerCase() === 'posted'")
    expect(receiptFlow).toContain('link.isInventoryLinkEstablished === true')
    expect(receiptFlow).toContain('textOf(link.requestNo) === receiptRequestNo')
    expect(receiptFlow).toContain('textOf(link.workOrderId) === workOrderId')
    expect(receiptFlow).toContain('textOf(link.producedLotNo) === producedLotNo')
    expect(receiptFlow).toContain("textOf(link.sourceService) === 'business-mes'")
    expect(receiptFlow).toContain('textOf(link.sourceDocumentId) === receiptRequestNo')
    expect(receiptFlow).toContain('textOf(link.sourceDocumentLineId) === workOrderId')
    expect(receiptFlow).toContain('const sourceMovement = movements.find(')
    expect(receiptFlow).toContain('const sourceBalance = balances.find(')
    expect(receiptFlow).toContain('const capitalizedReceipt = await pollRows(')
    expect(receiptFlow).toContain('Number(row.unitCost ?? 0) === finishedGoodsUnitCost')
    const capitalizedReceiptCall = sourceBetween(
      'const capitalizedReceipt = await pollRows(',
      'const terminalStatuses =',
    )
    expect(capitalizedReceiptCall).toContain('120_000')
    expect(receiptFlow).toContain('Number(movement.quantity ?? 0) === finishedGoodsQuantity')
    expect(receiptFlow).toContain('Number(balance.ledgerVersion ?? 0) > 0')
    expect(receiptFlow).toContain("node: 'inventory-produced-lot-fulfillment-lookup'")
    expect(receiptFlow).toContain('poll: inventoryLink.poll')
    const inventoryLinkEvidence = sourceBetween(
      "node: 'inventory-produced-lot-fulfillment-lookup'",
      "markFailure('inventory-produced-lot-fulfillment-lookup', error)",
    )
    expect(inventoryLinkEvidence).toContain("automationMode: 'automatic'")
    expect(inventoryLinkEvidence).toContain('responsibilityIssue: null')
    expect(inventoryLinkEvidence).toContain('capitalizedReceiptPoll: capitalizedReceipt.poll')
    expect(inventoryLinkEvidence).toContain('report labor accumulation')
    expect(inventoryLinkEvidence).toContain('ERP capitalization')
    expect(inventoryLinkEvidence).toContain('MES unit cost')
    expect(inventoryLinkEvidence).toContain('Inventory posting')
    expect(receiptFlow).not.toContain("responsibilityIssue: '#972 / MAN-528 (demo:defer)'")
    expect(finalAcceptance).toContain("entry.conclusion !== 'runtime-confirmed'")
    expect(finalAcceptance).not.toContain(
      "entry.node === 'inventory-produced-lot-fulfillment-lookup'",
    )
    expect(finalAcceptance).not.toContain('#972')
  })

  it('completes the run-scoped WMS outbound with the required business context query', () => {
    const completionFlow = sourceBetween(
      'const completed = await call(',
      "node: 'wms-completed-erp-delivery-status'",
    )

    expectScopedQuery(
      completionFlow,
      '/wms/outbound-orders/${encodeURIComponent(wmsOutboundId)}/complete`',
    )
    expect(completionFlow).toContain('packReviewNo: `PACK-${suffix}`')
    expect(completionFlow).toContain('idempotencyKey: `complete-outbound-${suffix}`')
    expect(completionFlow).toContain('{ organizationId, environmentId, keyword: deliveryOrderNo')
    expect(completionFlow).toContain(
      "row.deliveryOrderNo === deliveryOrderNo && row.status === 'completed'",
    )
  })
})
