import { expect, test, type APIResponse, type Page } from '@playwright/test'
import { writeFile } from 'node:fs/promises'

const baseURL = process.env.NERV_IIP_PLAYWRIGHT_BASE_URL
const adminPassword = process.env.NERV_IIP_FULLSTACK_ADMIN_PASSWORD
const evidencePath = process.env.NERV_IIP_MAIN_CHAIN_EVIDENCE_PATH
const runtimeProfileSource = process.env.NERV_IIP_MAIN_CHAIN_RUNTIME_PROFILE_SOURCE
const transport = process.env.NERV_IIP_MAIN_CHAIN_TRANSPORT
const persistence = process.env.NERV_IIP_MAIN_CHAIN_PERSISTENCE

test.skip(
  !baseURL ||
    !adminPassword ||
    !evidencePath ||
    !runtimeProfileSource ||
    !transport ||
    !persistence,
  'requires a managed full-stack session',
)
test.setTimeout(18 * 60 * 1000)

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

class TrackedSetupError extends Error {
  constructor(
    readonly responsibilityIssue: string,
    message: string,
  ) {
    super(message)
    this.name = 'TrackedSetupError'
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
  'sales-order-demand-source',
  'demand-source-mrp-suggestion',
  'mrp-suggestion-mes-work-order',
  'mes-work-order-schedule-plan',
  'schedule-release-mes-execution',
  'erp-work-center-cost-rate',
  'mes-task-production-report',
  'production-report-quality',
  'report-finished-goods-receipt',
  'finished-goods-receipt-inventory-posting',
  'inventory-produced-lot-fulfillment-lookup',
  'sales-order-delivery-order',
  'delivery-order-wms-outbound',
  'wms-completed-erp-delivery-status',
  'wms-completed-account-receivable',
  'account-receivable-voucher',
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

function textOf(value: unknown): string {
  if (value === null || value === undefined) return ''
  return String(value)
}

function dateOnly(value: Date): string {
  return value.toISOString().slice(0, 10)
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

test('MAN-524 records the public sales-to-fulfillment main chain', async ({ page }) => {
  const now = new Date()
  const suffix = now
    .toISOString()
    .replace(/[-:TZ.]/g, '')
    .slice(0, 14)
  let organizationId = ''
  let environmentId = ''
  const salesOrderNo = `SO-MAN524-${suffix}`
  const quotationNo = `QT-MAN524-${suffix}`
  const deliveryOrderNo = `DO-MAN524-${suffix}`
  const producedLotNo = `LOT-MAN524-${suffix}`
  const uomCode = `UOM-M524-${suffix}`
  const siteCode = `SITE-M524-${suffix}`
  const materialSiteCode = 'production'
  const finishedGoodsSiteCode = 'finished-goods'
  const finishedGoodsLocationCode = 'receiving'
  const workshopCode = `SHOP-M524-${suffix}`
  const lineCode = `LINE-M524-${suffix}`
  const workCenterCode = `WC-M524-${suffix}`
  const deviceCode = `DEV-M524-${suffix}`
  const customerCode = `CUST-M524-${suffix}`
  const supplierCode = `SUP-M524-${suffix}`
  const finishedSku = `FG-M524-${suffix}`
  const materialSku = `RM-M524-${suffix}`
  const rawMaterialQuantity = 10
  const finishedGoodsQuantity = 10
  const operationDurationMinutes = 5
  const workCenterHourlyRate = 3_000
  const finishedGoodsUnitCost = 25
  const finishedGoodsCapitalizedCost = finishedGoodsQuantity * finishedGoodsUnitCost
  const expectedTheoreticalRatePerHour = finishedGoodsQuantity / (operationDurationMinutes / 60)
  const ticksPerHour = 36_000_000_000
  const workCenterCostRateReason = `MAN-595 governed main-chain rate ${suffix}`
  const rawMaterialLotNo = `RMLOT-M524-${suffix}`
  const purchaseOrderNo = `PO-M524-${suffix}`
  const inboundOrderNo = `IN-M524-${suffix}`
  const putawayTaskNo = `PUT-M524-${suffix}`
  const operationCode = `OP-M524-${suffix}`
  const engineeringBomCode = `EB-M524-${suffix}`
  const manufacturingBomCode = `MB-M524-${suffix}`
  const routingCode = `RT-M524-${suffix}`
  const evidence = new Map<string, EvidenceEntry>()
  const setup: JsonRecord[] = []
  let sessionCredential = ''
  let deviceAssetId = ''
  let inspectionPlanId = ''

  for (const node of requiredNodes) {
    evidence.set(node, {
      node,
      sourceObject: salesOrderNo,
      downstreamObject: 'not-observed',
      stableKey: salesOrderNo,
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

  const fetchWorkOrder = (workOrderId: string) =>
    call(
      'GET',
      queryPath(`/api/business-console/v1/mes/work-orders/${encodeURIComponent(workOrderId)}`, {
        organizationId,
        environmentId,
      }),
    )

  const pollRows = async (
    path: string,
    query: JsonRecord,
    predicate: (row: JsonRecord) => boolean,
    timeoutMs = 45_000,
  ) => {
    const startedAt = Date.now()
    const deadline = startedAt + timeoutMs
    let attempts = 0
    let lastRows: JsonRecord[] = []
    let lastRequest: JsonRecord | null = null
    do {
      attempts += 1
      const response = await call('GET', queryPath(path, query))
      lastRequest = response.summary
      lastRows = rowsOf(response.payload)
      const match = lastRows.find(predicate)
      if (match) {
        return {
          match,
          call: response,
          poll: { attempts, elapsedMs: Date.now() - startedAt, timeoutMs },
        }
      }
      const remainingMs = deadline - Date.now()
      if (remainingMs > 0) await page.waitForTimeout(Math.min(1_000, remainingMs))
    } while (Date.now() < deadline)
    throw new PollTimeoutError(
      lastRequest,
      { items: lastRows },
      { attempts, elapsedMs: Date.now() - startedAt, timeoutMs },
      `Timed out after ${attempts} attempts in ${timeoutMs}ms waiting for a run-scoped row from ${path}; last rows=${safeText(JSON.stringify(lastRows))}.`,
    )
  }

  const pollData = async (
    path: string,
    query: JsonRecord,
    predicate: (data: JsonRecord) => boolean,
    timeoutMs = 45_000,
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
      demoWording: `${node}: the public runtime attempt did not converge; present this as a gap, not a completed automatic hop.`,
      responsibilityIssue: issue,
    })
  }

  try {
    await page.goto('/login')
    const loginResponse = page.waitForResponse(
      (response) => new URL(response.url()).pathname === '/api/console/v1/auth/login',
    )
    await page.getByLabel('登录名').fill('admin')
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
    let productionVersionId = ''
    let rawMaterialSupplyEvidence: JsonRecord | null = null
    try {
      await create('/api/business-console/v1/master-data/units-of-measure', {
        organizationId,
        environmentId,
        code: uomCode,
        name: 'MAN-524 each',
        dimensionType: 'quantity',
        precision: 3,
        roundingMode: 'half-up',
        idempotencyKey: `uom-${suffix}`,
      })
      await create('/api/business-console/v1/master-data/sites', {
        organizationId,
        environmentId,
        code: siteCode,
        name: 'MAN-524 site',
        timezone: 'UTC',
        idempotencyKey: `site-${suffix}`,
      })
      await create('/api/business-console/v1/master-data/workshops', {
        organizationId,
        environmentId,
        code: workshopCode,
        name: 'MAN-524 workshop',
        siteCode,
        idempotencyKey: `shop-${suffix}`,
      })
      await create('/api/business-console/v1/master-data/production-lines', {
        organizationId,
        environmentId,
        code: lineCode,
        name: 'MAN-524 line',
        siteCode,
        workshopCode,
        idempotencyKey: `line-${suffix}`,
      })
      await create('/api/business-console/v1/master-data/work-centers', {
        organizationId,
        environmentId,
        code: workCenterCode,
        name: 'MAN-524 work center',
        capacityMinutesPerDay: 1_440,
        resourceType: 'machine',
        plantCode: siteCode,
        lineCode,
        defaultCalendarCode: `CAL-M524-${suffix}`,
        capacityUnit: 'minute',
        finiteCapacity: true,
        workshopCode,
        idempotencyKey: `wc-${suffix}`,
      })
      const workCenterCostRatePath = '/api/business-console/v1/erp/finance/work-center-cost-rates'
      const rateEffectiveFromUtc = new Date(now.getTime() - 3_600_000)
      const rateEffectiveToUtc = new Date(now.getTime() + 86_400_000)
      const rateAuditAtUtc = new Date(now)
      const expectedRateActor = `${principalType}:${principalId}`
      try {
        const configuredRate = await call('POST', workCenterCostRatePath, {
          organizationId,
          environmentId,
          workCenterId: workCenterCode,
          hourlyRate: workCenterHourlyRate,
          currencyCode: 'CNY',
          effectiveFromUtc: rateEffectiveFromUtc.toISOString(),
          effectiveToUtc: rateEffectiveToUtc.toISOString(),
          reason: workCenterCostRateReason,
        })
        const configuredRateId = textOf(
          asRecord(dataOf(configuredRate.payload)).workCenterCostRateId,
        ).trim()
        const rateAuditCall = await call(
          'GET',
          queryPath(workCenterCostRatePath, {
            organizationId,
            environmentId,
            workCenterId: workCenterCode,
            atUtc: rateAuditAtUtc.toISOString(),
          }),
        )
        const rateAudit = asRecord(dataOf(rateAuditCall.payload))
        const rateItems = Array.isArray(rateAudit.items) ? rateAudit.items.map(asRecord) : []
        const currentRate = rateItems.find(
          (item) => item.isEffectiveAtUtc === true && item.isCurrentEffectiveRevision === true,
        )
        const hasExactGovernedRate =
          configuredRateId.length > 0 &&
          textOf(rateAudit.organizationId) === organizationId &&
          textOf(rateAudit.environmentId) === environmentId &&
          textOf(rateAudit.workCenterId) === workCenterCode &&
          rateAudit.currentEffectiveRevision === 1 &&
          currentRate !== undefined &&
          textOf(currentRate.workCenterCostRateId) === configuredRateId &&
          textOf(currentRate.workCenterId) === workCenterCode &&
          Number(currentRate.hourlyRate ?? 0) === workCenterHourlyRate &&
          textOf(currentRate.currencyCode) === 'CNY' &&
          new Date(textOf(currentRate.effectiveFromUtc)).getTime() ===
            rateEffectiveFromUtc.getTime() &&
          new Date(textOf(currentRate.effectiveToUtc)).getTime() === rateEffectiveToUtc.getTime() &&
          currentRate.revision === 1 &&
          textOf(currentRate.changedBy) === expectedRateActor &&
          textOf(currentRate.reason) === workCenterCostRateReason &&
          Number.isFinite(new Date(textOf(currentRate.changedAtUtc)).getTime()) &&
          currentRate.isEffectiveAtUtc === true &&
          currentRate.isCurrentEffectiveRevision === true
        if (!hasExactGovernedRate || !currentRate) {
          throw new Error(
            `ERP did not return the exact governed work-center rate revision: ${safeText(JSON.stringify(rateAudit))}.`,
          )
        }
        record({
          node: 'erp-work-center-cost-rate',
          sourceObject: `${organizationId}/${environmentId}/${workCenterCode}`,
          downstreamObject: configuredRateId,
          stableKey: `${organizationId} -> ${environmentId} -> ${workCenterCode} -> revision 1 -> ${configuredRateId}`,
          automationMode: 'manual',
          request: configuredRate.summary,
          responseOrLog: {
            configured: configuredRate.publicPayload,
            audited: rateAuditCall.publicPayload,
          },
          conclusion: 'runtime-confirmed',
          demoWording:
            'ERP publicly audited the run-scoped CNY work-center rate, effective window, revision, actor, and reason before MES production reporting.',
          responsibilityIssue: null,
        })
      } catch (error) {
        markFailure('erp-work-center-cost-rate', error, 'manual', '#1070 / MAN-595')
        throw error
      }
      await create('/api/business-console/v1/master-data/device-assets', {
        organizationId,
        environmentId,
        code: deviceCode,
        model: 'MAN-524 scheduling machine',
        lineCode,
        workCenterCode,
        assetClassCode: 'machine',
        manufacturer: 'Nerv Automation',
        serialNo: `SN-M524-${suffix}`,
        minimumCapacity: 1,
        maximumCapacity: 1,
        capacityUomCode: uomCode,
        criticality: 'normal',
        maintainable: true,
        telemetryEnabled: true,
        externalReferences: { mainChainRun: suffix },
        idempotencyKey: `device-${suffix}`,
        siteCode,
        workshopCode,
      })
      const deviceList = await call(
        'GET',
        queryPath('/api/business-console/v1/master-data/device-assets', {
          organizationId,
          environmentId,
          workCenterCode,
          keyword: deviceCode,
          take: 100,
        }),
      )
      const deviceResources = asRecord(dataOf(deviceList.payload)).resources
      const device = (Array.isArray(deviceResources) ? deviceResources : [])
        .map(asRecord)
        .find((row) => textOf(row.code).trim() === deviceCode)
      if (!device) {
        throw new Error(
          `MasterData did not expose the run-scoped device ${deviceCode} for work center ${workCenterCode}.`,
        )
      }
      deviceAssetId = textOf(device.deviceAssetId).trim()
      if (!deviceAssetId || deviceAssetId === workCenterCode) {
        throw new Error(
          `MasterData device ${deviceCode} did not expose a stable device asset id distinct from ${workCenterCode}.`,
        )
      }
      setup.push({ request: deviceList.summary, response: deviceList.publicPayload })
      await create('/api/business-console/v1/master-data/business-partners', {
        organizationId,
        environmentId,
        code: customerCode,
        partnerType: 'customer',
        name: 'MAN-524 customer',
        partnerRoles: ['customer'],
        creditLimit: 100_000,
        creditCurrencyCode: 'CNY',
        idempotencyKey: `customer-${suffix}`,
      })
      await create('/api/business-console/v1/master-data/business-partners', {
        organizationId,
        environmentId,
        code: supplierCode,
        partnerType: 'supplier',
        name: 'MAN-524 supplier',
        partnerRoles: ['supplier'],
        creditLimit: 0,
        creditCurrencyCode: 'CNY',
        idempotencyKey: `supplier-${suffix}`,
      })
      for (const [code, name, materialType] of [
        [finishedSku, 'MAN-524 finished good', 'finished-goods'],
        [materialSku, 'MAN-524 raw material', 'raw-material'],
      ]) {
        await create('/api/business-console/v1/master-data/skus', {
          organizationId,
          environmentId,
          code,
          name,
          baseUomCode: uomCode,
          category: 'electronic',
          materialType,
          batchTrackingPolicy: 'none',
          serialTrackingPolicy: 'none',
          shelfLifePolicyCode: 'none',
          storageConditionCode: 'ambient',
          defaultBarcodeRuleCode: 'code128',
          qualityRequired: code === finishedSku,
          complianceTags: [],
          idempotencyKey: `sku-${code}`,
        })
      }
      const inspectionPlan = asRecord(
        await create('/api/business-console/v1/quality/inspection-plans', {
          organizationId,
          environmentId,
          planCode: `IP-M524-${suffix}`,
          category: 'operation',
          skuCode: finishedSku,
          partnerId: null,
          workCenterId: workCenterCode,
          deviceAssetId: null,
          documentType: 'operation-task',
          characteristics: [
            {
              characteristicCode: `ATTR-M524-${suffix}`,
              name: 'MAN-524 operation acceptance',
              method: 'visual',
              severity: 'major',
              required: true,
              samplingRule: '100-percent',
              characteristicType: 'attribute',
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
        {
          organizationId,
          environmentId,
          inspectionPlanId,
        },
      )
      await create('/api/business-console/v1/engineering/standard-operations', {
        organizationId,
        environmentId,
        operationCode,
        operationName: 'MAN-524 assembly',
        defaultWorkCenterCode: workCenterCode,
        standardSetupMinutes: 1,
        standardRunMinutes: operationDurationMinutes,
        controlKey: 'internal',
        requiresReporting: true,
        requiresQualityInspection: true,
        isOutsourced: false,
        idempotencyKey: `op-${suffix}`,
      })
      await create('/api/business-console/v1/engineering/engineering-boms/release', {
        organizationId,
        environmentId,
        bomCode: engineeringBomCode,
        revision: 'A',
        parentItemCode: finishedSku,
        effectiveDate: dateOnly(now),
        lines: [{ componentCode: materialSku, quantity: 1, unitOfMeasureCode: uomCode }],
        idempotencyKey: `ebom-${suffix}`,
      })
      const mbom = asRecord(
        await create('/api/business-console/v1/engineering/manufacturing-boms/release', {
          organizationId,
          environmentId,
          bomCode: manufacturingBomCode,
          revision: 'A',
          skuCode: finishedSku,
          engineeringBomCode,
          engineeringBomRevision: 'A',
          effectiveDate: dateOnly(now),
          materialLines: [
            { skuCode: materialSku, quantity: 1, unitOfMeasureCode: uomCode, scrapRate: 0 },
          ],
          recipeLines: [],
          idempotencyKey: `mbom-${suffix}`,
        }),
      )
      const routing = asRecord(
        await create('/api/business-console/v1/engineering/routings/release', {
          organizationId,
          environmentId,
          routingCode,
          revision: 'A',
          skuCode: finishedSku,
          effectiveDate: dateOnly(now),
          operations: [
            {
              sequence: 10,
              workCenterCode,
              operationCode,
              operationName: 'MAN-524 assembly',
              standardMinutes: operationDurationMinutes,
            },
          ],
          idempotencyKey: `routing-${suffix}`,
        }),
      )
      const mbomVersionId = textOf(mbom.versionId).trim()
      if (!mbomVersionId) {
        throw new TrackedSetupError(
          '#1024 / MAN-564',
          'MBOM release response did not expose data.versionId.',
        )
      }
      const routingVersionId = textOf(routing.versionId).trim()
      if (!routingVersionId) {
        throw new TrackedSetupError(
          '#1024 / MAN-564',
          'Routing release response did not expose data.versionId.',
        )
      }
      const productionVersion = asRecord(
        await create('/api/business-console/v1/engineering/production-versions', {
          organizationId,
          environmentId,
          skuCode: finishedSku,
          mbomVersionId,
          routingVersionId,
          validFrom: dateOnly(now),
          lotSizeMin: 1,
          lotSizeMax: 1_000,
          priority: 1,
          isDefault: true,
        }),
      )
      productionVersionId = textOf(productionVersion.productionVersionId ?? productionVersion)

      const approvalTemplateCode = 'erp-purchase-order-release'
      await create('/api/business-console/v1/approval/templates', {
        organizationId,
        environmentId,
        templateCode: approvalTemplateCode,
        documentType: 'purchase-order',
        version: 1,
        isActive: true,
        steps: [
          {
            stepNo: 1,
            stepName: 'MAN-524 procurement approval',
            approverType: principalType,
            approverRef: principalId,
            dueInHours: 24,
            completionPolicy: 'all',
          },
        ],
      })

      const purchaseOrderPath = '/api/business-console/v1/erp/procurement/purchase-orders'
      const purchaseOrderRequest = {
        organizationId,
        environmentId,
        purchaseOrderNo,
        supplierCode,
        siteCode: materialSiteCode,
        lines: [
          {
            lineNo: '1',
            skuCode: materialSku,
            uomCode,
            quantity: rawMaterialQuantity,
            unitPrice: 1,
            promisedDate: dateOnly(new Date(now.getTime() + 7 * 86_400_000)),
          },
        ],
        idempotencyKey: `purchase-order-${suffix}`,
      }
      const purchaseOrder = asRecord(await create(purchaseOrderPath, purchaseOrderRequest))
      const purchaseOrderReplay = asRecord(await create(purchaseOrderPath, purchaseOrderRequest))
      const purchaseOrderId = textOf(purchaseOrder.purchaseOrderId).trim()
      if (
        !purchaseOrderId ||
        textOf(purchaseOrderReplay.purchaseOrderId).trim() !== purchaseOrderId
      )
        throw new Error('Purchase-order replay did not return the original run-scoped order.')

      const approval = await pollRows(
        '/api/business-console/v1/approval/chains',
        {
          organizationId,
          environmentId,
          status: 'pending',
          documentType: 'purchase-order',
          documentId: purchaseOrderNo,
        },
        (row) => row.documentId === purchaseOrderNo && row.status === 'pending',
      )
      const approvalChainId = textOf(approval.match.chainId).trim()
      if (!approvalChainId)
        throw new Error(`Purchase order ${purchaseOrderNo} exposed no approval chain ID.`)
      const approvalDecision = asRecord(
        await create(
          `/api/business-console/v1/approval/chains/${encodeURIComponent(approvalChainId)}/steps/1/resolve`,
          {
            organizationId,
            environmentId,
            actorType: principalType,
            actorRef: principalId,
            decision: 'approve',
            comment: 'MAN-524 run-scoped raw-material procurement approval',
          },
        ),
      )
      await pollRows(
        purchaseOrderPath,
        { organizationId, environmentId, keyword: purchaseOrderNo },
        (row) =>
          row.purchaseOrderNo === purchaseOrderNo &&
          textOf(row.status).trim().toLowerCase() === 'released',
      )

      const wmsInboundPath = '/api/business-console/v1/wms/inbound-orders'
      const wmsInboundRequest = {
        organizationId,
        environmentId,
        inboundOrderNo,
        sourceDocumentType: 'purchase-order',
        sourceDocumentId: purchaseOrderNo,
        siteCode: materialSiteCode,
        lines: [
          {
            lineNo: '1',
            skuCode: materialSku,
            uomCode,
            receivedQuantity: rawMaterialQuantity,
            stagingLocationCode: 'LINE-SIDE',
            lotNo: rawMaterialLotNo,
            serialNo: null,
            qualityStatus: 'qualified',
            ownerType: 'company',
            ownerId: null,
          },
        ],
      }
      const wmsInbound = asRecord(await create(wmsInboundPath, wmsInboundRequest))
      const wmsInboundReplay = asRecord(await create(wmsInboundPath, wmsInboundRequest))
      const inboundOrderId = textOf(wmsInbound.inboundOrderId).trim()
      if (!inboundOrderId || textOf(wmsInboundReplay.inboundOrderId).trim() !== inboundOrderId)
        throw new Error('WMS inbound replay did not return the original run-scoped order.')

      const putawayPath = queryPath(
        `/api/business-console/v1/wms/inbound-orders/${encodeURIComponent(inboundOrderId)}/putaway-tasks`,
        { organizationId, environmentId },
      )
      const putawayRequest = {
        taskNo: putawayTaskNo,
        lineNo: '1',
        fromLocationCode: 'RECEIVING',
        toLocationCode: 'LINE-SIDE',
        quantity: rawMaterialQuantity,
      }
      const putaway = asRecord(await create(putawayPath, putawayRequest))
      const putawayReplay = asRecord(await create(putawayPath, putawayRequest))
      const warehouseTaskId = textOf(putaway.warehouseTaskId).trim()
      if (!warehouseTaskId || textOf(putawayReplay.warehouseTaskId).trim() !== warehouseTaskId)
        throw new Error('WMS putaway replay did not return the original run-scoped task.')

      const completeInboundPath = queryPath(
        `/api/business-console/v1/wms/inbound-orders/${encodeURIComponent(inboundOrderId)}/complete`,
        { organizationId, environmentId },
      )
      const completeInboundRequest = {
        idempotencyKey: `complete-inbound-${suffix}`,
        lines: [{ lineNo: '1', lotNo: rawMaterialLotNo }],
      }
      const completedInbound = asRecord(await create(completeInboundPath, completeInboundRequest))
      const completedInboundReplay = asRecord(
        await create(completeInboundPath, completeInboundRequest),
      )
      const completedInboundRequestId = textOf(completedInbound.requestId).trim()
      const completeInboundReplayConfirmed =
        Boolean(completedInboundRequestId) &&
        textOf(completedInboundReplay.requestId).trim() === completedInboundRequestId
      if (!completeInboundReplayConfirmed)
        throw new Error(
          'WMS inbound completion replay did not return the original movement request.',
        )

      const receivedPurchaseOrder = await pollRows(
        purchaseOrderPath,
        { organizationId, environmentId, keyword: purchaseOrderNo },
        (row) =>
          row.purchaseOrderNo === purchaseOrderNo &&
          (Array.isArray(row.lines) ? row.lines : [])
            .map(asRecord)
            .some((line) => line.lineNo === '1' && line.receivedQuantity === rawMaterialQuantity),
      )
      const rawMaterialAvailability = await pollData(
        '/api/business-console/v1/inventory/availability',
        {
          organizationId,
          environmentId,
          skuCode: materialSku,
          uomCode,
          siteCode: materialSiteCode,
          locationCode: 'LINE-SIDE',
          lotNo: rawMaterialLotNo,
          qualityStatus: 'unrestricted',
          ownerType: 'company',
        },
        (availability) => availability.availableQuantity === rawMaterialQuantity,
      )
      rawMaterialSupplyEvidence = {
        purchaseOrder: publicJson(receivedPurchaseOrder.match),
        approval: publicJson({ chain: approval.match, decision: approvalDecision }),
        wms: publicJson({
          inboundOrderId,
          warehouseTaskId,
          completedInbound,
          completedInboundReplay,
        }),
        inventory: publicJson(rawMaterialAvailability.data),
        replayConfirmed: completeInboundReplayConfirmed,
      }
      setup.push({
        phase: 'raw-material-supply',
        conclusion: 'runtime-confirmed',
        response: rawMaterialSupplyEvidence,
      })
    } catch (error) {
      prerequisitesReady = false
      const blockedReason = safeText(error instanceof Error ? error.message : error)
      const responsibilityIssue =
        error instanceof TrackedSetupError
          ? error.responsibilityIssue
          : error instanceof PublicCallError &&
              error.path === '/api/business-console/v1/engineering/production-versions'
            ? '#1024 / MAN-564'
            : 'unattributed / requires follow-up issue'
      setup.push({
        phase: 'public-prerequisites',
        conclusion: 'gap',
        responsibilityIssue,
        error: blockedReason,
      })
      for (const node of requiredNodes) {
        const entry = evidence.get(node)!
        if (entry.conclusion !== 'not-verified') continue
        record({
          ...entry,
          responseOrLog: { blockedBy: 'public-prerequisites', error: blockedReason },
          demoWording: `${node}: this run was blocked before the business chain by the public-prerequisite gap tracked in ${responsibilityIssue}.`,
          responsibilityIssue,
        })
      }
    }

    let salesOrderCreated = false
    if (prerequisitesReady) {
      try {
        await create('/api/business-console/v1/erp/sales/quotations', {
          organizationId,
          environmentId,
          quotationNo,
          customerCode,
          expiresOn: dateOnly(new Date(now.getTime() + 30 * 86_400_000)),
          lines: [
            {
              lineNo: '1',
              skuCode: finishedSku,
              uomCode,
              quantity: finishedGoodsQuantity,
              unitPrice: 100,
              requiredDate: dateOnly(new Date(now.getTime() + 7 * 86_400_000)),
            },
          ],
          idempotencyKey: `quotation-${suffix}`,
        })
        await create(
          `/api/business-console/v1/erp/sales/quotations/${encodeURIComponent(quotationNo)}/approve`,
          {
            organizationId,
            environmentId,
          },
        )
        const salesOrder = await call('POST', '/api/business-console/v1/erp/sales/sales-orders', {
          organizationId,
          environmentId,
          salesOrderNo,
          quotationNo,
          siteCode,
          idempotencyKey: `sales-order-${suffix}`,
        })
        setup.push({ request: salesOrder.summary, response: salesOrder.publicPayload })
        salesOrderCreated = true
      } catch (error) {
        markFailure('sales-order-demand-source', error)
      }
    }

    let demandRow: JsonRecord | null = null
    if (salesOrderCreated) {
      try {
        const observed = await pollRows(
          '/api/business-console/v1/planning/demands',
          { organizationId, environmentId },
          (row) => row.sourceReference === salesOrderNo,
        )
        demandRow = observed.match
        record({
          node: 'sales-order-demand-source',
          sourceObject: salesOrderNo,
          downstreamObject: textOf(observed.match.demandSourceId ?? observed.match.sourceReference),
          stableKey: salesOrderNo,
          automationMode: 'automatic',
          request: observed.call.summary,
          responseOrLog: publicJson(observed.match) as JsonRecord,
          conclusion: 'runtime-confirmed',
          demoWording:
            'ERP release crossed a process boundary through Redis and appeared as a Planning demand for this exact sales order.',
          responsibilityIssue: '#958 (closed)',
        })
      } catch (error) {
        markFailure(
          'sales-order-demand-source',
          error,
          'automatic',
          '#958 (closed; regression evidence failed)',
        )
      }
    }

    let suggestion: JsonRecord | null = null
    if (demandRow) {
      try {
        const mrp = await call('POST', '/api/business-console/v1/planning/mrp-runs', {
          organizationId,
          environmentId,
          horizonStart: dateOnly(new Date(now.getTime() - 86_400_000)),
          horizonEnd: dateOnly(new Date(now.getTime() + 30 * 86_400_000)),
        })
        const runId = textOf(asRecord(dataOf(mrp.payload)).runId)
        const pegging = await call(
          'GET',
          queryPath(
            `/api/business-console/v1/planning/mrp-runs/${encodeURIComponent(runId)}/pegging`,
            {
              organizationId,
              environmentId,
            },
          ),
        )
        const peggingRow = rowsOf(pegging.payload).find(
          (row) => row.demandSourceReference === salesOrderNo,
        )
        const observed = await pollRows(
          '/api/business-console/v1/planning/suggestions',
          { organizationId, environmentId },
          (row) =>
            row.runId === runId &&
            row.suggestionType === 'planned-work-order' &&
            row.skuCode === finishedSku,
        )
        if (!peggingRow)
          throw new Error(`MRP run ${runId} did not expose pegging for ${salesOrderNo}.`)
        suggestion = observed.match
        record({
          node: 'demand-source-mrp-suggestion',
          sourceObject: textOf(demandRow.demandSourceId ?? salesOrderNo),
          downstreamObject: textOf(suggestion.suggestionId),
          stableKey: `${salesOrderNo} -> ${textOf(suggestion.suggestionId)}`,
          automationMode: 'manual',
          request: mrp.summary,
          responseOrLog: {
            mrp: mrp.publicPayload,
            pegging: publicJson(peggingRow),
            suggestion: publicJson(suggestion),
          },
          conclusion: 'runtime-confirmed',
          demoWording:
            'A public MRP run retained the exact sales-order demand in pegging and produced a planned work-order suggestion.',
          responsibilityIssue: '#965',
        })
      } catch (error) {
        markFailure('demand-source-mrp-suggestion', error, 'manual')
      }
    }

    let workOrderId = ''
    let operationTask: JsonRecord | null = null
    if (suggestion) {
      try {
        const accepted = await call(
          'POST',
          queryPath(
            `/api/business-console/v1/planning/suggestions/${encodeURIComponent(textOf(suggestion.suggestionId))}/accept`,
            { organizationId, environmentId },
          ),
          {
            downstreamService: 'BusinessMes',
            downstreamDocumentType: 'WorkOrder',
            downstreamDocumentId: null,
            idempotencyKey: `accept-wo-${suffix}`,
          },
        )
        workOrderId = textOf(asRecord(dataOf(accepted.payload)).downstreamDocumentId)
        if (!workOrderId)
          throw new Error('Planning acceptance returned no MES downstream document ID.')
        const detail = await fetchWorkOrder(workOrderId)
        const workOrder = asRecord(dataOf(detail.payload))
        if (asRecord(workOrder.sourcePlanReference).sourceDemandReference !== salesOrderNo) {
          throw new Error(
            `MES work order ${workOrderId} did not expose ${salesOrderNo} as its source demand reference.`,
          )
        }
        record({
          node: 'mrp-suggestion-mes-work-order',
          sourceObject: textOf(suggestion.suggestionId),
          downstreamObject: workOrderId,
          stableKey: `${salesOrderNo} -> ${workOrderId}`,
          automationMode: 'manual',
          request: accepted.summary,
          responseOrLog: publicJson(workOrder) as JsonRecord,
          conclusion: 'runtime-confirmed',
          demoWording:
            'Accepting the run-scoped planned-work-order suggestion created a real MES work order that preserved the sales-order demand reference.',
          responsibilityIssue: '#965',
        })
      } catch (error) {
        markFailure('mrp-suggestion-mes-work-order', error, 'manual')
      }
    }

    if (workOrderId) {
      try {
        await call(
          'POST',
          queryPath(
            `/api/business-console/v1/mes/work-orders/${encodeURIComponent(workOrderId)}/release`,
            { organizationId, environmentId },
          ),
          {
            confirmWarnings: true,
            idempotencyKey: `release-wo-${suffix}`,
          },
        )
        const releasedDetail = await fetchWorkOrder(workOrderId)
        const releasedWorkOrder = asRecord(dataOf(releasedDetail.payload))
        operationTask =
          (Array.isArray(releasedWorkOrder.operationTasks)
            ? releasedWorkOrder.operationTasks
            : []
          ).map(asRecord)[0] ?? null
        if (!operationTask) {
          throw new Error(`Released MES work order ${workOrderId} exposed no operation task.`)
        }
      } catch (error) {
        markFailure('mes-work-order-schedule-plan', error, 'manual')
      }
    }

    let scheduleReleased = false
    if (workOrderId && operationTask && deviceAssetId) {
      try {
        const taskId = textOf(operationTask.operationTaskId)
        const runtimeObservedAt = new Date()
        const runtimeState = await call('POST', '/api/business-console/v1/telemetry/samples', {
          organizationId,
          environmentId,
          deviceAssetId,
          tagKey: 'runtime.state',
          bucketStartUtc: new Date(runtimeObservedAt.getTime() - 1_000).toISOString(),
          bucketEndUtc: runtimeObservedAt.toISOString(),
          sampleCount: 1,
          minValue: 1,
          maxValue: 1,
          averageValue: 1,
          sourceSequence: `main-chain-available-${suffix}`,
          sourceSystem: 'leader-main-chain',
          sourceConnector: 'business-gateway',
          deviceState: 'available',
          stateOccurredAtUtc: runtimeObservedAt.toISOString(),
          firstValue: 1,
          lastValue: 1,
        })
        const runtimeStateData = asRecord(dataOf(runtimeState.payload))
        if (!textOf(runtimeStateData.deviceStateSnapshotId).trim()) {
          throw new Error(`IIoT did not persist a state snapshot for device ${deviceAssetId}.`)
        }
        const deviceDetail = await call(
          'GET',
          queryPath(
            `/api/business-console/v1/equipment/devices/${encodeURIComponent(deviceAssetId)}`,
            { organizationId, environmentId },
          ),
        )
        const currentState = asRecord(asRecord(dataOf(deviceDetail.payload)).currentState)
        if (
          textOf(currentState.deviceAssetId).trim() !== deviceAssetId ||
          textOf(currentState.currentState).trim().toLowerCase() !== 'available' ||
          currentState.isSourceFresh !== true
        ) {
          throw new Error(
            `Equipment detail did not expose fresh Available state for device ${deviceAssetId}.`,
          )
        }
        // The five-minute rush operation must start within the 60-minute freshness window; the later horizon remains fail closed.
        const horizonStart = new Date(runtimeObservedAt.getTime() + 60_000)
        const horizonEnd = new Date(horizonStart.getTime() + 8 * 3_600_000)
        const plan = await call('POST', '/api/business-console/v1/scheduling/plans', {
          problem: {
            contractVersion: 1,
            problemId: `MAN524-${suffix}`,
            organizationId,
            environmentId,
            horizonStartUtc: horizonStart.toISOString(),
            horizonEndUtc: horizonEnd.toISOString(),
            orders: [
              {
                orderId: workOrderId,
                skuCode: finishedSku,
                quantity: finishedGoodsQuantity,
                dueUtc: horizonEnd.toISOString(),
                priority: 1,
                isRush: true,
                operations: [
                  {
                    operationId: taskId,
                    operationSequence: 10,
                    predecessorOperationIds: [],
                    durationMinutes: operationDurationMinutes,
                    requiredCapabilityCode: operationCode,
                    eligibleResourceIds: [deviceAssetId],
                    primaryResourceId: deviceAssetId,
                    earliestStartUtc: horizonStart.toISOString(),
                    dueUtc: horizonEnd.toISOString(),
                    priority: 1,
                    isRush: true,
                    splitPolicy: 'nonSplittable',
                    materialReadyUtc: horizonStart.toISOString(),
                    qualityBlockReason: null,
                    sourceReference: salesOrderNo,
                    setupMinutes: 1,
                    toolingAvailable: true,
                  },
                ],
              },
            ],
            resources: [
              {
                resourceId: deviceAssetId,
                workCenterId: workCenterCode,
                capabilityCodes: [operationCode],
                capacityUnits: 1,
                calendarId: `CAL-M524-${suffix}`,
                sortKey: deviceAssetId,
              },
            ],
            calendars: [
              {
                calendarId: `CAL-M524-${suffix}`,
                shiftWindows: [
                  {
                    startUtc: horizonStart.toISOString(),
                    endUtc: horizonEnd.toISOString(),
                    reasonCode: 'MAN524',
                  },
                ],
              },
            ],
            unavailabilityWindows: [],
            materialReadiness: [
              {
                scopeType: 'operation',
                scopeId: taskId,
                materialReadyUtc: horizonStart.toISOString(),
                isReady: true,
                reasonCodes: [],
              },
            ],
            qualityBlocks: [],
            lockedAssignments: [],
          },
        })
        const planData = asRecord(dataOf(plan.payload))
        const planId = textOf(planData.planId)
        if (!planId) throw new Error('Scheduling plan creation returned no planId.')
        record({
          node: 'mes-work-order-schedule-plan',
          sourceObject: workOrderId,
          downstreamObject: planId,
          stableKey: `${salesOrderNo} -> ${workOrderId} -> ${workCenterCode} -> ${deviceAssetId} -> ${planId}`,
          automationMode: 'manual',
          request: plan.summary,
          responseOrLog: {
            plan: plan.publicPayload,
            rawMaterialSupply: rawMaterialSupplyEvidence,
            deviceIdentity: {
              deviceCode,
              deviceAssetId,
              workCenterCode,
            },
            runtimeState: runtimeState.publicPayload,
            equipmentAudit: deviceDetail.publicPayload,
          },
          conclusion: 'runtime-confirmed',
          demoWording:
            'The public scheduling contract created a plan whose order, operation, and source reference all belong to the same sales-order chain.',
          responsibilityIssue: '#1040',
        })
        const released = await call(
          'POST',
          queryPath(
            `/api/business-console/v1/scheduling/plans/${encodeURIComponent(planId)}/release`,
            {
              organizationId,
              environmentId,
            },
          ),
        )
        await page.waitForTimeout(1_500)
        const detail = await fetchWorkOrder(workOrderId)
        const scheduledTask = (
          asRecord(dataOf(detail.payload)).operationTasks as unknown[] | undefined
        )
          ?.map(asRecord)
          .find((row) => row.operationTaskId === taskId)
        if (!scheduledTask?.plannedStartUtc && !scheduledTask?.scheduledAtUtc) {
          throw new Error(
            `MES operation ${taskId} did not expose the released schedule assignment.`,
          )
        }
        scheduleReleased = true
        operationTask = scheduledTask
        record({
          node: 'schedule-release-mes-execution',
          sourceObject: planId,
          downstreamObject: taskId,
          stableKey: `${planId} -> ${taskId}`,
          automationMode: 'automatic',
          request: released.summary,
          responseOrLog: publicJson(scheduledTask) as JsonRecord,
          conclusion: 'runtime-confirmed',
          demoWording:
            'Scheduling release crossed Redis into MES and updated the exact operation task rather than a similarly named seeded task.',
          responsibilityIssue: '#965',
        })
      } catch (error) {
        if (evidence.get('mes-work-order-schedule-plan')?.conclusion !== 'runtime-confirmed') {
          markFailure('mes-work-order-schedule-plan', error, 'manual', '#1040')
        } else {
          markFailure('schedule-release-mes-execution', error, 'automatic')
        }
      }
    }

    let productionReportId = ''
    let operationTaskId = ''
    if (workOrderId && operationTask) {
      try {
        const taskId = textOf(operationTask.operationTaskId)
        operationTaskId = taskId
        await call(
          'POST',
          queryPath(
            `/api/business-console/v1/mes/operation-tasks/${encodeURIComponent(taskId)}/start`,
            { organizationId, environmentId },
          ),
          {
            reasonCode: scheduleReleased ? 'scheduled-execution' : 'manual-evidence-transition',
            idempotencyKey: `start-task-${suffix}`,
          },
        )
        const costBasisCall = await call(
          'GET',
          queryPath('/api/business-console/v1/mes/work-orders', {
            organizationId,
            environmentId,
            keyword: workOrderId,
            take: 100,
          }),
        )
        const costBasisWorkOrder = rowsOf(costBasisCall.payload).find(
          (row) => textOf(row.workOrderId) === workOrderId,
        )
        const costBasisTasks =
          costBasisWorkOrder && Array.isArray(costBasisWorkOrder.operationTasks)
            ? costBasisWorkOrder.operationTasks.map(asRecord)
            : []
        const costBasisTask = costBasisTasks.find((row) => textOf(row.operationTaskId) === taskId)
        const durationTicks = Number(costBasisTask?.durationTicks ?? 0)
        const theoreticalRatePerHour =
          durationTicks > 0
            ? Number(costBasisWorkOrder?.quantity ?? 0) / (durationTicks / ticksPerHour)
            : 0
        const expectedLaborHours =
          theoreticalRatePerHour > 0 ? finishedGoodsQuantity / theoreticalRatePerHour : 0
        const expectedLaborCost = expectedLaborHours * workCenterHourlyRate
        if (
          !costBasisWorkOrder ||
          !costBasisTask ||
          Number(costBasisWorkOrder.quantity ?? 0) !== finishedGoodsQuantity ||
          textOf(costBasisTask.workCenterId) !== workCenterCode ||
          durationTicks !== operationDurationMinutes * 60 * 10_000_000 ||
          theoreticalRatePerHour !== expectedTheoreticalRatePerHour ||
          expectedLaborCost !== finishedGoodsCapitalizedCost
        ) {
          throw new Error(
            `MES did not publicly expose the deterministic costing basis for ${workOrderId}/${taskId}: ${safeText(JSON.stringify(costBasisWorkOrder))}.`,
          )
        }
        const productionReportRequest = {
          organizationId,
          environmentId,
          workOrderId,
          operationTaskId: taskId,
          goodQuantity: finishedGoodsQuantity,
          scrapQuantity: 0,
          completesOperation: true,
          reportedAtUtc: new Date().toISOString(),
          idempotencyKey: `report-${suffix}`,
          consumedMaterialLots: [],
          reworkQuantity: 0,
          producedLotNo,
        }
        const report = await call(
          'POST',
          '/api/business-console/v1/mes/production-reports',
          productionReportRequest,
        )
        const reportData = asRecord(dataOf(report.payload))
        productionReportId = textOf(reportData.productionReportId)
        const reportReplay = await call(
          'POST',
          '/api/business-console/v1/mes/production-reports',
          productionReportRequest,
        )
        const reportReplayData = asRecord(dataOf(reportReplay.payload))
        const reportReplayId = textOf(reportReplayData.productionReportId)
        if (!productionReportId || reportReplayId !== productionReportId) {
          throw new Error(
            `Production-report replay returned ${reportReplayId || 'no id'} instead of ${productionReportId || 'no original id'}.`,
          )
        }
        record({
          node: 'mes-task-production-report',
          sourceObject: taskId,
          downstreamObject: productionReportId || textOf(reportData.reportNo),
          stableKey: `${salesOrderNo} -> ${workOrderId} -> ${producedLotNo}`,
          automationMode: scheduleReleased ? 'mixed' : 'manual',
          request: report.summary,
          responseOrLog: {
            original: report.publicPayload,
            replay: reportReplay.publicPayload,
            replayConfirmed: true,
            publicCostBasis: {
              workOrder: publicJson(costBasisWorkOrder),
              operationTask: publicJson(costBasisTask),
              derivedTheoreticalRatePerHour: theoreticalRatePerHour,
              derivedLaborHours: expectedLaborHours,
              configuredHourlyRate: workCenterHourlyRate,
              derivedLaborCost: expectedLaborCost,
            },
          },
          conclusion: 'runtime-confirmed',
          demoWording:
            'The exact MES task was released, started, and reported through public HTTP; replaying the same idempotency key returned the same report while preserving the run-scoped produced lot.',
          responsibilityIssue: '#965',
        })
      } catch (error) {
        markFailure('mes-task-production-report', error, scheduleReleased ? 'mixed' : 'manual')
      }
    }

    if (productionReportId && operationTaskId && inspectionPlanId) {
      try {
        const qualityPath = '/api/business-console/v1/quality/inspection-tasks'
        const qualityQuery = { organizationId, environmentId, skuCode: finishedSku, take: 100 }
        const qualityDeadline = Date.now() + 45_000
        let qualityCall: Awaited<ReturnType<typeof call>> | null = null
        let matchingTasks: JsonRecord[] = []
        do {
          qualityCall = await call('GET', queryPath(qualityPath, qualityQuery))
          matchingTasks = rowsOf(qualityCall.payload).filter(
            (row) =>
              textOf(row.sourceDocumentId) === workOrderId &&
              textOf(row.sourceDocumentLineId) === operationTaskId &&
              textOf(row.skuCode) === finishedSku,
          )
          if (matchingTasks.length === 1) break
          if (matchingTasks.length > 1) {
            throw new Error(
              `Quality created ${matchingTasks.length} inspection tasks for ${workOrderId}/${operationTaskId}; expected exactly one.`,
            )
          }
          await page.waitForTimeout(1_000)
        } while (Date.now() < qualityDeadline)
        if (!qualityCall || matchingTasks.length !== 1) {
          throw new Error(
            `Timed out waiting for the unique run-scoped Quality task for ${workOrderId}/${operationTaskId}.`,
          )
        }
        const inspectionTask = matchingTasks[0]!
        record({
          node: 'production-report-quality',
          sourceObject: productionReportId,
          downstreamObject: textOf(inspectionTask.inspectionTaskId),
          stableKey: `${inspectionPlanId} -> ${workOrderId} -> ${operationTaskId} -> ${textOf(inspectionTask.inspectionTaskId)}`,
          automationMode: 'automatic',
          request: qualityCall.summary,
          responseOrLog: {
            inspectionPlanId,
            workOrderId,
            operationTaskId,
            skuCode: finishedSku,
            workCenterId: workCenterCode,
            inspectionTask: publicJson(inspectionTask),
          },
          conclusion: 'runtime-confirmed',
          demoWording:
            'Quality matched the active run-scoped operation plan and exposed exactly one task for this work order and operation task.',
          responsibilityIssue: '#1046',
        })
      } catch (error) {
        markFailure('production-report-quality', error)
      }
    }

    let receiptRequestNo = ''
    if (productionReportId) {
      try {
        const receipt = await call(
          'POST',
          '/api/business-console/v1/mes/finished-goods-receipt-requests',
          {
            organizationId,
            environmentId,
            workOrderId,
            skuId: finishedSku,
            quantity: finishedGoodsQuantity,
            uomCode,
            requestedAtUtc: new Date().toISOString(),
            idempotencyKey: `fg-receipt-${suffix}`,
            producedLotNo,
          },
        )
        receiptRequestNo = textOf(asRecord(dataOf(receipt.payload)).requestNo)
        record({
          node: 'report-finished-goods-receipt',
          sourceObject: productionReportId,
          downstreamObject: receiptRequestNo,
          stableKey: `${productionReportId} -> ${receiptRequestNo} -> ${producedLotNo}`,
          automationMode: 'manual',
          request: receipt.summary,
          responseOrLog: receipt.publicPayload,
          conclusion: 'runtime-confirmed',
          demoWording:
            'A public finished-goods receipt request used the exact work order and authoritative produced lot while omitting client-supplied unit cost so ERP capitalization remained authoritative.',
          responsibilityIssue: null,
        })
      } catch (error) {
        markFailure('report-finished-goods-receipt', error, 'manual')
      }
    }

    if (receiptRequestNo) {
      try {
        const availability = await pollData(
          '/api/business-console/v1/inventory/availability',
          {
            organizationId,
            environmentId,
            skuCode: finishedSku,
            uomCode,
            siteCode: finishedGoodsSiteCode,
            locationCode: finishedGoodsLocationCode,
            lotNo: producedLotNo,
          },
          (data) => Number(data.onHandQuantity ?? 0) > 0,
          120_000,
        )
        record({
          node: 'finished-goods-receipt-inventory-posting',
          sourceObject: receiptRequestNo,
          downstreamObject: producedLotNo,
          stableKey: `${receiptRequestNo} -> ${finishedGoodsSiteCode}/${finishedGoodsLocationCode}/${producedLotNo}`,
          automationMode: 'automatic',
          request: availability.call.summary,
          responseOrLog: {
            poll: availability.poll,
            availability: publicJson(availability.data),
          },
          conclusion: 'runtime-confirmed',
          demoWording:
            'MES receipt posting crossed Redis into Inventory and produced positive on-hand for the authoritative lot.',
          responsibilityIssue: '#965',
        })
      } catch (error) {
        markFailure('finished-goods-receipt-inventory-posting', error)
      }
    }

    if (receiptRequestNo) {
      try {
        const capitalizedReceipt = await pollRows(
          '/api/business-console/v1/mes/finished-goods-receipt-requests',
          {
            organizationId,
            environmentId,
            workOrderId,
            take: 100,
          },
          (row) =>
            textOf(row.requestNo) === receiptRequestNo &&
            textOf(row.workOrderId) === workOrderId &&
            textOf(row.skuId) === finishedSku &&
            textOf(row.producedLotNo) === producedLotNo &&
            Number(row.quantity ?? 0) === finishedGoodsQuantity &&
            Number(row.unitCost ?? 0) === finishedGoodsUnitCost,
          120_000,
        )
        const terminalStatuses = new Set(['posted', 'postingfailed', 'qualityrestricted'])
        const inventoryLink = await pollData(
          `/api/business-console/v1/mes/finished-goods-receipt-requests/${encodeURIComponent(receiptRequestNo)}/inventory-link`,
          { organizationId, environmentId, workOrderId },
          (data) => {
            const status = textOf(data.linkStatus).trim().toLowerCase()
            return terminalStatuses.has(status)
          },
        )
        const link = inventoryLink.data
        const movements = Array.isArray(link.movements) ? link.movements.map(asRecord) : []
        const balances = Array.isArray(link.balances) ? link.balances.map(asRecord) : []
        const sourceMovement = movements.find(
          (movement) =>
            textOf(movement.sourceService) === 'business-mes' &&
            textOf(movement.sourceDocumentId) === receiptRequestNo &&
            textOf(movement.sourceDocumentLineId) === workOrderId &&
            textOf(movement.skuCode) === finishedSku &&
            textOf(movement.siteCode) === finishedGoodsSiteCode &&
            textOf(movement.locationCode) === finishedGoodsLocationCode &&
            textOf(movement.lotNo) === producedLotNo &&
            Number(movement.quantity ?? 0) === finishedGoodsQuantity,
        )
        const sourceBalance = balances.find(
          (balance) =>
            textOf(balance.skuCode) === finishedSku &&
            textOf(balance.siteCode) === finishedGoodsSiteCode &&
            textOf(balance.locationCode) === finishedGoodsLocationCode &&
            textOf(balance.lotNo) === producedLotNo &&
            Number(balance.onHandQuantity ?? 0) === finishedGoodsQuantity &&
            Number(balance.ledgerVersion ?? 0) > 0,
        )
        const mesReceipt = capitalizedReceipt.match
        const publicDerivedCapitalizedCost =
          Number(mesReceipt.unitCost ?? 0) * Number(sourceMovement?.quantity ?? 0)
        const hasExactInventoryLink =
          textOf(link.linkStatus).trim().toLowerCase() === 'posted' &&
          link.isInventoryLinkEstablished === true &&
          textOf(link.requestNo) === receiptRequestNo &&
          textOf(link.workOrderId) === workOrderId &&
          textOf(link.producedLotNo) === producedLotNo &&
          textOf(link.sourceService) === 'business-mes' &&
          textOf(link.sourceDocumentId) === receiptRequestNo &&
          textOf(link.sourceDocumentLineId) === workOrderId &&
          Number(link.requestedQuantity ?? 0) === finishedGoodsQuantity &&
          Number(link.postedQuantity ?? 0) === finishedGoodsQuantity &&
          Number(mesReceipt.unitCost ?? 0) === finishedGoodsUnitCost &&
          publicDerivedCapitalizedCost === finishedGoodsCapitalizedCost &&
          Boolean(sourceMovement) &&
          Boolean(sourceBalance)
        if (!hasExactInventoryLink || !sourceMovement || !sourceBalance) {
          throw new Error(
            `${receiptRequestNo} returned an incomplete Inventory source link: ${safeText(JSON.stringify(link))}.`,
          )
        }
        record({
          node: 'inventory-produced-lot-fulfillment-lookup',
          sourceObject: receiptRequestNo,
          downstreamObject: textOf(sourceMovement.movementId),
          stableKey: `${receiptRequestNo} -> ${workOrderId} -> ${producedLotNo} -> ${textOf(sourceMovement.movementId)}`,
          automationMode: 'automatic',
          request: inventoryLink.call.summary,
          responseOrLog: {
            poll: inventoryLink.poll,
            capitalizedReceiptPoll: capitalizedReceipt.poll,
            mesReceiptCost: publicJson(mesReceipt),
            movementId: textOf(sourceMovement.movementId),
            movementQuantity: sourceMovement.quantity ?? null,
            publicDerivedCapitalizedCost,
            inventoryValuationDisclosure:
              'The public source movement exposes exact quantity and lineage but not unitCost or movementAmount; no hidden valuation field is fabricated.',
            balanceLedgerVersion: sourceBalance.ledgerVersion ?? null,
            link: publicJson(link),
          },
          conclusion: 'runtime-confirmed',
          demoWording:
            'Public evidence links report labor accumulation -> ERP capitalization -> MES unit cost -> Inventory posting with the same work order, receipt, lot, exact unit cost, and exact posted quantity.',
          responsibilityIssue: null,
        })
      } catch (error) {
        markFailure('inventory-produced-lot-fulfillment-lookup', error)
      }
    }

    let wmsOutboundId = ''
    if (salesOrderCreated) {
      try {
        const delivery = await call('POST', '/api/business-console/v1/erp/sales/delivery-orders', {
          organizationId,
          environmentId,
          deliveryOrderNo,
          salesOrderNo,
          lines: [{ salesOrderLineNo: '1', quantity: finishedGoodsQuantity }],
          idempotencyKey: `delivery-${suffix}`,
        })
        record({
          node: 'sales-order-delivery-order',
          sourceObject: salesOrderNo,
          downstreamObject: deliveryOrderNo,
          stableKey: `${salesOrderNo} -> ${deliveryOrderNo}`,
          automationMode: 'manual',
          request: delivery.summary,
          responseOrLog: delivery.publicPayload,
          conclusion: 'runtime-confirmed',
          demoWording:
            'The delivery was released from the exact run-scoped sales order through the public ERP facade.',
          responsibilityIssue: '#965',
        })
        const outbound = await pollRows(
          '/api/business-console/v1/wms/outbound-orders',
          { organizationId, environmentId, keyword: deliveryOrderNo, take: 100 },
          (row) => row.outboundOrderNo === deliveryOrderNo,
        )
        wmsOutboundId = textOf(outbound.match.outboundOrderId)
        record({
          node: 'delivery-order-wms-outbound',
          sourceObject: deliveryOrderNo,
          downstreamObject: textOf(outbound.match.outboundOrderNo ?? wmsOutboundId),
          stableKey: `${deliveryOrderNo} -> ${textOf(outbound.match.outboundOrderNo)}`,
          automationMode: 'automatic',
          request: outbound.call.summary,
          responseOrLog: publicJson(outbound.match) as JsonRecord,
          conclusion: 'runtime-confirmed',
          demoWording:
            'ERP delivery release crossed Redis into WMS and created the matching outbound order.',
          responsibilityIssue: '#965',
        })
      } catch (error) {
        if (evidence.get('sales-order-delivery-order')?.conclusion !== 'runtime-confirmed') {
          markFailure('sales-order-delivery-order', error, 'manual')
        } else {
          markFailure('delivery-order-wms-outbound', error)
        }
      }
    }

    if (wmsOutboundId) {
      try {
        const completed = await call(
          'POST',
          queryPath(
            `/api/business-console/v1/wms/outbound-orders/${encodeURIComponent(wmsOutboundId)}/complete`,
            { organizationId, environmentId },
          ),
          {
            packReviewNo: `PACK-${suffix}`,
            passed: true,
            idempotencyKey: `complete-outbound-${suffix}`,
          },
        )
        const delivery = await pollRows(
          '/api/business-console/v1/erp/sales/delivery-orders',
          { organizationId, environmentId, keyword: deliveryOrderNo, take: 100 },
          (row) => row.deliveryOrderNo === deliveryOrderNo && row.status === 'completed',
          60_000,
        )
        record({
          node: 'wms-completed-erp-delivery-status',
          sourceObject: wmsOutboundId,
          downstreamObject: deliveryOrderNo,
          stableKey: `${wmsOutboundId} -> ${deliveryOrderNo}`,
          automationMode: 'automatic',
          request: completed.summary,
          responseOrLog: publicJson(delivery.match) as JsonRecord,
          conclusion: 'runtime-confirmed',
          demoWording:
            'Completing the WMS outbound order crossed Redis back into ERP and completed the matching delivery.',
          responsibilityIssue: '#971 (closed)',
        })
        const receivable = await pollRows(
          '/api/business-console/v1/erp/finance/receivables',
          { organizationId, environmentId, keyword: deliveryOrderNo, take: 100 },
          (row) => row.sourceDocumentNo === deliveryOrderNo,
          60_000,
        )
        const receivableNo = textOf(
          receivable.match.receivableNo ?? receivable.match.accountReceivableNo,
        )
        record({
          node: 'wms-completed-account-receivable',
          sourceObject: deliveryOrderNo,
          downstreamObject: receivableNo,
          stableKey: `${deliveryOrderNo} -> ${receivableNo}`,
          automationMode: 'automatic',
          request: receivable.call.summary,
          responseOrLog: publicJson(receivable.match) as JsonRecord,
          conclusion: 'runtime-confirmed',
          demoWording:
            'The completed delivery produced a public receivable carrying the same delivery source key.',
          responsibilityIssue: '#971 (closed)',
        })
        const voucher = await pollRows(
          '/api/business-console/v1/erp/finance/vouchers',
          { organizationId, environmentId, keyword: receivableNo, take: 100 },
          (row) => row.voucherNo === `JV-AR-${receivableNo}`,
          60_000,
        )
        record({
          node: 'account-receivable-voucher',
          sourceObject: receivableNo,
          downstreamObject: textOf(voucher.match.voucherNo ?? voucher.match.journalVoucherNo),
          stableKey: `${receivableNo} -> ${textOf(voucher.match.voucherNo ?? voucher.match.journalVoucherNo)}`,
          automationMode: 'automatic',
          request: voucher.call.summary,
          responseOrLog: publicJson(voucher.match) as JsonRecord,
          conclusion: 'runtime-confirmed',
          demoWording:
            'The receivable generated a public finance voucher tied to the same receivable number.',
          responsibilityIssue: '#971 (closed)',
        })
      } catch (error) {
        const pending = [
          'wms-completed-erp-delivery-status',
          'wms-completed-account-receivable',
          'account-receivable-voucher',
        ] as const
        const node = pending.find((name) => evidence.get(name)?.conclusion !== 'runtime-confirmed')
        if (node) markFailure(node, error)
      }
    }
  } finally {
    const entries = requiredNodes.map((node) => evidence.get(node)!)
    await writeFile(
      evidencePath!,
      JSON.stringify(
        {
          issue: 'Linear MAN-524 / GitHub #965',
          generatedAtUtc: new Date().toISOString(),
          runSuffix: suffix,
          organizationId,
          environmentId,
          salesOrderNo,
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
    'Every main-chain node must be runtime-confirmed through public BusinessGateway evidence.',
  ).toEqual([])
  expect(entries.some((entry) => entry.conclusion === 'runtime-confirmed')).toBe(true)
})
