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

  it('starts the run-scoped MES operation task with the required business context query', () => {
    const productionFlow = sourceBetween("let productionReportId = ''", 'if (productionReportId) {')

    expect(productionFlow).not.toContain(
      '/mes/work-orders/${encodeURIComponent(workOrderId)}/release',
    )
    expectScopedQuery(productionFlow, '/mes/operation-tasks/${encodeURIComponent(taskId)}/start`')
    expect(productionFlow).toContain('idempotencyKey: `start-task-${suffix}`')
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
