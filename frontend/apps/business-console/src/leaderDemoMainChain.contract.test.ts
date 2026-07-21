import { readFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

const scenarioSource = readFileSync(
  resolve(dirname(fileURLToPath(import.meta.url)), '../e2e/leader-demo-main-chain.spec.ts'),
  'utf8',
)

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
})
