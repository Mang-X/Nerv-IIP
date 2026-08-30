export const NERV1851_BASELINE = {
  issue: 'NERV-1851',
  organizationId: 'org-001',
  environmentId: 'env-dev',
  finishedSkuCode: 'FG-QJ-P1-L',
  manufacturingBomCode: 'MBOM-FG-QJ-P1-L',
  revision: '2',
  siteCode: 'SITE-001',
  productionQuantity: 1,
  // 独立于本次 HTTP 响应的工厂世界观物料集合（factory-world-bible §3/§4）。
  expectedMaterialSkuCodes: [
    'SF-ROD-01',
    'SF-TUB-01',
    'SF-VLV-01',
    'RM-SPR-05',
    'RM-SEL-01',
    'RM-OIL-01',
    'RM-ACC-01',
    'RM-ACC-04',
    'RM-ACC-07',
    'PK-BOX-01',
    'PK-LBL-03',
  ],
} as const

export type MbomMaterialLineFact = Readonly<{
  skuCode: string
  quantity: number
  unitOfMeasureCode: string
  scrapRate: number
  /** API contract defaults this optional field to 1 when no yield override exists. */
  yieldRate?: number | null
  isPhantom?: boolean
  alternateGroup?: string | null
  alternatePriority?: number | null
}>

export type InventoryAvailabilityFact = Readonly<{
  organizationId: string
  environmentId: string
  skuCode: string
  uomCode: string
  siteCode: string
  onHandQuantity: number
  reservedQuantity: number
  availableQuantity: number
}>

export type InventoryMovementFact = Readonly<{
  movementId: string
  movementType: string
  sourceService: string
  sourceDocumentId: string
  sourceDocumentLineId?: string | null
  skuCode: string
  uomCode: string
  siteCode: string
  quantity: number
  postedAtUtc: string | null
}>

export type InventoryMovementSummary = Readonly<{
  receivedQuantity: number
  postedQuantity: number
  receivedMovementCount: number
  postedMovementCount: number
  sourceDocumentIds: readonly string[]
  missingFacts: readonly string[]
}>

export type MaterialBaselineContext = Readonly<{
  organizationId: string
  environmentId: string
  siteCode: string
}>

export type MaterialBaselineFact = Readonly<{
  requirement: Readonly<{
    skuCode: string
    uomCode: string
    quantityPerUnit: number
    scrapRate: number
    yieldRate: number
    requiredQuantity: number
  }>
  inventory: InventoryAvailabilityFact & InventoryMovementSummary
  shortageQuantity: number
  state: 'shortage' | 'sufficient'
  missingSupplyFacts: readonly string[]
}>

function normalizedCode(value: string): string {
  return value.trim().toUpperCase()
}

function assertNonEmpty(name: string, value: string): void {
  if (!value.trim()) throw new Error(`${name} must not be empty`)
}

function assertFiniteNumber(name: string, value: number): void {
  if (!Number.isFinite(value)) throw new Error(`${name} must be finite`)
}

function roundedQuantity(value: number): number {
  return Number(value.toFixed(6))
}

/**
 * Inventory and MBOM quantities use the six-decimal precision of the public contracts.
 * Rounding here keeps the comparison deterministic across JSON number implementations.
 */
export function calculateRequiredQuantity(
  line: MbomMaterialLineFact,
  productionQuantity: number,
): number {
  assertNonEmpty('MBOM material skuCode', line.skuCode)
  assertNonEmpty('MBOM material unitOfMeasureCode', line.unitOfMeasureCode)
  assertFiniteNumber('MBOM production quantity', productionQuantity)
  assertFiniteNumber('MBOM material quantity', line.quantity)
  assertFiniteNumber('MBOM material scrapRate', line.scrapRate)
  if (productionQuantity <= 0) throw new Error('MBOM production quantity must be positive')
  if (line.quantity <= 0) throw new Error('MBOM material quantity must be positive')
  if (line.scrapRate < 0) throw new Error('MBOM material scrapRate must not be negative')

  // ManufacturingBomMaterialLine 的契约默认 YieldRate=1；只有显式覆盖才改变需求。
  const yieldRate = line.yieldRate ?? 1
  assertFiniteNumber('MBOM material yieldRate', yieldRate)
  if (yieldRate <= 0) throw new Error('MBOM material yieldRate must be positive')

  return roundedQuantity(
    (productionQuantity * line.quantity * (1 + line.scrapRate)) / yieldRate,
  )
}

/**
 * Selects concrete MBOM demand lines using the same business rule as MES: phantom lines are
 * excluded, and an alternate group contributes only its lowest-priority candidate.
 */
export function selectConcreteMaterialLines(
  lines: readonly MbomMaterialLineFact[],
): readonly MbomMaterialLineFact[] {
  const concrete = lines.filter((line) => line.isPhantom !== true)
  const standalone = concrete.filter((line) => !line.alternateGroup?.trim())
  const alternates = new Map<string, MbomMaterialLineFact[]>()
  for (const line of concrete) {
    const group = line.alternateGroup?.trim()
    if (!group) continue
    const existing = alternates.get(normalizedCode(group)) ?? []
    existing.push(line)
    alternates.set(normalizedCode(group), existing)
  }

  const selectedAlternates = [...alternates.values()].map((group) =>
    [...group].sort(
      (left, right) =>
        (left.alternatePriority ?? Number.MAX_SAFE_INTEGER) -
          (right.alternatePriority ?? Number.MAX_SAFE_INTEGER) ||
        normalizedCode(left.skuCode).localeCompare(normalizedCode(right.skuCode)),
    )[0],
  )
  return [...standalone, ...selectedAlternates]
}

export function assertExpectedMaterialSkuCodes(
  lines: readonly MbomMaterialLineFact[],
  expectedSkuCodes: readonly string[] = NERV1851_BASELINE.expectedMaterialSkuCodes,
): void {
  const actual = selectConcreteMaterialLines(lines).map((line) => normalizedCode(line.skuCode))
  const expected = expectedSkuCodes.map(normalizedCode)
  const actualSet = new Set(actual)
  const expectedSet = new Set(expected)
  const missing = expected.filter((sku) => !actualSet.has(sku))
  const unexpected = actual.filter((sku) => !expectedSet.has(sku))
  if (actual.length !== actualSet.size || actual.length !== expected.length || missing.length || unexpected.length) {
    throw new Error(
      `MBOM material set differs (missing=${missing.join(',') || 'none'}, unexpected=${unexpected.join(',') || 'none'}, actualCount=${actual.length}, expectedCount=${expected.length})`,
    )
  }
}

export function assertInventoryAvailabilityFact(
  fact: InventoryAvailabilityFact,
  expectedContext?: Readonly<Partial<Pick<InventoryAvailabilityFact, 'organizationId' | 'environmentId' | 'skuCode' | 'uomCode' | 'siteCode'>>>,
): void {
  assertNonEmpty('Inventory organizationId', fact.organizationId)
  assertNonEmpty('Inventory environmentId', fact.environmentId)
  assertNonEmpty('Inventory skuCode', fact.skuCode)
  assertNonEmpty('Inventory uomCode', fact.uomCode)
  assertNonEmpty('Inventory siteCode', fact.siteCode)
  assertFiniteNumber('Inventory onHandQuantity', fact.onHandQuantity)
  assertFiniteNumber('Inventory reservedQuantity', fact.reservedQuantity)
  assertFiniteNumber('Inventory availableQuantity', fact.availableQuantity)

  for (const [key, expected] of Object.entries(expectedContext ?? {})) {
    if (expected !== undefined && fact[key as keyof InventoryAvailabilityFact] !== expected) {
      throw new Error(
        `Inventory ${key} differs (expected=${String(expected)}, actual=${String(fact[key as keyof InventoryAvailabilityFact])})`,
      )
    }
  }

  const calculatedAvailable = roundedQuantity(fact.onHandQuantity - fact.reservedQuantity)
  if (calculatedAvailable !== roundedQuantity(fact.availableQuantity)) {
    throw new Error(
      `Inventory availableQuantity is inconsistent (expected=${calculatedAvailable}, actual=${fact.availableQuantity})`,
    )
  }
}

export function summarizeInventoryMovements(
  movements: readonly InventoryMovementFact[],
): InventoryMovementSummary {
  let receivedQuantity = 0
  let postedQuantity = 0
  let receivedMovementCount = 0
  let postedMovementCount = 0
  const sourceDocumentIds: string[] = []
  const seenSourceDocumentIds = new Set<string>()

  for (const movement of movements) {
    assertNonEmpty('Inventory movement movementId', movement.movementId)
    assertNonEmpty('Inventory movement movementType', movement.movementType)
    assertNonEmpty('Inventory movement sourceService', movement.sourceService)
    assertNonEmpty('Inventory movement sourceDocumentId', movement.sourceDocumentId)
    assertFiniteNumber('Inventory movement quantity', movement.quantity)
    if (movement.quantity < 0) throw new Error('Inventory movement quantity must not be negative')
    if (movement.movementType.trim().toLowerCase() !== 'inbound' || movement.quantity === 0) continue

    receivedQuantity += movement.quantity
    receivedMovementCount += 1
    if (!seenSourceDocumentIds.has(movement.sourceDocumentId)) {
      seenSourceDocumentIds.add(movement.sourceDocumentId)
      sourceDocumentIds.push(movement.sourceDocumentId)
    }

    if (movement.postedAtUtc && Number.isFinite(Date.parse(movement.postedAtUtc))) {
      postedQuantity += movement.quantity
      postedMovementCount += 1
    }
  }

  const missingFacts: string[] = []
  if (receivedQuantity === 0) {
    missingFacts.push('未发现正数入库接收量（Inventory movements inbound）')
  }
  if (postedQuantity === 0) {
    missingFacts.push('未发现已过账入库量（Inventory movements postedAtUtc）')
  }

  return {
    receivedQuantity: roundedQuantity(receivedQuantity),
    postedQuantity: roundedQuantity(postedQuantity),
    receivedMovementCount,
    postedMovementCount,
    sourceDocumentIds,
    missingFacts,
  }
}

function assertMovementScope(
  movement: InventoryMovementFact,
  expected: Readonly<Pick<InventoryAvailabilityFact, 'organizationId' | 'environmentId' | 'skuCode' | 'uomCode' | 'siteCode'>>,
): void {
  // Movement rows do not repeat tenant fields in the public contract; the query scope carries them.
  if (normalizedCode(movement.skuCode) !== normalizedCode(expected.skuCode)) {
    throw new Error(`Inventory movement skuCode differs (expected=${expected.skuCode}, actual=${movement.skuCode})`)
  }
  if (normalizedCode(movement.uomCode) !== normalizedCode(expected.uomCode)) {
    throw new Error(`Inventory movement uomCode differs (expected=${expected.uomCode}, actual=${movement.uomCode})`)
  }
  if (normalizedCode(movement.siteCode) !== normalizedCode(expected.siteCode)) {
    throw new Error(`Inventory movement siteCode differs (expected=${expected.siteCode}, actual=${movement.siteCode})`)
  }
}

export function buildMaterialBaselineFact(input: Readonly<{
  context: MaterialBaselineContext
  requirement: MbomMaterialLineFact
  productionQuantity: number
  availability: InventoryAvailabilityFact
  movements: readonly InventoryMovementFact[]
}>): MaterialBaselineFact {
  const expectedUom = input.requirement.unitOfMeasureCode
  assertInventoryAvailabilityFact(input.availability, {
    organizationId: input.context.organizationId,
    environmentId: input.context.environmentId,
    skuCode: input.requirement.skuCode,
    uomCode: expectedUom,
    siteCode: input.context.siteCode,
  })
  for (const movement of input.movements) {
    assertMovementScope(movement, {
      organizationId: input.context.organizationId,
      environmentId: input.context.environmentId,
      skuCode: input.requirement.skuCode,
      uomCode: expectedUom,
      siteCode: input.context.siteCode,
    })
  }

  const requiredQuantity = calculateRequiredQuantity(input.requirement, input.productionQuantity)
  const movementSummary = summarizeInventoryMovements(input.movements)
  const shortageQuantity = roundedQuantity(
    Math.max(requiredQuantity - input.availability.availableQuantity, 0),
  )
  return {
    requirement: {
      skuCode: input.requirement.skuCode,
      uomCode: expectedUom,
      quantityPerUnit: input.requirement.quantity,
      scrapRate: input.requirement.scrapRate,
      yieldRate: input.requirement.yieldRate ?? 1,
      requiredQuantity,
    },
    inventory: {
      ...input.availability,
      ...movementSummary,
    },
    shortageQuantity,
    state: shortageQuantity > 0 ? 'shortage' : 'sufficient',
    missingSupplyFacts: movementSummary.missingFacts,
  }
}
