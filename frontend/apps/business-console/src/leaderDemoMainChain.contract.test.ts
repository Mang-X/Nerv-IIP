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

    expect(acceptCall).toContain('queryPath(')
    expect(acceptCall).toContain(
      '/planning/suggestions/${encodeURIComponent(textOf(suggestion.suggestionId))}/accept`',
    )
    expect(acceptCall).toContain('organizationId')
    expect(acceptCall).toContain('environmentId')
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
    expect(acceptedWorkOrderFlow).toContain('queryPath(')
    expect(acceptedWorkOrderFlow).toContain('organizationId')
    expect(acceptedWorkOrderFlow).toContain('environmentId')
    expect(acceptedWorkOrderFlow).toContain('idempotencyKey: `release-wo-${suffix}`')
  })

  it('starts the run-scoped MES operation task with the required business context query', () => {
    const productionFlow = sourceBetween("let productionReportId = ''", 'if (productionReportId) {')

    expect(productionFlow).not.toContain(
      '/mes/work-orders/${encodeURIComponent(workOrderId)}/release',
    )
    expect(productionFlow).toContain('queryPath(')
    expect(productionFlow).toContain('/mes/operation-tasks/${encodeURIComponent(taskId)}/start')
    expect(productionFlow).toContain('organizationId')
    expect(productionFlow).toContain('environmentId')
    expect(productionFlow).toContain('idempotencyKey: `start-task-${suffix}`')
  })

  it('completes the run-scoped WMS outbound with the required business context query', () => {
    const completeCall = sourceBetween(
      'const completed = await call(',
      'const delivery = await pollRows(',
    )

    expect(completeCall).toContain('queryPath(')
    expect(completeCall).toContain(
      '/wms/outbound-orders/${encodeURIComponent(wmsOutboundId)}/complete`',
    )
    expect(completeCall).toContain('organizationId')
    expect(completeCall).toContain('environmentId')
    expect(completeCall).toContain('packReviewNo: `PACK-${suffix}`')
    expect(completeCall).toContain('idempotencyKey: `complete-outbound-${suffix}`')
  })
})
