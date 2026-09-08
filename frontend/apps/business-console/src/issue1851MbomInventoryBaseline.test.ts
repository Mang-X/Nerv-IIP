import { describe, expect, it } from 'vitest'

import {
  NERV1851_BASELINE,
  assertInventoryAvailabilityFact,
  buildMaterialBaselineFact,
  calculateRequiredQuantity,
  summarizeInventoryMovements,
  type InventoryAvailabilityFact,
  type InventoryMovementFact,
  type MbomMaterialLineFact,
} from './issue1851MbomInventoryBaseline'

const requirement: MbomMaterialLineFact = {
  skuCode: 'RM-SEL-01',
  quantity: 2,
  unitOfMeasureCode: 'pcs',
  scrapRate: 0.005,
  yieldRate: 1,
}

const availability: InventoryAvailabilityFact = {
  organizationId: 'org-001',
  environmentId: 'env-dev',
  skuCode: 'RM-SEL-01',
  uomCode: 'pcs',
  siteCode: 'SITE-001',
  onHandQuantity: 3,
  reservedQuantity: 1,
  availableQuantity: 2,
}

function inboundMovement(quantity: number, movementId: string): InventoryMovementFact {
  return {
    movementId,
    movementType: 'inbound',
    sourceService: 'wms',
    sourceDocumentId: `IN-${movementId}`,
    sourceDocumentLineId: '10',
    skuCode: 'RM-SEL-01',
    uomCode: 'pcs',
    siteCode: 'SITE-001',
    quantity,
    postedAtUtc: '2026-08-30T00:00:00Z',
  }
}

describe('NERV-1851 MBOM 与 Inventory 事实基线', () => {
  it('按独立生产数量、损耗率和良率计算需求，不从库存或 release 响应反推', () => {
    expect(calculateRequiredQuantity(requirement, 1)).toBe(2.01)
    expect(calculateRequiredQuantity({ ...requirement, yieldRate: 0.5 }, 1)).toBe(4.02)
    expect(calculateRequiredQuantity(requirement, 2)).toBe(4.02)
  })

  it('要求库存公共响应的可用量等于 on-hand 减 reserved', () => {
    expect(() => assertInventoryAvailabilityFact(availability)).not.toThrow()
    expect(() =>
      assertInventoryAvailabilityFact({ ...availability, availableQuantity: 3 }),
    ).toThrow(/availableQuantity/)
  })

  it('从独立入库流水读取 received/posted，而不是把 available 当成已收货', () => {
    const movements = [inboundMovement(1, 'MV-001'), inboundMovement(2, 'MV-002')]
    expect(summarizeInventoryMovements(movements)).toMatchObject({
      receivedQuantity: 3,
      postedQuantity: 3,
      receivedMovementCount: 2,
      postedMovementCount: 2,
      sourceDocumentIds: ['IN-MV-001', 'IN-MV-002'],
    })

    const fact = buildMaterialBaselineFact({
      context: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        siteCode: 'SITE-001',
      },
      requirement,
      productionQuantity: 1,
      availability: { ...availability, onHandQuantity: 1, availableQuantity: 0 },
      movements,
    })
    expect(fact.inventory.availableQuantity).toBe(0)
    expect(fact.inventory.receivedQuantity).toBe(3)
    expect(fact.inventory.postedQuantity).toBe(3)
    expect(fact.shortageQuantity).toBe(2.01)
  })

  it('把没有入库流水明确记录为后续供给事实缺口', () => {
    const movementSummary = summarizeInventoryMovements([])
    expect(movementSummary).toMatchObject({
      receivedQuantity: 0,
      postedQuantity: 0,
      receivedMovementCount: 0,
      postedMovementCount: 0,
    })
    expect(movementSummary.missingFacts).toEqual([
      '未发现正数入库接收量（Inventory movements inbound）',
      '未发现已过账入库量（Inventory movements postedAtUtc）',
    ])

    const fact = buildMaterialBaselineFact({
      context: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        siteCode: 'SITE-001',
      },
      requirement: { ...requirement, quantity: 1 },
      productionQuantity: 1,
      availability,
      movements: [],
    })
    expect(fact.state).toBe('sufficient')
    expect(fact.missingSupplyFacts).toEqual(movementSummary.missingFacts)
  })

  it('用工厂世界观的独立物料集合锁住本次基线范围', () => {
    expect(NERV1851_BASELINE.organizationId).toBe('org-001')
    expect(NERV1851_BASELINE.environmentId).toBe('env-dev')
    expect(NERV1851_BASELINE.finishedSkuCode).toBe('FG-QJ-P1-L')
    expect(NERV1851_BASELINE.revision).toBe('2')
    expect(NERV1851_BASELINE.expectedMaterialSkuCodes).toHaveLength(11)
    expect(NERV1851_BASELINE.expectedMaterialSkuCodes).toContain('PK-LBL-03')
    expect(NERV1851_BASELINE.expectedMaterialSkuCodes).toContain('RM-SPR-05')
  })
})
