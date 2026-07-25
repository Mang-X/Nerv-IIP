import { expect, test, type APIResponse, type Page } from '@playwright/test'
import { writeFile } from 'node:fs/promises'

const baseURL = process.env.NERV_IIP_PLAYWRIGHT_BASE_URL
const adminPassword = process.env.NERV_IIP_FULLSTACK_ADMIN_PASSWORD
const evidencePath = process.env.NERV_IIP_QUALITY_BRANCH_EVIDENCE_PATH
const runtimeProfileSource = process.env.NERV_IIP_QUALITY_BRANCH_RUNTIME_PROFILE_SOURCE
const transport = process.env.NERV_IIP_QUALITY_BRANCH_TRANSPORT
const persistence = process.env.NERV_IIP_QUALITY_BRANCH_PERSISTENCE

test.skip(
  !baseURL ||
    !adminPassword ||
    !evidencePath ||
    !runtimeProfileSource ||
    !transport ||
    !persistence,
  'requires a managed full-stack session',
)
test.setTimeout(12 * 60 * 1000)

type JsonRecord = Record<string, unknown>
type Conclusion = 'runtime-confirmed' | 'gap' | 'not-verified'

class PublicCallError extends Error {
  constructor(
    readonly method: 'GET' | 'POST',
    readonly path: string,
    readonly status: number,
    readonly request: JsonRecord,
    readonly payload: unknown,
  ) {
    super(`${method} ${path} returned HTTP ${status}: ${safeText(JSON.stringify(payload))}`)
    this.name = 'PublicCallError'
  }
}

class PollTimeoutError extends Error {
  constructor(
    readonly request: JsonRecord | null,
    readonly lastData: JsonRecord,
    readonly poll: JsonRecord,
    message: string,
  ) {
    super(message)
    this.name = 'PollTimeoutError'
  }
}

type EvidenceEntry = {
  node: string
  sourceObject: string
  downstreamObject: string
  stableKey: string
  automationMode: 'automatic' | 'manual' | 'mixed'
  request: JsonRecord | null
  responseOrLog: JsonRecord
  conclusion: Conclusion
  demoWording: string
  responsibilityIssue: string | null
}

const requiredNodes = [
  'quality-plan-out-of-spec-rejection',
  'inspection-rejection-nonconformance-report',
  'quality-rejection-mes-work-order-hold',
  'reinspection-in-spec-pass',
  'reinspection-mes-hold-auto-release',
  'quality-hold-timeline-complete',
  'nonconformance-report-disposition',
] as const

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

function listOf(value: unknown): JsonRecord[] {
  return Array.isArray(value) ? value.map(asRecord) : []
}

function textOf(value: unknown): string {
  if (value === null || value === undefined) return ''
  return String(value)
}

function safeText(value: unknown): string {
  return textOf(value)
    .replace(/authorization/gi, '<redacted-header>')
    .replace(/bearer\s+[^\s"']+/gi, '<redacted-credential>')
    .replace(/password/gi, '<redacted-field>')
    .replace(/(?:access|refresh)[_-]?token/gi, '<redacted-field>')
    .slice(0, 1200)
}

function publicJson(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(publicJson)
  if (value === null || typeof value !== 'object')
    return typeof value === 'string' ? safeText(value) : value
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

async function captureSessionCredential(page: Page): Promise<string> {
  const businessRequest = page.waitForRequest((request) => {
    const path = new URL(request.url()).pathname
    return (
      path === '/api/business-console/v1/master-data/skus' &&
      Boolean(request.headers().authorization)
    )
  })
  await page.goto('/master-data/skus')
  const credential = (await businessRequest).headers().authorization
  if (!credential)
    throw new Error('Authenticated public business request did not carry a session credential.')
  return credential
}

test('MAN-520 records the public quality exception branch', async ({ page }) => {
  const now = new Date()
  const suffix = now
    .toISOString()
    .replace(/[-:TZ.]/g, '')
    .slice(0, 14)
  let organizationId = ''
  let environmentId = ''
  const uomCode = `UOM-QB-${suffix}`
  const siteCode = `SITE-QB-${suffix}`
  const workshopCode = `SHOP-QB-${suffix}`
  const lineCode = `LINE-QB-${suffix}`
  const workCenterCode = `WC-QB-${suffix}`
  const finishedSku = `FG-QB-${suffix}`
  const planCode = `IP-QB-${suffix}`
  const characteristicCode = `DIM-QB-${suffix}`
  const characteristicUnit = 'mm'
  const nominalValue = 10
  const lowerSpecLimit = 9.5
  const upperSpecLimit = 10.5
  const outOfSpecMeasuredValue = 12.4
  const inSpecMeasuredValue = 10.1
  const inspectedQuantity = 10
  const defectQuantity = 2
  const mesSourceService = 'mes'
  const mesHoldSourceService = 'business-mes'
  const inspectionSourceType = 'operation'
  const defectReason = `QB-${suffix}-oversize`
  const dispositionReason = `MAN-520 quality branch out-of-tolerance ${suffix}`

  const evidence = new Map<string, EvidenceEntry>()
  const setup: JsonRecord[] = []
  let sessionCredential = ''
  let workOrderNo = ''
  let inspectionPlanId = ''
  let rejectedInspectionRecordId = ''
  let reinspectionRecordId = ''
  let ncrId = ''
  let ncrCode = ''

  for (const node of requiredNodes) {
    evidence.set(node, {
      node,
      sourceObject: planCode,
      downstreamObject: 'not-observed',
      stableKey: planCode,
      automationMode: 'automatic',
      request: null,
      responseOrLog: { reason: 'upstream evidence was not established in this run' },
      conclusion: 'not-verified',
      demoWording: `${node}: this run did not establish a public runtime association.`,
      responsibilityIssue: null,
    })
  }

  const record = (entry: EvidenceEntry) => evidence.set(entry.node, entry)

  const call = async (method: 'GET' | 'POST', path: string, body?: JsonRecord) => {
    const url = new URL(path, baseURL!).toString()
    const response = await page.request.fetch(url, {
      method,
      data: body,
      headers: { authorization: sessionCredential },
    })
    const payload = await jsonOf(response)
    const summary = {
      method,
      path: new URL(url).pathname + new URL(url).search,
      status: response.status(),
      correlationId:
        response.headers()['x-correlation-id'] ?? response.headers().traceparent ?? null,
      body: body ? publicJson(body) : null,
    }
    if (!response.ok()) {
      throw new PublicCallError(
        method,
        summary.path,
        response.status(),
        summary,
        publicJson(payload),
      )
    }
    return { payload, summary, publicPayload: publicJson(payload) as JsonRecord }
  }

  const queryPath = (path: string, query: JsonRecord) => {
    const url = new URL(path, baseURL!)
    for (const [key, value] of Object.entries(query)) {
      if (value !== null && value !== undefined && value !== '')
        url.searchParams.set(key, String(value))
    }
    return `${url.pathname}${url.search}`
  }

  const pollData = async (
    path: string,
    query: JsonRecord,
    predicate: (data: JsonRecord) => boolean,
    timeoutMs = 90_000,
  ) => {
    const startedAt = Date.now()
    const deadline = startedAt + timeoutMs
    let attempts = 0
    let lastData: JsonRecord = {}
    let lastRequest: JsonRecord | null = null
    do {
      attempts += 1
      try {
        const response = await call('GET', queryPath(path, query))
        lastRequest = response.summary
        lastData = asRecord(dataOf(response.payload))
        if (predicate(lastData)) {
          return {
            data: lastData,
            call: response,
            poll: { attempts, elapsedMs: Date.now() - startedAt, timeoutMs },
          }
        }
      } catch (error) {
        if (!(error instanceof PublicCallError && error.status === 404)) throw error
        lastRequest = error.request
        lastData = asRecord(error.payload)
      }
      const remainingMs = deadline - Date.now()
      if (remainingMs > 0) await page.waitForTimeout(Math.min(1_000, remainingMs))
    } while (Date.now() < deadline)
    throw new PollTimeoutError(
      lastRequest,
      lastData,
      { attempts, elapsedMs: Date.now() - startedAt, timeoutMs },
      `Timed out after ${attempts} attempts in ${timeoutMs}ms waiting for run-scoped data from ${path}; last data=${safeText(JSON.stringify(lastData))}.`,
    )
  }

  const markFailure = (
    node: (typeof requiredNodes)[number],
    error: unknown,
    mode: EvidenceEntry['automationMode'] = 'automatic',
    issue: string | null = null,
  ) => {
    const current = evidence.get(node)!
    const pollFailure = error instanceof PollTimeoutError ? error : null
    const callFailure = error instanceof PublicCallError ? error : null
    record({
      ...current,
      automationMode: mode,
      request: pollFailure?.request ?? callFailure?.request ?? current.request,
      responseOrLog: pollFailure
        ? {
            error: safeText(pollFailure.message),
            poll: pollFailure.poll,
            lastData: publicJson(pollFailure.lastData),
          }
        : callFailure
          ? {
              error: safeText(callFailure.message),
              response: publicJson(callFailure.payload),
            }
          : { error: safeText(error instanceof Error ? error.message : error) },
      conclusion: 'gap',
      demoWording: `${node}: the public runtime attempt did not converge; present this as a gap, not a completed hop.`,
      responsibilityIssue: issue,
    })
  }

  const fetchWorkOrder = () =>
    call(
      'GET',
      queryPath(`/api/business-console/v1/mes/work-orders/${encodeURIComponent(workOrderNo)}`, {
        organizationId,
        environmentId,
      }),
    )

  const runScopedHold = (data: JsonRecord) =>
    listOf(data.qualityHolds).find(
      (hold) =>
        textOf(hold.sourceService) === mesHoldSourceService &&
        textOf(hold.sourceDocumentId) === workOrderNo,
    ) ?? null

  try {
    await page.goto('/login')
    const loginName = page.getByLabel('登录名')
    await expect(loginName).toBeVisible({ timeout: 120_000 })
    const loginResponse = page.waitForResponse(
      (response) => new URL(response.url()).pathname === '/api/console/v1/auth/login',
    )
    await loginName.fill('admin')
    await page.getByLabel('密码').fill(adminPassword!)
    await page.getByRole('button', { name: '登录' }).click()
    const login = await loginResponse
    expect(login.ok()).toBe(true)
    const auth = asRecord(dataOf(await login.json()))
    const principal = asRecord(auth.principal)
    organizationId = textOf(principal.organizationId)
    environmentId = textOf(principal.environmentId)
    const principalType = textOf(principal.principalType).trim().toLowerCase()
    const principalId = textOf(principal.principalId).trim()
    if (!organizationId || !environmentId || !principalType || !principalId) {
      throw new Error(
        'The public login response did not expose the authenticated principal and organization/environment scope.',
      )
    }
    await expect(page).toHaveURL(new URL('/', baseURL!).toString())
    sessionCredential = await captureSessionCredential(page)

    const create = async (path: string, body: JsonRecord) => {
      const result = await call('POST', path, body)
      setup.push({ request: result.summary, response: result.publicPayload })
      return dataOf(result.payload)
    }

    let prerequisitesReady = true
    try {
      await create('/api/business-console/v1/master-data/units-of-measure', {
        organizationId,
        environmentId,
        code: uomCode,
        name: 'MAN-520 quality branch each',
        dimensionType: 'quantity',
        precision: 3,
        roundingMode: 'half-up',
        idempotencyKey: `uom-${suffix}`,
      })
      await create('/api/business-console/v1/master-data/sites', {
        organizationId,
        environmentId,
        code: siteCode,
        name: 'MAN-520 quality branch site',
        timezone: 'UTC',
        idempotencyKey: `site-${suffix}`,
      })
      await create('/api/business-console/v1/master-data/workshops', {
        organizationId,
        environmentId,
        code: workshopCode,
        name: 'MAN-520 quality branch workshop',
        siteCode,
        idempotencyKey: `shop-${suffix}`,
      })
      await create('/api/business-console/v1/master-data/production-lines', {
        organizationId,
        environmentId,
        code: lineCode,
        name: 'MAN-520 quality branch line',
        siteCode,
        workshopCode,
        idempotencyKey: `line-${suffix}`,
      })
      await create('/api/business-console/v1/master-data/work-centers', {
        organizationId,
        environmentId,
        code: workCenterCode,
        name: 'MAN-520 quality branch work center',
        capacityMinutesPerDay: 1_440,
        resourceType: 'machine',
        plantCode: siteCode,
        lineCode,
        defaultCalendarCode: `CAL-QB-${suffix}`,
        capacityUnit: 'minute',
        finiteCapacity: true,
        workshopCode,
        idempotencyKey: `wc-${suffix}`,
      })
      await create('/api/business-console/v1/master-data/skus', {
        organizationId,
        environmentId,
        code: finishedSku,
        name: 'MAN-520 quality branch finished good',
        baseUomCode: uomCode,
        category: 'electronic',
        materialType: 'finished-goods',
        batchTrackingPolicy: 'none',
        serialTrackingPolicy: 'none',
        shelfLifePolicyCode: 'none',
        storageConditionCode: 'ambient',
        defaultBarcodeRuleCode: 'code128',
        qualityRequired: true,
        complianceTags: [],
        idempotencyKey: `sku-${finishedSku}`,
      })

      const inspectionPlan = asRecord(
        await create('/api/business-console/v1/quality/inspection-plans', {
          organizationId,
          environmentId,
          planCode,
          category: inspectionSourceType,
          skuCode: finishedSku,
          partnerId: null,
          workCenterId: workCenterCode,
          deviceAssetId: null,
          documentType: 'operation-task',
          characteristics: [
            {
              characteristicCode,
              name: 'MAN-520 critical dimension',
              method: 'gauge',
              severity: 'major',
              required: true,
              samplingRule: '100-percent',
              characteristicType: 'variable',
              nominalValue,
              lowerSpecLimit,
              upperSpecLimit,
              unitCode: characteristicUnit,
            },
          ],
        }),
      )
      inspectionPlanId = textOf(inspectionPlan.inspectionPlanId).trim()
      if (!inspectionPlanId) {
        throw new Error('Quality did not return the run-scoped inspection plan id.')
      }
      await create(
        `/api/business-console/v1/quality/inspection-plans/${encodeURIComponent(inspectionPlanId)}/activate`,
        { organizationId, environmentId, inspectionPlanId },
      )
      const publishedCharacteristics = await call(
        'GET',
        queryPath(
          `/api/business-console/v1/quality/inspection-plans/${encodeURIComponent(inspectionPlanId)}/characteristics`,
          { organizationId, environmentId },
        ),
      )
      const publishedCharacteristic = listOf(
        asRecord(dataOf(publishedCharacteristics.payload)).items,
      ).find((item) => textOf(item.characteristicCode) === characteristicCode)
      if (
        !publishedCharacteristic ||
        Number(publishedCharacteristic.lowerSpecLimit) !== lowerSpecLimit ||
        Number(publishedCharacteristic.upperSpecLimit) !== upperSpecLimit ||
        textOf(publishedCharacteristic.characteristicType) !== 'variable'
      ) {
        throw new Error(
          `Quality did not publish the run-scoped variable tolerance for ${characteristicCode}: ${safeText(JSON.stringify(publishedCharacteristic))}.`,
        )
      }
      setup.push({
        request: publishedCharacteristics.summary,
        response: publishedCharacteristics.publicPayload,
      })

      const rushWorkOrder = asRecord(
        await create('/api/business-console/v1/mes/work-orders/rush', {
          organizationId,
          environmentId,
          workOrderId: null,
          skuId: finishedSku,
          productionVersionId: null,
          quantity: inspectedQuantity,
          dueUtc: new Date(now.getTime() + 8 * 3_600_000).toISOString(),
          workCenterId: workCenterCode,
          operationTaskId: null,
          operationSequence: 10,
          durationMinutes: 30,
          idempotencyKey: `rush-wo-${suffix}`,
        }),
      )
      workOrderNo = textOf(rushWorkOrder.workOrderId).trim()
      if (!workOrderNo || !/^WO-\d{8}-\d{6}$/.test(workOrderNo)) {
        throw new Error(
          `MES did not allocate a human-readable run-scoped work-order number; received '${safeText(workOrderNo)}'.`,
        )
      }
      const workOrderDetail = await fetchWorkOrder()
      const workOrderData = asRecord(dataOf(workOrderDetail.payload))
      if (
        textOf(workOrderData.workOrderId) !== workOrderNo ||
        Number(workOrderData.quantity) !== inspectedQuantity
      ) {
        throw new Error(
          `MES work order ${workOrderNo} did not expose the run-scoped quantity: ${safeText(JSON.stringify(workOrderData))}.`,
        )
      }
      setup.push({ request: workOrderDetail.summary, response: workOrderDetail.publicPayload })
    } catch (error) {
      prerequisitesReady = false
      const blockedReason = safeText(error instanceof Error ? error.message : error)
      setup.push({
        phase: 'public-prerequisites',
        conclusion: 'gap',
        responsibilityIssue: 'unattributed / requires follow-up issue',
        error: blockedReason,
      })
      for (const node of requiredNodes) {
        const entry = evidence.get(node)!
        if (entry.conclusion !== 'not-verified') continue
        record({
          ...entry,
          responseOrLog: { blockedBy: 'public-prerequisites', error: blockedReason },
          demoWording: `${node}: this run was blocked before the quality branch by a public-prerequisite gap.`,
          responsibilityIssue: 'unattributed / requires follow-up issue',
        })
      }
    }

    if (prerequisitesReady) {
      try {
        const rejected = await call('POST', '/api/business-console/v1/quality/inspection-records', {
          organizationId,
          environmentId,
          inspectionPlanId,
          sourceType: inspectionSourceType,
          sourceService: mesSourceService,
          sourceDocumentId: workOrderNo,
          skuCode: finishedSku,
          inspectedQuantity,
          batchNo: null,
          serialNo: null,
          resultLines: [
            {
              characteristicCode,
              observedValue: String(outOfSpecMeasuredValue),
              unitCode: characteristicUnit,
              result: 'failed',
              defectReason,
              defectQuantity,
              attachmentFileIds: [],
              measuredValue: outOfSpecMeasuredValue,
            },
          ],
          dispositionReason,
          dispositionAttachmentFileIds: [],
        })
        rejectedInspectionRecordId = textOf(
          asRecord(dataOf(rejected.payload)).inspectionRecordId,
        ).trim()
        if (!rejectedInspectionRecordId) {
          throw new Error('Quality did not return the run-scoped inspection record id.')
        }
        const detail = await call(
          'GET',
          queryPath(
            `/api/business-console/v1/quality/inspection-records/${encodeURIComponent(rejectedInspectionRecordId)}`,
            { organizationId, environmentId },
          ),
        )
        const recordData = asRecord(dataOf(detail.payload))
        const line = listOf(recordData.resultLines).find(
          (item) => textOf(item.characteristicCode) === characteristicCode,
        )
        if (
          textOf(recordData.result) !== 'rejected' ||
          textOf(recordData.sourceDocumentId) !== workOrderNo ||
          textOf(recordData.sourceService) !== mesSourceService ||
          Number(recordData.attemptNumber) !== 1 ||
          !line ||
          textOf(line.result) !== 'failed' ||
          Number(line.measuredValue) !== outOfSpecMeasuredValue ||
          Number(line.observedValue) !== outOfSpecMeasuredValue ||
          Number(line.defectQuantity) !== defectQuantity
        ) {
          throw new Error(
            `Quality did not judge the out-of-tolerance measurement as rejected: ${safeText(JSON.stringify(recordData))}.`,
          )
        }
        record({
          node: 'quality-plan-out-of-spec-rejection',
          sourceObject: `${planCode} -> ${characteristicCode}`,
          downstreamObject: workOrderNo,
          stableKey: `${planCode} -> ${characteristicCode} -> ${workOrderNo} -> attempt 1`,
          automationMode: 'manual',
          request: rejected.summary,
          responseOrLog: {
            tolerance: { nominalValue, lowerSpecLimit, upperSpecLimit, unit: characteristicUnit },
            measuredValue: outOfSpecMeasuredValue,
            inspectionRecord: publicJson(recordData),
          },
          conclusion: 'runtime-confirmed',
          demoWording:
            'The inspector entered an out-of-tolerance measurement against the active run-scoped plan and Quality — not the client — judged the record rejected.',
          responsibilityIssue: '#1099',
        })
      } catch (error) {
        markFailure('quality-plan-out-of-spec-rejection', error, 'manual')
      }
    }

    if (rejectedInspectionRecordId) {
      try {
        const opened = await call(
          'POST',
          queryPath(
            `/api/business-console/v1/quality/inspection-records/${encodeURIComponent(rejectedInspectionRecordId)}/failures/ncr`,
            { organizationId, environmentId },
          ),
          { defectReason, attachmentFileIds: [] },
        )
        ncrId = textOf(asRecord(dataOf(opened.payload)).ncrId).trim()
        if (!ncrId) throw new Error('Quality did not return the run-scoped NCR id.')
        const ncrDetail = await call(
          'GET',
          queryPath(`/api/business-console/v1/quality/ncrs/${encodeURIComponent(ncrId)}`, {
            organizationId,
            environmentId,
          }),
        )
        const ncrData = asRecord(dataOf(ncrDetail.payload))
        ncrCode = textOf(ncrData.code).trim()
        if (!ncrCode.startsWith('NCR-')) {
          throw new Error(
            `Quality did not expose a human-readable NCR document number; received '${safeText(ncrCode)}'.`,
          )
        }
        if (
          textOf(ncrData.status) !== 'open' ||
          textOf(ncrData.sourceType) !== 'in-process' ||
          textOf(ncrData.sourceDocumentId) !== workOrderNo ||
          textOf(ncrData.skuCode) !== finishedSku ||
          textOf(ncrData.defectReason) !== defectReason ||
          Number(ncrData.defectQuantity) !== defectQuantity ||
          textOf(ncrData.sourceInspectionRecordId) !== rejectedInspectionRecordId
        ) {
          throw new Error(
            `NCR ${ncrCode} did not preserve the run-scoped inspection lineage: ${safeText(JSON.stringify(ncrData))}.`,
          )
        }
        record({
          node: 'inspection-rejection-nonconformance-report',
          sourceObject: workOrderNo,
          downstreamObject: ncrCode,
          stableKey: `${workOrderNo} -> attempt 1 -> ${ncrCode}`,
          automationMode: 'manual',
          request: opened.summary,
          responseOrLog: {
            ncr: publicJson(ncrData),
            documentNumberDisclosure:
              'Quality issues NCR document numbers as NCR-<org>-<env>-<uuid>; the code is the product NCR number, not a bare GUID substituted by this harness.',
          },
          conclusion: 'runtime-confirmed',
          demoWording:
            'The rejected inspection opened a public NCR whose document number, defect quantity, reason, and source inspection all belong to this exact work order.',
          responsibilityIssue: '#1099',
        })
      } catch (error) {
        markFailure('inspection-rejection-nonconformance-report', error, 'manual')
      }
    }

    if (ncrCode) {
      try {
        const held = await pollData(
          `/api/business-console/v1/mes/work-orders/${encodeURIComponent(workOrderNo)}`,
          { organizationId, environmentId },
          (data) => {
            const hold = runScopedHold(data)
            return (
              hold !== null &&
              hold.isActive === true &&
              textOf(hold.heldInspectionRecordId) === rejectedInspectionRecordId
            )
          },
        )
        const hold = runScopedHold(held.data)!
        const listed = await call(
          'GET',
          queryPath('/api/business-console/v1/mes/work-orders', {
            organizationId,
            environmentId,
            keyword: workOrderNo,
            take: 100,
          }),
        )
        const listedWorkOrder = rowsOf(listed.payload).find(
          (row) => textOf(row.workOrderId) === workOrderNo,
        )
        if (!listedWorkOrder || listedWorkOrder.hasActiveQualityHold !== true) {
          throw new Error(
            `MES work-order list did not surface the active quality hold for ${workOrderNo}: ${safeText(JSON.stringify(listedWorkOrder))}.`,
          )
        }
        record({
          node: 'quality-rejection-mes-work-order-hold',
          sourceObject: rejectedInspectionRecordId,
          downstreamObject: workOrderNo,
          stableKey: `${ncrCode} -> ${workOrderNo} -> hold active`,
          automationMode: 'automatic',
          request: held.call.summary,
          responseOrLog: {
            poll: held.poll,
            hold: publicJson(hold),
            listRow: publicJson(listedWorkOrder),
          },
          conclusion: 'runtime-confirmed',
          demoWording:
            'The Quality rejection crossed Redis into MES and froze the exact run-scoped work order, naming the rejecting inspection record as the hold source.',
          responsibilityIssue: '#1099',
        })
      } catch (error) {
        markFailure('quality-rejection-mes-work-order-hold', error)
      }
    }

    if (evidence.get('quality-rejection-mes-work-order-hold')?.conclusion === 'runtime-confirmed') {
      try {
        const reinspection = await call(
          'POST',
          queryPath(
            `/api/business-console/v1/quality/inspection-records/${encodeURIComponent(rejectedInspectionRecordId)}/reinspections`,
            { organizationId, environmentId },
          ),
          {
            resultLines: [
              {
                characteristicCode,
                observedValue: String(inSpecMeasuredValue),
                unitCode: characteristicUnit,
                result: 'passed',
                defectReason: null,
                defectQuantity: null,
                attachmentFileIds: [],
                measuredValue: inSpecMeasuredValue,
              },
            ],
            dispositionReason: null,
            dispositionAttachmentFileIds: [],
            measuringDeviceId: null,
          },
        )
        const reinspectionData = asRecord(dataOf(reinspection.payload))
        reinspectionRecordId = textOf(reinspectionData.inspectionRecordId).trim()
        if (!reinspectionRecordId || Number(reinspectionData.attemptNumber) !== 2) {
          throw new Error(
            `Quality did not return a second-attempt reinspection record: ${safeText(JSON.stringify(reinspectionData))}.`,
          )
        }
        const detail = await call(
          'GET',
          queryPath(
            `/api/business-console/v1/quality/inspection-records/${encodeURIComponent(reinspectionRecordId)}`,
            { organizationId, environmentId },
          ),
        )
        const recordData = asRecord(dataOf(detail.payload))
        const line = listOf(recordData.resultLines).find(
          (item) => textOf(item.characteristicCode) === characteristicCode,
        )
        if (
          textOf(recordData.result) !== 'passed' ||
          Number(recordData.attemptNumber) !== 2 ||
          textOf(recordData.reinspectionOfInspectionRecordId) !== rejectedInspectionRecordId ||
          textOf(recordData.sourceDocumentId) !== workOrderNo ||
          !line ||
          textOf(line.result) !== 'passed' ||
          Number(line.measuredValue) !== inSpecMeasuredValue
        ) {
          throw new Error(
            `Quality did not judge the in-tolerance reinspection as passed: ${safeText(JSON.stringify(recordData))}.`,
          )
        }
        record({
          node: 'reinspection-in-spec-pass',
          sourceObject: rejectedInspectionRecordId,
          downstreamObject: reinspectionRecordId,
          stableKey: `${workOrderNo} -> attempt 1 -> attempt 2 (passed)`,
          automationMode: 'manual',
          request: reinspection.summary,
          responseOrLog: {
            measuredValue: inSpecMeasuredValue,
            tolerance: { lowerSpecLimit, upperSpecLimit, unit: characteristicUnit },
            reinspection: publicJson(recordData),
          },
          conclusion: 'runtime-confirmed',
          demoWording:
            'A same-source reinspection recorded an in-tolerance measurement, and Quality kept the audit chain by numbering it attempt 2 of the rejected record.',
          responsibilityIssue: '#954',
        })
      } catch (error) {
        markFailure('reinspection-in-spec-pass', error, 'manual')
      }
    }

    if (reinspectionRecordId) {
      try {
        const released = await pollData(
          `/api/business-console/v1/mes/work-orders/${encodeURIComponent(workOrderNo)}`,
          { organizationId, environmentId },
          (data) => {
            const hold = runScopedHold(data)
            return (
              hold !== null &&
              hold.isActive === false &&
              textOf(hold.inspectionRecordId) === reinspectionRecordId
            )
          },
        )
        const hold = runScopedHold(released.data)!
        if (
          textOf(hold.heldInspectionRecordId) !== rejectedInspectionRecordId ||
          !textOf(hold.releasedAtUtc)
        ) {
          throw new Error(
            `MES released the hold on ${workOrderNo} without a complete release audit: ${safeText(JSON.stringify(hold))}.`,
          )
        }
        const listed = await call(
          'GET',
          queryPath('/api/business-console/v1/mes/work-orders', {
            organizationId,
            environmentId,
            keyword: workOrderNo,
            take: 100,
          }),
        )
        const listedWorkOrder = rowsOf(listed.payload).find(
          (row) => textOf(row.workOrderId) === workOrderNo,
        )
        if (!listedWorkOrder || listedWorkOrder.hasActiveQualityHold !== false) {
          throw new Error(
            `MES work-order list still reports an active quality hold for ${workOrderNo}: ${safeText(JSON.stringify(listedWorkOrder))}.`,
          )
        }
        record({
          node: 'reinspection-mes-hold-auto-release',
          sourceObject: reinspectionRecordId,
          downstreamObject: workOrderNo,
          stableKey: `${workOrderNo} -> attempt 2 -> hold released`,
          automationMode: 'automatic',
          request: released.call.summary,
          responseOrLog: {
            poll: released.poll,
            hold: publicJson(hold),
            listRow: publicJson(listedWorkOrder),
            forceReleaseDisclosure:
              'No force-release facade was called in this run; the release is the automatic consequence of the passed reinspection crossing Redis into MES.',
          },
          conclusion: 'runtime-confirmed',
          demoWording:
            'The passed reinspection automatically released the MES quality hold — no operator override, and the release names the reinspection record.',
          responsibilityIssue: '#954',
        })
      } catch (error) {
        markFailure('reinspection-mes-hold-auto-release', error)
      }
    }

    if (evidence.get('reinspection-mes-hold-auto-release')?.conclusion === 'runtime-confirmed') {
      try {
        const timeline = await pollData(
          `/api/business-console/v1/mes/quality-holds/${encodeURIComponent(workOrderNo)}/timeline`,
          { organizationId, environmentId, sourceService: mesHoldSourceService },
          (data) => listOf(data.items).length === 2,
        )
        const items = listOf(timeline.data.items)
        const applied = items[0]
        const releasedEntry = items[1]
        if (
          !applied ||
          !releasedEntry ||
          textOf(applied.eventKind) !== 'hold-applied' ||
          textOf(applied.sourceInspectionRecordId) !== rejectedInspectionRecordId ||
          textOf(applied.origin) !== 'automatic' ||
          textOf(applied.sourceDocumentId) !== workOrderNo ||
          textOf(releasedEntry.eventKind) !== 'inspection-released' ||
          textOf(releasedEntry.sourceInspectionRecordId) !== reinspectionRecordId ||
          textOf(releasedEntry.origin) !== 'automatic' ||
          textOf(releasedEntry.sourceDocumentId) !== workOrderNo ||
          new Date(textOf(releasedEntry.occurredAtUtc)).getTime() <
            new Date(textOf(applied.occurredAtUtc)).getTime()
        ) {
          throw new Error(
            `The quality-hold timeline for ${workOrderNo} is not the expected automatic apply/release pair: ${safeText(JSON.stringify(items))}.`,
          )
        }
        record({
          node: 'quality-hold-timeline-complete',
          sourceObject: workOrderNo,
          downstreamObject: `${textOf(applied.eventKind)} -> ${textOf(releasedEntry.eventKind)}`,
          stableKey: `${workOrderNo} -> hold-applied(attempt 1) -> inspection-released(attempt 2)`,
          automationMode: 'automatic',
          request: timeline.call.summary,
          responseOrLog: { poll: timeline.poll, items: publicJson(items) },
          conclusion: 'runtime-confirmed',
          demoWording:
            'The public hold timeline tells the whole story in order: automatic freeze on the rejected inspection, automatic release on the passed reinspection.',
          responsibilityIssue: '#954',
        })
      } catch (error) {
        markFailure('quality-hold-timeline-complete', error)
      }
    }

    if (ncrCode) {
      try {
        const disposition = await call(
          'POST',
          queryPath(
            `/api/business-console/v1/quality/ncrs/${encodeURIComponent(ncrId)}/disposition`,
            { organizationId, environmentId },
          ),
          {
            dispositionType: 'rework',
            dispositionApprovalChainId: null,
            attachmentFileIds: [],
            mrbReviews: [
              {
                reviewerId: `${principalType}:${principalId}`,
                decision: 'approved',
                comment: `MAN-520 quality branch rework disposition ${suffix}`,
                reviewedAtUtc: new Date().toISOString(),
              },
            ],
          },
        )
        const dispositioned = await pollData(
          `/api/business-console/v1/quality/ncrs/${encodeURIComponent(ncrId)}`,
          { organizationId, environmentId },
          (data) => textOf(data.status) === 'disposition-in-progress',
        )
        if (textOf(dispositioned.data.code) !== ncrCode) {
          throw new Error(
            `NCR disposition read-back returned ${safeText(textOf(dispositioned.data.code))} instead of ${ncrCode}.`,
          )
        }
        record({
          node: 'nonconformance-report-disposition',
          sourceObject: ncrCode,
          downstreamObject: 'rework',
          stableKey: `${workOrderNo} -> ${ncrCode} -> rework disposition`,
          automationMode: 'manual',
          request: disposition.summary,
          responseOrLog: {
            poll: dispositioned.poll,
            ncr: publicJson(dispositioned.data),
            mrbDisclosure:
              'Quality enforced an approved MRB review before accepting the rework disposition; the reviewer is the authenticated session principal.',
          },
          conclusion: 'runtime-confirmed',
          demoWording:
            'The run-scoped NCR moved to an MRB-approved rework disposition through the public facade, closing the exception with an auditable decision.',
          responsibilityIssue: '#1099',
        })
      } catch (error) {
        markFailure('nonconformance-report-disposition', error, 'manual')
      }
    }
  } finally {
    const entries = requiredNodes.map((node) => evidence.get(node)!)
    await writeFile(
      evidencePath!,
      JSON.stringify(
        {
          issue: 'Linear MAN-520 / GitHub #1099',
          generatedAtUtc: new Date().toISOString(),
          runSuffix: suffix,
          organizationId,
          environmentId,
          workOrderNo,
          ncrCode,
          runtimeProfileSource,
          transport,
          persistence,
          assertionBoundary:
            'public BusinessGateway HTTP only; no database reads as business assertions',
          setup,
          entries,
          summary: Object.fromEntries(
            (['runtime-confirmed', 'gap', 'not-verified'] as const).map((conclusion) => [
              conclusion,
              entries.filter((entry) => entry.conclusion === conclusion).length,
            ]),
          ),
        },
        null,
        2,
      ),
      'utf8',
    )
    sessionCredential = ''
  }

  const entries = requiredNodes.map((node) => evidence.get(node)!)
  const unacceptableEntries = entries.filter((entry) => entry.conclusion !== 'runtime-confirmed')
  expect(
    unacceptableEntries.map((entry) => ({
      node: entry.node,
      conclusion: entry.conclusion,
      responsibilityIssue: entry.responsibilityIssue,
    })),
    'Every quality-branch node must be runtime-confirmed through public BusinessGateway evidence.',
  ).toEqual([])
  expect(entries.some((entry) => entry.conclusion === 'runtime-confirmed')).toBe(true)
})
