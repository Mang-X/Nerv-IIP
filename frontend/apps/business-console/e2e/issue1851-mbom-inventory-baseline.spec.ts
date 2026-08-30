import { expect, test, type APIResponse, type Page } from '@playwright/test'
import { mkdir, writeFile } from 'node:fs/promises'
import path from 'node:path'

import {
  assertExpectedMaterialSkuCodes,
  buildMaterialBaselineFact,
  NERV1851_BASELINE,
  selectConcreteMaterialLines,
  type InventoryAvailabilityFact,
  type InventoryMovementFact,
  type MbomMaterialLineFact,
} from '../src/issue1851MbomInventoryBaseline'

const baseURL = process.env.NERV_IIP_PLAYWRIGHT_BASE_URL
const adminPassword = process.env.NERV_IIP_FULLSTACK_ADMIN_PASSWORD
const evidencePath = process.env.NERV_IIP_NERV1851_EVIDENCE_PATH
const runtimeProfileSource = process.env.NERV_IIP_NERV1851_RUNTIME_PROFILE_SOURCE
const transport = process.env.NERV_IIP_NERV1851_TRANSPORT
const persistence = process.env.NERV_IIP_NERV1851_PERSISTENCE

test.skip(
  !baseURL ||
    !adminPassword ||
    !evidencePath ||
    !runtimeProfileSource ||
    !transport ||
    !persistence,
  'requires a managed FullStack session and NERV-1851 evidence metadata',
)
test.setTimeout(12 * 60 * 1000)

type JsonRecord = Record<string, unknown>

class PublicCallError extends Error {
  constructor(
    readonly method: 'GET',
    readonly path: string,
    readonly status: number,
    readonly request: JsonRecord,
    readonly payload: unknown,
  ) {
    super(`${method} ${path} returned HTTP ${status}: ${safeText(JSON.stringify(payload))}`)
    this.name = 'PublicCallError'
  }
}

function asRecord(value: unknown): JsonRecord {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
    ? (value as JsonRecord)
    : {}
}

function dataOf(value: unknown): unknown {
  return asRecord(value).data ?? value
}

function rowsOf(value: unknown): JsonRecord[] {
  const data = dataOf(value)
  if (Array.isArray(data)) return data.map(asRecord)
  const items = asRecord(data).items
  return Array.isArray(items) ? items.map(asRecord) : []
}

function textOf(value: unknown): string {
  if (value === null || value === undefined) return ''
  return String(value)
}

function requiredText(record: JsonRecord, field: string, source: string): string {
  const value = textOf(record[field]).trim()
  if (!value) throw new Error(`${source} did not expose required ${field}.`)
  return value
}

function requiredNumber(record: JsonRecord, field: string, source: string): number {
  const raw = record[field]
  if (raw === null || raw === undefined || raw === '') {
    throw new Error(`${source} did not expose required numeric ${field}.`)
  }
  const value = Number(raw)
  if (!Number.isFinite(value)) throw new Error(`${source} exposed non-finite ${field}.`)
  return value
}

function optionalNumber(record: JsonRecord, field: string, source: string): number | null {
  const raw = record[field]
  if (raw === null || raw === undefined || raw === '') return null
  const value = Number(raw)
  if (!Number.isFinite(value)) throw new Error(`${source} exposed non-finite ${field}.`)
  return value
}

function safeText(value: unknown): string {
  return textOf(value)
    .replace(/authorization/gi, '<redacted-header>')
    .replace(/bearer\s+[^\s"']+/gi, '<redacted-credential>')
    .replace(/password/gi, '<redacted-field>')
    .replace(/(?:access|refresh)[_-]?token/gi, '<redacted-field>')
    .slice(0, 1600)
}

function publicJson(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(publicJson)
  if (value === null || typeof value !== 'object') {
    return typeof value === 'string' ? safeText(value) : value
  }
  return Object.fromEntries(
    Object.entries(value as JsonRecord)
      .filter(([key]) => !/(authorization|password|access[_-]?token|refresh[_-]?token)/i.test(key))
      .map(([key, item]) => [key, publicJson(item)]),
  )
}

async function jsonOf(response: APIResponse): Promise<unknown> {
  const contentType = response.headers()['content-type'] ?? ''
  if (!contentType.includes('json')) return { text: safeText(await response.text()) }
  return response.json()
}

function queryPath(pathname: string, query: JsonRecord): string {
  const url = new URL(pathname, baseURL!)
  for (const [key, value] of Object.entries(query)) {
    if (value !== null && value !== undefined && value !== '') {
      url.searchParams.set(key, String(value))
    }
  }
  return `${url.pathname}${url.search}`
}

function parseMbomLine(record: JsonRecord, index: number): MbomMaterialLineFact {
  const source = `MBOM material line ${index + 1}`
  return {
    skuCode: requiredText(record, 'skuCode', source),
    quantity: requiredNumber(record, 'quantity', source),
    unitOfMeasureCode: requiredText(record, 'unitOfMeasureCode', source),
    scrapRate: requiredNumber(record, 'scrapRate', source),
    // YieldRate is optional in the public contract and defaults to one by contract.
    yieldRate: optionalNumber(record, 'yieldRate', source) ?? 1,
    isPhantom: record.isPhantom === true,
    alternateGroup: textOf(record.alternateGroup).trim() || null,
    alternatePriority: optionalNumber(record, 'alternatePriority', source),
  }
}

function parseMbomLines(record: JsonRecord, source: string): MbomMaterialLineFact[] {
  const rawLines = record.materialLines
  if (!Array.isArray(rawLines)) throw new Error(`${source} did not expose materialLines.`)
  return rawLines.map((line, index) => parseMbomLine(asRecord(line), index))
}

function materialLineFingerprint(lines: readonly MbomMaterialLineFact[]): string {
  return JSON.stringify(
    selectConcreteMaterialLines(lines)
      .map((line) => ({
        skuCode: line.skuCode,
        quantity: line.quantity,
        unitOfMeasureCode: line.unitOfMeasureCode,
        scrapRate: line.scrapRate,
        yieldRate: line.yieldRate ?? 1,
        isPhantom: line.isPhantom === true,
        alternateGroup: line.alternateGroup ?? null,
        alternatePriority: line.alternatePriority ?? null,
      }))
      .sort((left, right) => left.skuCode.localeCompare(right.skuCode)),
  )
}

function parseAvailability(record: JsonRecord, source: string): InventoryAvailabilityFact {
  return {
    organizationId: requiredText(record, 'organizationId', source),
    environmentId: requiredText(record, 'environmentId', source),
    skuCode: requiredText(record, 'skuCode', source),
    uomCode: requiredText(record, 'uomCode', source),
    siteCode: requiredText(record, 'siteCode', source),
    onHandQuantity: requiredNumber(record, 'onHandQuantity', source),
    reservedQuantity: requiredNumber(record, 'reservedQuantity', source),
    availableQuantity: requiredNumber(record, 'availableQuantity', source),
  }
}

function parseMovement(record: JsonRecord, index: number): InventoryMovementFact {
  const source = `Inventory movement ${index + 1}`
  return {
    movementId: requiredText(record, 'movementId', source),
    movementType: requiredText(record, 'movementType', source),
    sourceService: requiredText(record, 'sourceService', source),
    sourceDocumentId: requiredText(record, 'sourceDocumentId', source),
    sourceDocumentLineId: textOf(record.sourceDocumentLineId).trim() || null,
    skuCode: requiredText(record, 'skuCode', source),
    uomCode: requiredText(record, 'uomCode', source),
    siteCode: requiredText(record, 'siteCode', source),
    quantity: requiredNumber(record, 'quantity', source),
    postedAtUtc: textOf(record.postedAtUtc).trim() || null,
  }
}

async function captureSessionCredential(page: Page): Promise<string> {
  const businessRequest = page.waitForRequest(
    (request) => {
      const pathname = new URL(request.url()).pathname
      return (
        pathname === '/api/business-console/v1/master-data/skus' &&
        Boolean(request.headers().authorization)
      )
    },
    { timeout: 120_000 },
  )
  await page.goto('/master-data/skus', { waitUntil: 'domcontentloaded', timeout: 120_000 })
  const credential = (await businessRequest).headers().authorization
  if (!credential)
    throw new Error('Authenticated public business request had no bearer credential.')
  return credential
}

test('NERV-1851 独立读取 MBOM 与 Inventory 真实缺料事实', async ({ page }) => {
  test.skip(
    test.info().project.name !== 'desktop',
    'NERV-1851 evidence is collected once from the managed Desktop Chrome project',
  )
  const generatedAtUtc = new Date().toISOString()
  const calls: JsonRecord[] = []
  const uiPages: JsonRecord[] = []
  const report: JsonRecord = {
    schemaVersion: 1,
    issue: NERV1851_BASELINE.issue,
    generatedAtUtc,
    conclusion: 'not-verified',
    businessState: 'not-verified',
    scope: {
      organizationId: NERV1851_BASELINE.organizationId,
      environmentId: NERV1851_BASELINE.environmentId,
      siteCode: NERV1851_BASELINE.siteCode,
      finishedSkuCode: NERV1851_BASELINE.finishedSkuCode,
      manufacturingBomCode: NERV1851_BASELINE.manufacturingBomCode,
      revision: NERV1851_BASELINE.revision,
      productionQuantity: NERV1851_BASELINE.productionQuantity,
    },
    runtime: {
      baseURL: new URL(baseURL!).origin,
      browserProject: test.info().project.name,
      runtimeProfileSource,
      transport,
      persistence,
      fullStackStateRoot: process.env.NERV_IIP_FULLSTACK_STATE_ROOT ?? null,
    },
    uiPages,
    calls,
    materials: [],
    missingSupplyFacts: [],
    mutationsAttempted: false,
    cleanup: {
      delegatedToManagedFullStack: Boolean(process.env.NERV_IIP_FULLSTACK_STATE_ROOT),
      cleanupEvidencePath: process.env.NERV_IIP_NERV1851_CLEANUP_EVIDENCE_PATH ?? null,
    },
  }

  let sessionCredential = ''
  const call = async (pathname: string) => {
    const url = new URL(pathname, baseURL!).toString()
    const response = await page.request.fetch(url, {
      method: 'GET',
      headers: { authorization: sessionCredential },
    })
    const payload = await jsonOf(response)
    const summary = {
      method: 'GET',
      path: new URL(url).pathname + new URL(url).search,
      status: response.status(),
      correlationId:
        response.headers()['x-correlation-id'] ?? response.headers().traceparent ?? null,
    }
    const evidence = { request: summary, response: publicJson(payload) }
    calls.push(evidence)
    if (!response.ok()) {
      throw new PublicCallError(
        'GET',
        summary.path,
        response.status(),
        summary,
        publicJson(payload),
      )
    }
    return { payload, summary, publicPayload: publicJson(payload) }
  }

  try {
    await page.goto('/login', { waitUntil: 'domcontentloaded', timeout: 120_000 })
    const loginName = page.getByLabel('登录名')
    await expect(loginName).toBeVisible({ timeout: 120_000 })
    const loginResponse = page.waitForResponse(
      (response) => new URL(response.url()).pathname === '/api/console/v1/auth/login',
      { timeout: 120_000 },
    )
    await loginName.fill('admin')
    await page.getByLabel('密码').fill(adminPassword!)
    await page.getByRole('button', { name: '登录' }).click()
    const login = await loginResponse
    expect(login.ok()).toBe(true)
    const principal = asRecord(asRecord(dataOf(await login.json())).principal)
    const organizationId = requiredText(principal, 'organizationId', 'login principal')
    const environmentId = requiredText(principal, 'environmentId', 'login principal')
    expect(organizationId).toBe(NERV1851_BASELINE.organizationId)
    expect(environmentId).toBe(NERV1851_BASELINE.environmentId)
    await expect(page).toHaveURL(new URL('/', baseURL!).toString())
    sessionCredential = await captureSessionCredential(page)
    report.runtime.userAgent = await page.evaluate(() => navigator.userAgent)

    for (const route of ['/engineering/mbom', '/inventory/availability'] as const) {
      const response = await page.goto(route, {
        waitUntil: 'domcontentloaded',
        timeout: 120_000,
      })
      const pageEvidence = { route, status: response?.status() ?? null, url: page.url() }
      uiPages.push(pageEvidence)
      expect(response?.ok(), `real Chromium page ${route} should return HTTP 2xx`).toBe(true)
    }

    const listCall = await call(
      queryPath('/api/business-console/v1/engineering/manufacturing-boms', {
        organizationId,
        environmentId,
        skuCode: NERV1851_BASELINE.finishedSkuCode,
        status: 'Published',
        skip: 0,
        take: 100,
      }),
    )
    const listRows = rowsOf(listCall.payload)
    const listMatch = listRows.find(
      (row) =>
        textOf(row.bomCode).trim() === NERV1851_BASELINE.manufacturingBomCode &&
        textOf(row.revision).trim() === NERV1851_BASELINE.revision,
    )
    if (!listMatch) {
      throw new Error(
        `Published MBOM ${NERV1851_BASELINE.manufacturingBomCode}:${NERV1851_BASELINE.revision} was not returned by the public list.`,
      )
    }
    const listLines = parseMbomLines(listMatch, 'MBOM list response')
    assertExpectedMaterialSkuCodes(listLines)

    const detailCall = await call(
      queryPath(
        `/api/business-console/v1/engineering/manufacturing-boms/${encodeURIComponent(NERV1851_BASELINE.manufacturingBomCode)}/${encodeURIComponent(NERV1851_BASELINE.revision)}`,
        { organizationId, environmentId },
      ),
    )
    const detail = asRecord(dataOf(detailCall.payload))
    if (
      requiredText(detail, 'bomCode', 'MBOM detail response') !==
        NERV1851_BASELINE.manufacturingBomCode ||
      requiredText(detail, 'revision', 'MBOM detail response') !== NERV1851_BASELINE.revision ||
      requiredText(detail, 'skuCode', 'MBOM detail response') !==
        NERV1851_BASELINE.finishedSkuCode ||
      requiredText(detail, 'status', 'MBOM detail response').toLowerCase() !== 'published'
    ) {
      throw new Error(
        'MBOM detail response did not preserve the expected published business identity.',
      )
    }
    const detailLines = parseMbomLines(detail, 'MBOM detail response')
    assertExpectedMaterialSkuCodes(detailLines)
    if (materialLineFingerprint(listLines) !== materialLineFingerprint(detailLines)) {
      throw new Error(
        'MBOM list and detail public reads disagree on concrete material requirements.',
      )
    }

    const context = {
      organizationId,
      environmentId,
      siteCode: NERV1851_BASELINE.siteCode,
    }
    const materials: JsonRecord[] = []
    for (const requirement of selectConcreteMaterialLines(detailLines)) {
      const availabilityCall = await call(
        queryPath('/api/business-console/v1/inventory/availability', {
          ...context,
          skuCode: requirement.skuCode,
          uomCode: requirement.unitOfMeasureCode,
        }),
      )
      const availability = parseAvailability(
        asRecord(dataOf(availabilityCall.payload)),
        `Inventory availability ${requirement.skuCode}`,
      )

      const movementPages: JsonRecord[] = []
      const movements: InventoryMovementFact[] = []
      let pageNumber = 1
      let totalCount = 0
      do {
        const movementCall = await call(
          queryPath('/api/business-console/v1/inventory/movements', {
            ...context,
            skuCode: requirement.skuCode,
            movementType: 'inbound',
            page: pageNumber,
            pageSize: 100,
          }),
        )
        const movementData = asRecord(dataOf(movementCall.payload))
        const pageRows = rowsOf(movementCall.payload)
        totalCount =
          movementData.totalCount === null || movementData.totalCount === undefined
            ? pageRows.length
            : requiredNumber(
                movementData,
                'totalCount',
                `Inventory movements ${requirement.skuCode}`,
              )
        movements.push(
          ...pageRows
            .map(parseMovement)
            .filter(
              (movement) =>
                movement.uomCode.trim().toUpperCase() ===
                requirement.unitOfMeasureCode.trim().toUpperCase(),
            ),
        )
        movementPages.push({ request: movementCall.summary, response: movementCall.publicPayload })
        if (pageRows.length === 0 || movements.length >= totalCount) break
        pageNumber += 1
        if (pageNumber > 100) {
          throw new Error(
            `Inventory movements ${requirement.skuCode} exceeded the bounded page window.`,
          )
        }
      } while (true)

      const baselineFact = buildMaterialBaselineFact({
        context,
        requirement,
        productionQuantity: NERV1851_BASELINE.productionQuantity,
        availability,
        movements,
      })
      materials.push({
        ...baselineFact,
        sources: {
          mbomList: {
            request: listCall.summary,
            response: publicJson(listMatch),
          },
          mbomDetail: {
            request: detailCall.summary,
            response: publicJson(detail),
          },
          inventoryAvailability: {
            request: availabilityCall.summary,
            response: availabilityCall.publicPayload,
          },
          inventoryMovements: movementPages,
        },
      })
    }

    report.materials = materials
    report.missingSupplyFacts = materials.flatMap((material) =>
      (Array.isArray(material.missingSupplyFacts) ? material.missingSupplyFacts : []).map(
        (fact) => ({
          skuCode: asRecord(material.requirement).skuCode,
          fact,
        }),
      ),
    )
    report.businessState = materials.some((material) => material.state === 'shortage')
      ? 'shortage'
      : 'sufficient'
    report.conclusion = 'runtime-confirmed'
  } catch (error) {
    report.failure = {
      type: error instanceof PublicCallError ? 'public-call' : 'runtime',
      message: safeText(error instanceof Error ? error.message : error),
    }
    throw error
  } finally {
    await mkdir(path.dirname(evidencePath!), { recursive: true })
    await writeFile(evidencePath!, JSON.stringify(publicJson(report), null, 2), 'utf8')
  }
})
