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
  it('posts raw-material stock through the supported external inbound contract with stable business keys', () => {
    const movementCall = scenarioSource.match(
      /await create\('\/api\/business-console\/v1\/inventory\/movements',[\s\S]*?\n\s*\}\)/,
    )?.[0]

    expect(movementCall).toBeDefined()
    expect(movementCall).toContain("movementType: 'inbound'")
    expect(movementCall).toContain("sourceService: 'MAN-524-Acceptance'")
    expect(movementCall).toContain('sourceDocumentId: `RM-SEED-${suffix}`')
    expect(movementCall).toContain('idempotencyKey: `rm-stock-${suffix}`')
    expect(movementCall).toContain('skuCode: materialSku')
    expect(movementCall).toContain("locationCode: 'LINE-SIDE'")
    expect(movementCall).toContain('lotNo: `RMLOT-${suffix}`')
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
