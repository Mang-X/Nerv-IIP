import {
  expect,
  test,
  type APIResponse,
  type BrowserContext,
  type Page,
  type Response,
} from '@playwright/test'
import { createHash } from 'node:crypto'
import { mkdir, writeFile } from 'node:fs/promises'
import { dirname, join } from 'node:path'
import {
  callWithSessionCredential,
  createSessionCredentialTracker,
  withSessionCredentialCleanup,
} from './session-credential-tracker'
import {
  buildAuthorizedWorkPoolAssignment,
  extractPublicError,
  runWithActorContext,
  selectAuthorizedWorkPoolScope,
  selectAuthorizedWorkSiteScope,
  type AuthorizedWorkPoolScope,
  type AuthorizedWorkSiteScope,
} from './issue1912-walkthrough-runtime'
import {
  classifyRequestFailure,
  clickRefreshAndWaitForListResponse,
  clickTabAndConfirmUnmount,
  fillFilterAndWaitForListResponse,
  listQueryFingerprint,
  navigateAndWaitForInitialList,
  RequestFailureEvidenceTracker,
} from './issue1912-walkthrough-policy'

const baseURL = process.env.NERV_IIP_PLAYWRIGHT_BASE_URL
const adminPassword = process.env.NERV_IIP_FULLSTACK_ADMIN_PASSWORD
const workerPassword = process.env.NERV_IIP_LEADER_DEMO_WORKER_PASSWORD
const evidencePath = process.env.NERV_IIP_ISSUE_1912_EVIDENCE_PATH
const runtimeProfileSource = process.env.NERV_IIP_ISSUE_1912_RUNTIME_PROFILE_SOURCE
const transport = process.env.NERV_IIP_ISSUE_1912_TRANSPORT
const persistence = process.env.NERV_IIP_ISSUE_1912_PERSISTENCE
const worldEnabled = process.env.NERV_IIP_ISSUE_1912_WORLD_ENABLED
const historyEnabled = process.env.NERV_IIP_ISSUE_1912_HISTORY_ENABLED
const scaleOrderCount = process.env.NERV_IIP_ISSUE_1912_SCALE_ORDER_COUNT

const requiresManagedSession = !baseURL || !adminPassword || !workerPassword || !evidencePath

test.setTimeout(25 * 60 * 1000)
test.describe.configure({ mode: 'serial' })

type JsonRecord = Record<string, unknown>
type Conclusion = 'runtime-confirmed' | 'gap' | 'not-verified'
type AutomationMode = 'automatic' | 'manual' | 'mixed'
type WalkthroughActor = 'erp-admin' | 'wms-worker'

const RFQ_NO = 'RFQ-WALK-001'
const SUPPLIER_QUOTATION_NO = 'SQ-WALK-001'
const SALES_QUOTATION_NO = 'QUO-WALK-001'
const PURCHASE_ORDER_NO = 'PO-WALK-001'
const PURCHASE_ORDER_APPROVAL_TEMPLATE_CODE = 'purchase-order-release'
const PURCHASE_ORDER_APPROVAL_TEMPLATE_VERSION = 1
const PURCHASE_RECEIPT_NO = 'PR-WALK-001'
const SALES_ORDER_NO = 'SO-WALK-001'
const DELIVERY_ORDER_NO = 'DO-WALK-001'
const INBOUND_ORDER_NO = 'IN-WALK-001'
const PUTAWAY_TASK_NO = 'PUT-WALK-001'
const PRODUCED_LOT_NO = 'LOT-WALK-001'
const PACK_REVIEW_NO = 'PACK-WALK-001'
const FINISHED_SKU = 'FG-QJ-P1-L'
const SITE_CODE = 'SITE-001'
const INBOUND_LOCATION = 'loc-raw-01'
const LINE_SIDE_LOCATION = 'loc-line-01'
const FINISHED_GOODS_LOCATION = 'loc-fg-01'
const QUANTITY = 1

const REQUIRED_NODES = [
  'rfq-supplier-quotation',
  'supplier-quotation-purchase-order',
  'purchase-order-approval',
  'purchase-order-receipt',
  'receipt-inbound-inventory',
  'sales-quotation-sales-order',
  'sales-order-demand',
  'demand-mrp-suggestion',
  'mrp-suggestion-mes-work-order',
  'mes-work-order-production',
  'production-finished-goods-receipt',
  'finished-goods-inventory',
  'sales-order-delivery',
  'delivery-wms-outbound',
  'wms-completed-erp-delivery',
  'erp-account-receivable',
] as const

type NodeName = (typeof REQUIRED_NODES)[number]

type EvidenceEntry = {
  node: NodeName
  sourceObject: string
  downstreamObject: string
  stableKey: string
  automationMode: AutomationMode
  request: JsonRecord | null
  responseOrLog: unknown
  conclusion: Conclusion
  demoWording: string
  responsibilityIssue: string | null
}

type UiProof = {
  node: NodeName
  actor: WalkthroughActor
  principalId: string
  page: string
  pageHttpStatus: number
  listPath: string
  listHttpStatus: number
  stableKey: string
  renderedRowText: string
  emptyText: string
  screenshot: string
}

type SessionCredentialTracker = ReturnType<typeof createSessionCredentialTracker>

type ActorRuntime = {
  actor: WalkthroughActor
  loginName: string
  expectedPrincipalId: string
  page: Page
  tracker: SessionCredentialTracker
  requestFailureEvidence: RequestFailureEvidenceTracker
  successfulListResponses: Map<string, Response>
  lastNavigationResponse: Response | null
  lastNavigationRoute: string | null
  lastNavigationEpoch: number | null
  principalId: string
  principalType: string
  permissionCodes: string[]
}

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
    readonly path: string,
    readonly lastData: unknown,
    readonly attempts: number,
    readonly timeoutMs: number,
  ) {
    super(
      `Timed out after ${attempts} attempts in ${timeoutMs}ms waiting for ${path}; last=${safeText(JSON.stringify(lastData))}`,
    )
    this.name = 'PollTimeoutError'
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

function inventoryStateFingerprint(value: unknown): JsonRecord {
  const data = asRecord(dataOf(value))
  const items = Array.isArray(data.items)
    ? data.items
        .map(asRecord)
        .map((item) => ({
          locationCode: textOf(item.locationCode),
          lotNo: item.lotNo ?? null,
          serialNo: item.serialNo ?? null,
          qualityStatus: textOf(item.qualityStatus),
          ownerType: textOf(item.ownerType),
          ownerId: item.ownerId ?? null,
          onHandQuantity: item.onHandQuantity ?? null,
          reservedQuantity: item.reservedQuantity ?? null,
          availableQuantity: item.availableQuantity ?? null,
          inventoryValue: item.inventoryValue ?? null,
        }))
        .sort((left, right) =>
          `${left.locationCode}/${left.lotNo ?? ''}/${left.serialNo ?? ''}`.localeCompare(
            `${right.locationCode}/${right.lotNo ?? ''}/${right.serialNo ?? ''}`,
          ),
        )
    : []
  return {
    onHandQuantity: data.onHandQuantity ?? null,
    reservedQuantity: data.reservedQuantity ?? null,
    availableQuantity: data.availableQuantity ?? null,
    inventoryValue: data.inventoryValue ?? null,
    items,
  }
}

function inventoryMovementFingerprint(value: unknown): JsonRecord {
  const data = asRecord(dataOf(value))
  const items = rowsOf(value)
    .map((item) => ({
      movementId: textOf(item.movementId),
      movementType: textOf(item.movementType),
      sourceService: textOf(item.sourceService),
      sourceDocumentId: textOf(item.sourceDocumentId),
      sourceDocumentLineId: item.sourceDocumentLineId ?? null,
      idempotencyKey: textOf(item.idempotencyKey),
      skuCode: textOf(item.skuCode),
      uomCode: textOf(item.uomCode),
      siteCode: textOf(item.siteCode),
      locationCode: textOf(item.locationCode),
      lotNo: item.lotNo ?? null,
      serialNo: item.serialNo ?? null,
      quantity: item.quantity ?? null,
    }))
    .sort((left, right) => left.movementId.localeCompare(right.movementId))
  return {
    totalCount: data.totalCount ?? null,
    inboundQuantityTotal: data.inboundQuantityTotal ?? null,
    outboundQuantityTotal: data.outboundQuantityTotal ?? null,
    items,
  }
}

function textOf(value: unknown): string {
  return value === null || value === undefined ? '' : String(value)
}

function safeText(value: unknown): string {
  return textOf(value)
    .replace(
      /(["']?(?:authorization|password|(?:access|refresh|id)?[_-]?token|secret|connectionstring|jwt)["']?\s*[:=]\s*)(?:"[^"]*"|'[^']*'|[^\s,;}]+)/gi,
      '$1<redacted-secret>',
    )
    .replace(/bearer\s+[^\s"']+/gi, '<redacted-credential>')
    .replace(/authorization/gi, '<redacted-header>')
    .replace(/password/gi, '<redacted-field>')
    .replace(/(?:access|refresh|id)?[_-]?token/gi, '<redacted-field>')
    .replace(/(?:secret|connectionstring|jwt)/gi, '<redacted-field>')
    .slice(0, 1600)
}

function credentialDigest(headers: { authorization?: string } | undefined): string {
  const authorization = headers?.authorization?.trim()
  return authorization ? createHash('sha256').update(authorization).digest('hex').slice(0, 16) : ''
}

function publicJson(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(publicJson)
  if (value === null || typeof value !== 'object') {
    return typeof value === 'string' ? safeText(value) : value
  }
  return Object.fromEntries(
    Object.entries(value as JsonRecord)
      .filter(
        ([key]) =>
          !/(authorization|password|(?:access|refresh|id)?[_-]?token|secret|connectionstring|jwt)/i.test(
            key,
          ),
      )
      .map(([key, item]) => [key, publicJson(item)]),
  )
}

async function jsonOf(response: APIResponse): Promise<unknown> {
  const contentType = response.headers()['content-type'] ?? ''
  if (!contentType.includes('json')) return { text: safeText(await response.text()) }
  return response.json()
}

function dateOnly(date: Date): string {
  return date.toISOString().slice(0, 10)
}

function queryPath(path: string, query: JsonRecord): string {
  const url = new URL(path, baseURL!)
  for (const [key, value] of Object.entries(query)) {
    if (value !== null && value !== undefined && value !== '')
      url.searchParams.set(key, String(value))
  }
  return `${url.pathname}${url.search}`
}

function errorText(error: unknown): string {
  return safeText(error instanceof Error ? error.message : error)
}

test('request failure policy keeps superseded navigation aborts but records API failures', async ({
  page,
}) => {
  test.skip(
    test.info().project.name !== 'desktop',
    'the request policy probe is intentionally desktop-only',
  )

  const expectedCancellations: JsonRecord[] = []
  const unexpectedFailures: JsonRecord[] = []
  const apiFailures: JsonRecord[] = []
  page.on('requestfailed', (request) => {
    const classified = classifyRequestFailure({
      method: request.method(),
      url: request.url(),
      failure: safeText(request.failure()?.errorText ?? 'unknown request failure'),
      resourceType: request.resourceType(),
      isNavigationRequest: request.isNavigationRequest(),
    })
    if (classified.expected) expectedCancellations.push(classified.record)
    else unexpectedFailures.push(classified.record)
  })
  page.on('response', (response: Response) => {
    const url = new URL(response.url())
    if (url.pathname === '/api/issue1912-policy-failure' && response.status() >= 400) {
      apiFailures.push({
        kind: 'http-error',
        method: response.request().method(),
        path: url.pathname + url.search,
        status: response.status(),
        classification: 'api-http-error',
      })
    }
  })

  await page.route('**/issue1912-policy-navigation*', async (route) => {
    const url = new URL(route.request().url())
    if (url.searchParams.get('phase') === 'first')
      await new Promise((resolve) => setTimeout(resolve, 250))
    try {
      await route.fulfill({
        status: 200,
        contentType: 'text/html',
        body: '<!doctype html><p id="policy-success">success</p>',
      })
    } catch {
      // The first navigation is intentionally superseded and may be gone by the time its route resolves.
    }
  })
  await page.route('**/api/issue1912-policy-failure', async (route) => {
    await route.fulfill({
      status: 503,
      contentType: 'application/json',
      body: '{"error":"intentional policy probe"}',
    })
  })

  try {
    const firstRequest = page.waitForRequest(
      (request) => new URL(request.url()).pathname === '/issue1912-policy-navigation',
    )
    const firstNavigation = page
      .goto('/issue1912-policy-navigation?phase=first', {
        waitUntil: 'domcontentloaded',
        timeout: 30_000,
      })
      .catch(() => null)
    await firstRequest
    const secondNavigation = await page.goto('/issue1912-policy-navigation?phase=second', {
      waitUntil: 'domcontentloaded',
      timeout: 30_000,
    })
    await firstNavigation
    expect(secondNavigation?.status()).toBe(200)
    await expect(page.locator('#policy-success')).toHaveText('success')

    await page.evaluate(() => fetch('/api/issue1912-policy-failure').catch(() => undefined))
    await expect.poll(() => apiFailures.length).toBe(1)
    expect(
      expectedCancellations.some(
        (item) =>
          item.classification === 'expected-superseded-document-or-resource' &&
          textOf(item.resourceType) === 'document',
      ),
    ).toBe(true)
    expect(unexpectedFailures).toEqual([])
    expect(apiFailures[0]).toMatchObject({ status: 503, classification: 'api-http-error' })
  } finally {
    await page.unroute('**/issue1912-policy-navigation*')
    await page.unroute('**/api/issue1912-policy-failure')
  }
})

test('NERV-1127 / GitHub #1912 verifies the isolated walkthrough in real browser pages', async ({
  page,
  browser,
}) => {
  test.skip(
    requiresManagedSession,
    'requires a managed full-stack session and an evidence destination',
  )
  test.skip(
    test.info().project.name !== 'desktop',
    'the evidence run is intentionally desktop-only',
  )

  const generatedAtUtc = new Date()
  const evidenceDirectory = dirname(evidencePath!)
  const screenshotDirectory = join(
    evidenceDirectory,
    'issue1912-real-machine-walkthrough-screenshots',
  )
  await mkdir(screenshotDirectory, { recursive: true })

  let organizationId = ''
  let environmentId = ''
  let principalId = ''
  let principalType = ''
  let workerPrincipalId = ''
  let workerPrincipalType = ''
  const workerContext: BrowserContext = await browser.newContext({ baseURL: baseURL! })
  const sessionCredentialTracker = createSessionCredentialTracker({
    origin: new URL(baseURL!).origin,
    page,
    businessPathPrefix: '/api/business-console/',
    refreshPath: '/api/console/v1/auth/refresh',
  })
  const workerPage = await workerContext.newPage()
  const workerSessionCredentialTracker = createSessionCredentialTracker({
    origin: new URL(baseURL!).origin,
    page: workerPage,
    businessPathPrefix: '/api/business-console/',
    refreshPath: '/api/console/v1/auth/refresh',
  })
  const adminRuntime: ActorRuntime = {
    actor: 'erp-admin',
    loginName: 'admin',
    expectedPrincipalId: 'user-admin',
    page,
    tracker: sessionCredentialTracker,
    requestFailureEvidence: new RequestFailureEvidenceTracker(),
    successfulListResponses: new Map(),
    lastNavigationResponse: null,
    lastNavigationRoute: null,
    lastNavigationEpoch: null,
    principalId: '',
    principalType: '',
    permissionCodes: [],
  }
  const workerRuntime: ActorRuntime = {
    actor: 'wms-worker',
    loginName: 'emp049',
    expectedPrincipalId: 'user-emp-049',
    page: workerPage,
    tracker: workerSessionCredentialTracker,
    requestFailureEvidence: new RequestFailureEvidenceTracker(),
    successfulListResponses: new Map(),
    lastNavigationResponse: null,
    lastNavigationRoute: null,
    lastNavigationEpoch: null,
    principalId: '',
    principalType: '',
    permissionCodes: [],
  }
  const evidence = new Map<NodeName, EvidenceEntry>()
  const setup: JsonRecord[] = []
  const uiEvidence: UiProof[] = []
  const failedRequests: JsonRecord[] = []
  const expectedRequestCancellations: JsonRecord[] = []
  const expectedBusinessRejections: JsonRecord[] = []
  const pageErrors: string[] = []

  for (const node of REQUIRED_NODES) {
    evidence.set(node, {
      node,
      sourceObject: 'not-observed',
      downstreamObject: 'not-observed',
      stableKey: node,
      automationMode: 'automatic',
      request: null,
      responseOrLog: { reason: 'upstream evidence was not established in this run' },
      conclusion: 'not-verified',
      demoWording: `${node}: this run did not establish a public runtime association.`,
      responsibilityIssue: null,
    })
  }

  const record = (entry: EvidenceEntry) => evidence.set(entry.node, entry)

  const attachObservers = (runtime: ActorRuntime) => {
    runtime.page.on('request', (request) => {
      runtime.requestFailureEvidence.observeRequest(request, runtime.page.url())
      runtime.tracker.observeRequest({ page: runtime.page, request })
    })
    runtime.page.on('requestfailed', (request) => {
      runtime.requestFailureEvidence.resolveFailureEvidence(request, (cancellationEvidence) => {
        const classified = classifyRequestFailure({
          method: request.method(),
          url: request.url(),
          failure: safeText(request.failure()?.errorText ?? 'unknown request failure'),
          resourceType: request.resourceType(),
          isNavigationRequest: request.isNavigationRequest(),
          cancellationEvidence,
        })
        const record = {
          ...classified.record,
          actor: runtime.actor,
          principalId: runtime.principalId || runtime.expectedPrincipalId,
        }
        if (classified.expected) expectedRequestCancellations.push(record)
        else failedRequests.push(record)
      })
    })
    runtime.page.on('response', (response: Response) => {
      const url = new URL(response.url())
      if (url.pathname === '/api/console/v1/auth/refresh') {
        void runtime.tracker
          .observeRefreshResponse({ page: runtime.page, response })
          .catch((error) => {
            failedRequests.push({
              kind: 'refresh-credential-capture',
              actor: runtime.actor,
              principalId: runtime.principalId || runtime.expectedPrincipalId,
              path: url.pathname,
              status: response.status(),
              error: errorText(error),
            })
          })
      }
      if (
        response.request().method() === 'GET' &&
        response.status() === 200 &&
        url.pathname.startsWith('/api/')
      ) {
        runtime.successfulListResponses.set(url.pathname, response)
      }
      if (url.pathname.startsWith('/api/') && response.status() >= 400) {
        failedRequests.push({
          kind: 'http-error',
          actor: runtime.actor,
          principalId: runtime.principalId || runtime.expectedPrincipalId,
          method: response.request().method(),
          path: url.pathname + url.search,
          status: response.status(),
        })
      }
    })
    runtime.page.on('pageerror', (error) =>
      pageErrors.push(`${runtime.actor}: ${safeText(error.message)}`),
    )
  }

  attachObservers(adminRuntime)
  attachObservers(workerRuntime)

  type CallOptions = { expectedStatus?: number }

  const invoke = async (
    runtime: ActorRuntime,
    method: 'GET' | 'POST',
    path: string,
    body?: JsonRecord,
    options: CallOptions = {},
  ) => {
    const url = new URL(path, baseURL!)
    const response = await callWithSessionCredential(runtime.tracker, (headers) =>
      runWithActorContext(
        {
          actor: runtime.actor,
          principalId: runtime.principalId || runtime.expectedPrincipalId,
          authorization: headers.authorization ?? '',
        },
        ({ authorization }) =>
          runtime.page.request.fetch(url.toString(), {
            method,
            data: body,
            headers: { authorization },
          }),
      ),
    )
    const payload = await jsonOf(response)
    const summary: JsonRecord = {
      actor: runtime.actor,
      principalId: runtime.principalId || runtime.expectedPrincipalId,
      method,
      path: url.pathname + url.search,
      status: response.status(),
      correlationId:
        response.headers()['x-correlation-id'] ?? response.headers().traceparent ?? null,
      body: body ? publicJson(body) : null,
    }
    if (!response.ok() && response.status() !== options.expectedStatus) {
      throw new PublicCallError(
        method,
        summary.path as string,
        response.status(),
        summary,
        publicJson(payload),
      )
    }
    return { payload, summary, publicPayload: publicJson(payload) as JsonRecord }
  }

  const call = (method: 'GET' | 'POST', path: string, body?: JsonRecord) =>
    invoke(adminRuntime, method, path, body)
  const workerCall = (method: 'GET' | 'POST', path: string, body?: JsonRecord) =>
    invoke(workerRuntime, method, path, body)
  const workerCallExpecting = async (
    method: 'GET' | 'POST',
    path: string,
    body: JsonRecord,
    expectedError: { code: string; message: string },
  ) => {
    const response = await invoke(workerRuntime, method, path, body, { expectedStatus: 403 })
    expect(response.summary.status).toBe(403)
    const publicError = extractPublicError(response.publicPayload)
    expect(publicError).toEqual(expectedError)
    expectedBusinessRejections.push({
      ...response.summary,
      response: response.publicPayload,
      publicError,
    })
    return { ...response, publicError }
  }

  const pollRowsFor = async (
    runtime: ActorRuntime,
    path: string,
    query: JsonRecord,
    predicate: (row: JsonRecord) => boolean,
    timeoutMs = 90_000,
  ) => {
    const startedAt = Date.now()
    const deadline = startedAt + timeoutMs
    let attempts = 0
    let lastRows: JsonRecord[] = []
    do {
      attempts += 1
      const response = await invoke(runtime, 'GET', queryPath(path, query))
      lastRows = rowsOf(response.payload)
      const match = lastRows.find(predicate)
      if (match)
        return {
          match,
          call: response,
          poll: { attempts, elapsedMs: Date.now() - startedAt, timeoutMs },
        }
      const remaining = deadline - Date.now()
      if (remaining > 0) await runtime.page.waitForTimeout(Math.min(1000, remaining))
    } while (Date.now() < deadline)
    throw new PollTimeoutError(path, { items: lastRows }, attempts, timeoutMs)
  }

  const pollDataFor = async (
    runtime: ActorRuntime,
    path: string,
    query: JsonRecord,
    predicate: (data: JsonRecord) => boolean,
    timeoutMs = 90_000,
  ) => {
    const startedAt = Date.now()
    const deadline = startedAt + timeoutMs
    let attempts = 0
    let lastData: JsonRecord = {}
    do {
      attempts += 1
      const response = await invoke(runtime, 'GET', queryPath(path, query))
      lastData = asRecord(dataOf(response.payload))
      if (predicate(lastData))
        return {
          data: lastData,
          call: response,
          poll: { attempts, elapsedMs: Date.now() - startedAt, timeoutMs },
        }
      const remaining = deadline - Date.now()
      if (remaining > 0) await runtime.page.waitForTimeout(Math.min(1000, remaining))
    } while (Date.now() < deadline)
    throw new PollTimeoutError(path, lastData, attempts, timeoutMs)
  }

  const pollRows = (
    path: string,
    query: JsonRecord,
    predicate: (row: JsonRecord) => boolean,
    timeoutMs = 90_000,
  ) => pollRowsFor(adminRuntime, path, query, predicate, timeoutMs)
  const workerPollRows = (
    path: string,
    query: JsonRecord,
    predicate: (row: JsonRecord) => boolean,
    timeoutMs = 90_000,
  ) => pollRowsFor(workerRuntime, path, query, predicate, timeoutMs)
  const pollData = (
    path: string,
    query: JsonRecord,
    predicate: (data: JsonRecord) => boolean,
    timeoutMs = 90_000,
  ) => pollDataFor(adminRuntime, path, query, predicate, timeoutMs)
  const workerPollData = (
    path: string,
    query: JsonRecord,
    predicate: (data: JsonRecord) => boolean,
    timeoutMs = 90_000,
  ) => pollDataFor(workerRuntime, path, query, predicate, timeoutMs)

  const markFailure = (node: NodeName, error: unknown, mode: AutomationMode = 'automatic') => {
    const current = evidence.get(node)!
    const publicError =
      error instanceof PublicCallError
        ? { error: errorText(error), request: error.request, response: publicJson(error.payload) }
        : error instanceof PollTimeoutError
          ? {
              error: errorText(error),
              path: error.path,
              attempts: error.attempts,
              timeoutMs: error.timeoutMs,
              lastData: publicJson(error.lastData),
            }
          : { error: errorText(error) }
    record({
      ...current,
      automationMode: mode,
      request: error instanceof PublicCallError ? error.request : current.request,
      responseOrLog: publicError,
      conclusion: 'gap',
      demoWording: `${node}: the public runtime attempt did not converge; this is a gap, not a completed hop.`,
      responsibilityIssue: null,
    })
  }

  type PageProofOptions = {
    actor?: WalkthroughActor
    route: string
    listPath: string
    stableText: string
    filterLabel?: string
    // `client` proves the rendered table after a local filter; `server` requires an exact 200 list response.
    filterResponseMode?: 'server' | 'client'
    tabText?: string | RegExp
    // Reuse a settled route when a tab proof only needs refreshed data; a full reload can supersede API work.
    reuseCurrentRoute?: boolean
    refreshListBeforeProof?: boolean
    selectOptions?: Array<{ label: string; option: string }>
    emptyText: string
    screenshotName: string
  }

  const samePageRoute = (currentUrl: string, route: string): boolean => {
    if (!currentUrl) return false
    const current = new URL(currentUrl)
    const expected = new URL(route, currentUrl)
    return (
      current.origin === expected.origin &&
      current.pathname === expected.pathname &&
      current.search === expected.search
    )
  }

  const provePage = async (node: NodeName, options: PageProofOptions): Promise<UiProof> => {
    const runtime = options.actor === 'wms-worker' ? workerRuntime : adminRuntime
    const targetPage = runtime.page
    const reuseCurrentRoute = options.reuseCurrentRoute === true
    if (reuseCurrentRoute && !samePageRoute(targetPage.url(), options.route)) {
      throw new Error(`page ${options.route} cannot reuse the current route ${targetPage.url()}`)
    }
    let navigation: Response | null = null
    let firstList: Response | null = null
    let firstListNavigationEpoch: number | undefined

    if (reuseCurrentRoute) {
      if (
        !runtime.lastNavigationRoute ||
        !samePageRoute(runtime.lastNavigationRoute, options.route)
      ) {
        throw new Error(`page ${options.route} has no matching completed navigation to reuse`)
      }
      navigation = runtime.lastNavigationResponse
      if (!navigation || navigation.status() !== 200) {
        throw new Error(`page ${options.route} has no completed HTTP 200 navigation to reuse`)
      }
      firstList = runtime.successfulListResponses.get(options.listPath) ?? null
      firstListNavigationEpoch = runtime.lastNavigationEpoch ?? undefined
    } else {
      const navigationAttempt = runtime.requestFailureEvidence.beginLifecycleAttempt(
        targetPage.url(),
      )
      let navigationConfirmed = false
      try {
        const initialPage = await navigateAndWaitForInitialList(targetPage, {
          route: options.route,
          listPath: options.listPath,
          timeoutMs: 120_000,
        })
        navigation = initialPage.navigation
        firstList = initialPage.firstList
        firstListNavigationEpoch = initialPage.navigationEpoch
        runtime.lastNavigationResponse = navigation
        runtime.lastNavigationRoute = targetPage.url()
        runtime.lastNavigationEpoch = initialPage.navigationEpoch
        runtime.successfulListResponses.set(options.listPath, firstList)
        expect(navigation?.status(), `page ${options.route} must return HTTP 200`).toBe(200)
        expect(firstList.status(), `list ${options.listPath} must return HTTP 200`).toBe(200)
        navigationAttempt.confirm('navigation')
        navigationConfirmed = true
      } finally {
        if (navigationConfirmed) navigationAttempt.complete()
        else navigationAttempt.cancel()
      }
    }

    if (options.refreshListBeforeProof) {
      // A data refresh is not a lifecycle transition: any API abort remains an unexpected failure.
      firstList = await clickRefreshAndWaitForListResponse(targetPage, options.listPath)
      firstListNavigationEpoch = runtime.lastNavigationEpoch ?? undefined
      runtime.successfulListResponses.set(options.listPath, firstList)
    }

    if (!firstList) {
      throw new Error(`list ${options.listPath} has no completed HTTP 200 response to prove`)
    }
    expect(navigation?.status(), `page ${options.route} must return HTTP 200`).toBe(200)
    expect(firstList.status(), `list ${options.listPath} must return HTTP 200`).toBe(200)

    if (options.filterLabel) {
      await fillFilterAndWaitForListResponse(targetPage, {
        route: targetPage.url(),
        listPath: options.listPath,
        filterLabel: options.filterLabel,
        stableText: options.stableText,
        responseMode: options.filterResponseMode ?? 'server',
        initialListResponse: firstList,
        initialListNavigationEpoch: firstListNavigationEpoch,
        expectedListQueryFingerprint: listQueryFingerprint(firstList.url()),
        timeoutMs: 120_000,
      })
    }

    if (options.tabText) {
      await clickTabAndConfirmUnmount(targetPage, options.tabText, runtime.requestFailureEvidence)
    }

    for (const selectOption of options.selectOptions ?? []) {
      const listResponse = targetPage.waitForResponse(
        (response) => {
          const url = new URL(response.url())
          return (
            response.request().method() === 'GET' &&
            url.pathname === options.listPath &&
            response.status() === 200
          )
        },
        { timeout: 120_000 },
      )
      await targetPage.getByLabel(selectOption.label).click()
      await targetPage.getByRole('option', { name: selectOption.option, exact: true }).click()
      await listResponse
    }

    const row = targetPage.locator('tbody tr').filter({ hasText: options.stableText }).first()
    await expect(row, `page ${options.route} must render a stable business row`).toBeVisible({
      timeout: 120_000,
    })
    await expect(row).toContainText(options.stableText)
    await expect(targetPage.getByText(options.emptyText, { exact: true })).toHaveCount(0)
    const screenshot = join(screenshotDirectory, options.screenshotName)
    await targetPage.screenshot({ path: screenshot, fullPage: true })
    const proof: UiProof = {
      node,
      actor: runtime.actor,
      principalId: runtime.principalId,
      page: options.route,
      pageHttpStatus: navigation?.status() ?? 0,
      listPath: new URL(firstList.url()).pathname,
      listHttpStatus: firstList.status(),
      stableKey: options.stableText,
      renderedRowText: safeText(await row.innerText()),
      emptyText: options.emptyText,
      screenshot,
    }
    uiEvidence.push(proof)
    return proof
  }

  const provePageSafely = async (node: NodeName, options: PageProofOptions) => {
    try {
      return await provePage(node, options)
    } catch (error) {
      markFailure(node, error, 'mixed')
      throw error
    }
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
    expect(login.status()).toBe(200)
    const auth = asRecord(dataOf(await login.json()))
    const principal = asRecord(auth.principal)
    organizationId = textOf(principal.organizationId)
    environmentId = textOf(principal.environmentId)
    principalType = textOf(principal.principalType).trim().toLowerCase()
    principalId = textOf(principal.principalId).trim()
    adminRuntime.principalId = principalId
    adminRuntime.principalType = principalType
    adminRuntime.permissionCodes = Array.isArray(principal.permissionCodes)
      ? principal.permissionCodes.map(textOf).filter(Boolean)
      : []
    expect(organizationId).not.toBe('')
    expect(environmentId).not.toBe('')
    expect(principalType).toBe('user')
    expect(principalId).toBe(adminRuntime.expectedPrincipalId)
    expect(adminRuntime.permissionCodes).toContain('business.approvals.manage')

    const businessRequest = page.waitForRequest(
      (request) => {
        const path = new URL(request.url()).pathname
        return (
          path === '/api/business-console/v1/master-data/skus' &&
          Boolean(request.headers().authorization)
        )
      },
      { timeout: 120_000 },
    )
    await page.goto('/master-data/skus', { waitUntil: 'domcontentloaded', timeout: 120_000 })
    sessionCredentialTracker.observeRequest({ page, request: await businessRequest })
    const adminHeaders = await sessionCredentialTracker.headers()
    expect(adminHeaders).toBeDefined()

    await workerPage.goto('/login', { waitUntil: 'domcontentloaded', timeout: 120_000 })
    const workerLoginName = workerPage.getByLabel('登录名')
    await expect(workerLoginName).toBeVisible({ timeout: 120_000 })
    const workerLoginResponse = workerPage.waitForResponse(
      (response) => new URL(response.url()).pathname === '/api/console/v1/auth/login',
      { timeout: 120_000 },
    )
    await workerLoginName.fill('emp049')
    await workerPage.getByLabel('密码').fill(workerPassword!)
    await workerPage.getByRole('button', { name: '登录' }).click()
    const workerLogin = await workerLoginResponse
    expect(workerLogin.status()).toBe(200)
    const workerAuth = asRecord(dataOf(await workerLogin.json()))
    const workerPrincipal = asRecord(workerAuth.principal)
    workerPrincipalId = textOf(workerPrincipal.principalId).trim()
    workerPrincipalType = textOf(workerPrincipal.principalType).trim().toLowerCase()
    workerRuntime.principalId = workerPrincipalId
    workerRuntime.principalType = workerPrincipalType
    workerRuntime.permissionCodes = Array.isArray(workerPrincipal.permissionCodes)
      ? workerPrincipal.permissionCodes.map(textOf).filter(Boolean)
      : []
    expect(workerPrincipal.organizationId).toBe(organizationId)
    expect(workerPrincipal.environmentId).toBe(environmentId)
    expect(workerPrincipalType).toBe('user')
    expect(workerPrincipalId).toBe(workerRuntime.expectedPrincipalId)
    expect(workerRuntime.permissionCodes).toContain('business.wms.receipts.manage')
    expect(workerRuntime.permissionCodes).toContain('business.wms.shipments.manage')
    expect(workerRuntime.permissionCodes).toContain('business.inventory.ledger.read')
    expect(workerRuntime.permissionCodes).not.toContain('business.approvals.manage')

    const workerBusinessRequest = workerPage.waitForRequest(
      (request) => {
        const path = new URL(request.url()).pathname
        return path.startsWith('/api/business-console/') && Boolean(request.headers().authorization)
      },
      { timeout: 120_000 },
    )
    await workerPage.goto('/wms/inbound', {
      waitUntil: 'domcontentloaded',
      timeout: 120_000,
    })
    workerSessionCredentialTracker.observeRequest({
      page: workerPage,
      request: await workerBusinessRequest,
    })
    const workerHeaders = await workerSessionCredentialTracker.headers()
    expect(workerHeaders).toBeDefined()
    const adminCredentialDigest = credentialDigest(adminHeaders)
    const workerCredentialDigest = credentialDigest(workerHeaders)
    expect(adminCredentialDigest).not.toBe('')
    expect(workerCredentialDigest).not.toBe('')
    expect(workerCredentialDigest).not.toBe(adminCredentialDigest)
    setup.push({
      kind: 'identityIsolation',
      contexts: 2,
      admin: {
        actor: adminRuntime.actor,
        loginName: adminRuntime.loginName,
        principalId: adminRuntime.principalId,
        permissionCodes: adminRuntime.permissionCodes,
        credentialDigest: adminCredentialDigest,
      },
      worker: {
        actor: workerRuntime.actor,
        loginName: workerRuntime.loginName,
        principalId: workerRuntime.principalId,
        permissionCodes: workerRuntime.permissionCodes,
        credentialDigest: workerCredentialDigest,
      },
      credentialsShared: false,
    })

    // The seed is intentionally read-only here. The test proves the reserved facts exist and never
    // creates or overwrites an approval template; CreatePurchaseOrderCommand starts the seeded chain.
    const rfq = await call(
      'GET',
      queryPath('/api/business-console/v1/erp/procurement/rfqs', {
        organizationId,
        environmentId,
        keyword: RFQ_NO,
        skip: 0,
        take: 100,
      }),
    )
    const rfqRow = rowsOf(rfq.payload).find((row) => textOf(row.rfqNo) === RFQ_NO)
    if (!rfqRow) throw new Error(`Seed RFQ ${RFQ_NO} was not returned by the public facade.`)
    const supplierQuotes = await call(
      'GET',
      queryPath('/api/business-console/v1/erp/procurement/supplier-quotations', {
        organizationId,
        environmentId,
        rfqNo: RFQ_NO,
        keyword: SUPPLIER_QUOTATION_NO,
        skip: 0,
        take: 100,
      }),
    )
    const supplierQuote = rowsOf(supplierQuotes.payload).find(
      (row) => textOf(row.quotationNo) === SUPPLIER_QUOTATION_NO,
    )
    if (!supplierQuote)
      throw new Error(
        `Seed supplier quotation ${SUPPLIER_QUOTATION_NO} was not returned by the public facade.`,
      )
    const quoteLine = asRecord((Array.isArray(supplierQuote.lines) ? supplierQuote.lines : [])[0])
    const supplierCode = textOf(supplierQuote.supplierCode)
    const materialSku = textOf(quoteLine.skuCode)
    const materialUom = textOf(quoteLine.uomCode)
    const materialQuantity = Number(quoteLine.quantity ?? 0)
    const materialUnitPrice = Number(quoteLine.unitPrice ?? 0)
    if (
      !supplierCode ||
      !materialSku ||
      !materialUom ||
      materialQuantity <= 0 ||
      materialUnitPrice <= 0
    ) {
      throw new Error(
        `Seed supplier quotation ${SUPPLIER_QUOTATION_NO} did not expose a complete line.`,
      )
    }
    const rfqUi = await provePageSafely('rfq-supplier-quotation', {
      route: '/erp/procurement/rfqs',
      listPath: '/api/business-console/v1/erp/procurement/rfqs',
      filterLabel: 'RFQ 关键字',
      stableText: RFQ_NO,
      emptyText: '还没有询价单。可从采购申请或供应商策略发起真实询价。',
      screenshotName: '01-rfq.png',
    })
    const quoteUi = await provePageSafely('rfq-supplier-quotation', {
      route: '/erp/procurement/supplier-quotations',
      listPath: '/api/business-console/v1/erp/procurement/supplier-quotations',
      filterLabel: '供应商报价关键字',
      stableText: SUPPLIER_QUOTATION_NO,
      emptyText: '还没有供应商报价。先在询价单页面发起询价，供应商回价后在此汇总比价。',
      screenshotName: '02-supplier-quotation.png',
    })
    record({
      node: 'rfq-supplier-quotation',
      sourceObject: RFQ_NO,
      downstreamObject: SUPPLIER_QUOTATION_NO,
      stableKey: `${RFQ_NO} -> ${SUPPLIER_QUOTATION_NO}`,
      automationMode: 'automatic',
      request: supplierQuotes.summary,
      responseOrLog: {
        rfq: publicJson(rfqRow),
        supplierQuotation: publicJson(supplierQuote),
        ui: [rfqUi, quoteUi],
      },
      conclusion: 'runtime-confirmed',
      demoWording:
        '浏览器页面以 HTTP 200 返回 RFQ 与供应商报价，并渲染了稳定业务编号行；报价事实来自隔离 walkthrough seed。',
      responsibilityIssue: null,
    })

    const purchaseOrderRequest = {
      organizationId,
      environmentId,
      purchaseOrderNo: PURCHASE_ORDER_NO,
      supplierCode,
      siteCode: SITE_CODE,
      lines: [
        {
          lineNo: textOf(quoteLine.lineNo || '10'),
          skuCode: materialSku,
          uomCode: materialUom,
          quantity: materialQuantity,
          unitPrice: materialUnitPrice,
          promisedDate: textOf(quoteLine.promisedDate || '2099-12-31'),
        },
      ],
      idempotencyKey: `issue1912-${PURCHASE_ORDER_NO}`,
    }
    const purchaseOrder = await call(
      'POST',
      '/api/business-console/v1/erp/procurement/purchase-orders',
      purchaseOrderRequest,
    )
    setup.push({ request: purchaseOrder.summary, response: purchaseOrder.publicPayload })
    const purchaseOrderId = textOf(asRecord(dataOf(purchaseOrder.payload)).purchaseOrderId)
    if (!purchaseOrderId)
      throw new Error(`Purchase order ${PURCHASE_ORDER_NO} did not return an ID.`)
    const poUi = await provePageSafely('supplier-quotation-purchase-order', {
      route: '/erp/procurement/purchase-orders',
      listPath: '/api/business-console/v1/erp/procurement/purchase-orders',
      filterLabel: '采购订单关键字',
      stableText: PURCHASE_ORDER_NO,
      emptyText: '还没有采购订单。已批准的供应商报价或采购申请转单后会在这里出现。',
      screenshotName: '03-purchase-order-pending.png',
    })
    record({
      node: 'supplier-quotation-purchase-order',
      sourceObject: SUPPLIER_QUOTATION_NO,
      downstreamObject: PURCHASE_ORDER_NO,
      stableKey: `${SUPPLIER_QUOTATION_NO} -> ${PURCHASE_ORDER_NO}`,
      automationMode: 'manual',
      request: purchaseOrder.summary,
      responseOrLog: { purchaseOrder: purchaseOrder.publicPayload, ui: poUi },
      conclusion: 'runtime-confirmed',
      demoWording:
        '通过公开 ERP 采购接口以供应商报价行创建了固定采购订单，随后将用真实审批链释放。',
      responsibilityIssue: null,
    })

    const pendingApproval = await pollRows(
      '/api/business-console/v1/approval/chains',
      {
        organizationId,
        environmentId,
        status: 'pending',
        sourceService: 'business-erp',
        documentType: 'purchase-order',
        documentId: PURCHASE_ORDER_NO,
        skip: 0,
        take: 100,
      },
      (row) =>
        textOf(row.documentId) === PURCHASE_ORDER_NO &&
        textOf(row.status).toLowerCase() === 'pending',
    )
    const chainId = textOf(pendingApproval.match.chainId)
    if (!chainId)
      throw new Error(`Purchase order ${PURCHASE_ORDER_NO} did not expose an approval chain.`)
    const approvalTemplateCode = textOf(pendingApproval.match.templateCode)
    const approvalTemplateVersion = Number(pendingApproval.match.templateVersion)
    if (
      approvalTemplateCode !== PURCHASE_ORDER_APPROVAL_TEMPLATE_CODE ||
      approvalTemplateVersion !== PURCHASE_ORDER_APPROVAL_TEMPLATE_VERSION
    ) {
      throw new Error(
        `Purchase order ${PURCHASE_ORDER_NO} matched unexpected approval template ${approvalTemplateCode}@${approvalTemplateVersion}.`,
      )
    }
    const approvalChainDetail = await call(
      'GET',
      queryPath(`/api/business-console/v1/approval/chains/${encodeURIComponent(chainId)}`, {
        organizationId,
        environmentId,
      }),
    )
    const approvalChain = asRecord(dataOf(approvalChainDetail.payload))
    if (
      textOf(approvalChain.templateCode) !== PURCHASE_ORDER_APPROVAL_TEMPLATE_CODE ||
      Number(approvalChain.templateVersion) !== PURCHASE_ORDER_APPROVAL_TEMPLATE_VERSION ||
      textOf(approvalChain.sourceService) !== 'business-erp' ||
      textOf(approvalChain.documentType) !== 'purchase-order' ||
      textOf(approvalChain.documentId) !== PURCHASE_ORDER_NO
    ) {
      throw new Error(
        `Approval chain ${chainId} did not preserve the seeded purchase-order-release identity.`,
      )
    }
    const approvalStep = (Array.isArray(approvalChain.steps) ? approvalChain.steps : [])
      .map(asRecord)
      .find((step) => Number(step.stepNo) === 1)
    if (
      !approvalStep ||
      textOf(approvalStep.stepName).trim() === '' ||
      textOf(approvalStep.approverType) !== 'user' ||
      textOf(approvalStep.approverRef) !== 'user-admin' ||
      textOf(approvalStep.status).toLowerCase() !== 'pending'
    ) {
      throw new Error(
        `Approval chain ${chainId} did not expose the seeded user-admin step 1 as pending.`,
      )
    }
    const approvalDecision = await call(
      'POST',
      queryPath(
        `/api/business-console/v1/approval/chains/${encodeURIComponent(chainId)}/steps/1/resolve`,
        {
          organizationId,
          environmentId,
        },
      ),
      {
        organizationId,
        environmentId,
        actorType: principalType || 'user',
        actorRef: principalId,
        decision: 'approve',
        comment: 'NERV-1127 real-machine walkthrough approval',
      },
    )
    const releasedOrder = await pollRows(
      '/api/business-console/v1/erp/procurement/purchase-orders',
      {
        organizationId,
        environmentId,
        keyword: PURCHASE_ORDER_NO,
        skip: 0,
        take: 100,
      },
      (row) =>
        textOf(row.purchaseOrderNo) === PURCHASE_ORDER_NO &&
        textOf(row.status).toLowerCase() === 'released',
    )
    const approvalUi = await provePageSafely('purchase-order-approval', {
      route: `/approval?sourceService=business-erp&documentType=purchase-order&documentId=${encodeURIComponent(PURCHASE_ORDER_NO)}`,
      listPath: '/api/business-console/v1/approval/chains',
      stableText: PURCHASE_ORDER_NO,
      tabText: /审批中的单据/,
      emptyText: '还没有审批链。审批模板匹配后，业务单据会在这里留下流程实例。',
      screenshotName: '04-purchase-order-approval.png',
    })
    const releasedPoUi = await provePageSafely('purchase-order-approval', {
      route: '/erp/procurement/purchase-orders',
      listPath: '/api/business-console/v1/erp/procurement/purchase-orders',
      filterLabel: '采购订单关键字',
      stableText: PURCHASE_ORDER_NO,
      emptyText: '还没有采购订单。已批准的供应商报价或采购申请转单后会在这里出现。',
      screenshotName: '05-purchase-order-released.png',
    })
    record({
      node: 'purchase-order-approval',
      sourceObject: PURCHASE_ORDER_NO,
      downstreamObject: chainId,
      stableKey: `${PURCHASE_ORDER_NO} -> ${chainId} -> approved -> released`,
      automationMode: 'manual',
      request: pendingApproval.call.summary,
      responseOrLog: {
        chain: publicJson(pendingApproval.match),
        templateCode: approvalTemplateCode,
        templateVersion: approvalTemplateVersion,
        chainDetail: approvalChainDetail.publicPayload,
        step: publicJson(approvalStep),
        decision: approvalDecision.publicPayload,
        releasedOrder: publicJson(releasedOrder.match),
        ui: [approvalUi, releasedPoUi],
      },
      conclusion: 'runtime-confirmed',
      demoWording:
        '采购订单走过 ERP 创建的真实 purchase-order-release 审批模板，公开审批中心返回批准后订单行显示 released；测试没有写入或覆盖模板。',
      responsibilityIssue: null,
    })

    const receiptRequest = {
      organizationId,
      environmentId,
      purchaseReceiptNo: PURCHASE_RECEIPT_NO,
      purchaseOrderNo: PURCHASE_ORDER_NO,
      lines: [
        {
          purchaseOrderLineNo: textOf(quoteLine.lineNo || '10'),
          receivedQuantity: materialQuantity,
          qualityStatus: 'unrestricted',
        },
      ],
      idempotencyKey: `issue1912-${PURCHASE_RECEIPT_NO}`,
    }
    const receipt = await call(
      'POST',
      '/api/business-console/v1/erp/procurement/purchase-receipts',
      receiptRequest,
    )
    setup.push({ request: receipt.summary, response: receipt.publicPayload })
    const receiptOrder = await pollRows(
      '/api/business-console/v1/erp/procurement/purchase-orders',
      {
        organizationId,
        environmentId,
        keyword: PURCHASE_ORDER_NO,
        skip: 0,
        take: 100,
      },
      (row) =>
        textOf(row.purchaseOrderNo) === PURCHASE_ORDER_NO &&
        (Array.isArray(row.lines) ? row.lines : []).some((line) => {
          const item = asRecord(line)
          return (
            textOf(item.lineNo) === textOf(quoteLine.lineNo || '10') &&
            Number(item.receivedQuantity ?? 0) >= materialQuantity
          )
        }),
    )
    const receiptUi = await provePageSafely('purchase-order-receipt', {
      route: '/erp/procurement/receipts',
      listPath: '/api/business-console/v1/erp/procurement/purchase-orders',
      filterLabel: '采购收货关键字',
      stableText: PURCHASE_ORDER_NO,
      emptyText: '还没有可收货的采购订单。采购订单释放后会在这里跟进入库。',
      screenshotName: '06-purchase-receipt.png',
    })
    record({
      node: 'purchase-order-receipt',
      sourceObject: PURCHASE_ORDER_NO,
      downstreamObject: PURCHASE_RECEIPT_NO,
      stableKey: `${PURCHASE_ORDER_NO} -> ${PURCHASE_RECEIPT_NO}`,
      automationMode: 'manual',
      request: receipt.summary,
      responseOrLog: {
        receipt: receipt.publicPayload,
        purchaseOrder: publicJson(receiptOrder.match),
        ui: receiptUi,
      },
      conclusion: 'runtime-confirmed',
      demoWording:
        '采购收货公开接口以固定 PR-WALK-001 记账，采购收货页面以 HTTP 200 渲染同一 PO 行和已收数量。',
      responsibilityIssue: null,
    })

    const receiptScopes = await workerCall(
      'GET',
      queryPath('/api/business-console/v1/wms/work-scopes/receipts', {
        organizationId,
        environmentId,
      }),
    )
    const receiptScope: AuthorizedWorkPoolScope | undefined = selectAuthorizedWorkPoolScope(
      receiptScopes.payload,
      SITE_CODE,
    )
    const receiptReadScope: AuthorizedWorkSiteScope | undefined = selectAuthorizedWorkSiteScope(
      receiptScopes.payload,
      SITE_CODE,
    )
    if (!receiptScope || !receiptReadScope)
      throw new Error('WMS receipt scope catalog returned no authorized read or work-pool scope.')
    expect(textOf(asRecord(dataOf(receiptScopes.payload)).actorPrincipalId)).toBe(
      workerRuntime.principalId,
    )
    const receiptScopeKind = textOf(receiptScope.scopeKind).trim()
    const receiptScopeId = textOf(receiptScope.scopeId).trim()
    const receiptPoolCode = textOf(receiptScope.poolCode).trim()
    const receiptReadScopeKind = textOf(receiptReadScope.scopeKind).trim()
    const receiptReadScopeId = textOf(receiptReadScope.scopeId).trim()
    const receiptReadSiteCode = textOf(receiptReadScope.siteCode).trim()
    expect(receiptPoolCode).not.toBe('')
    expect(textOf(receiptScope.siteCode)).toBe(SITE_CODE)
    expect(receiptReadScopeKind.toLowerCase()).toBe('site')
    expect(receiptReadScopeId).toBe(receiptReadSiteCode)
    expect(receiptReadSiteCode).toBe(SITE_CODE)
    setup.push({
      kind: 'wms-scope-catalog',
      actor: workerRuntime.actor,
      principalId: workerRuntime.principalId,
      operation: 'receipts',
      source: 'authorized WarehouseWorkScopeCatalogItem',
      scope: publicJson(receiptScope),
      readScope: publicJson(receiptReadScope),
      request: receiptScopes.summary,
    })
    const inbound = await workerCall('POST', '/api/business-console/v1/wms/inbound-orders', {
      organizationId,
      environmentId,
      inboundOrderNo: INBOUND_ORDER_NO,
      sourceDocumentType: 'purchase-order',
      sourceDocumentId: PURCHASE_ORDER_NO,
      siteCode: SITE_CODE,
      lines: [
        {
          lineNo: textOf(quoteLine.lineNo || '10'),
          skuCode: materialSku,
          uomCode: materialUom,
          receivedQuantity: materialQuantity,
          stagingLocationCode: INBOUND_LOCATION,
          lotNo: 'LOT-WALK-RM-001',
          serialNo: null,
          qualityStatus: 'unrestricted',
          ownerType: 'company',
          ownerId: null,
        },
      ],
    })
    const inboundOrderId = textOf(asRecord(dataOf(inbound.payload)).inboundOrderId)
    if (!inboundOrderId) throw new Error(`WMS inbound ${INBOUND_ORDER_NO} did not return an ID.`)
    const inboundRow = await workerPollRows(
      '/api/business-console/v1/wms/inbound-orders',
      {
        organizationId,
        environmentId,
        keyword: INBOUND_ORDER_NO,
        scopeKind: receiptReadScopeKind,
        scopeId: receiptReadScopeId,
        siteCode: receiptReadSiteCode,
        skip: 0,
        take: 100,
      },
      (row) => textOf(row.inboundOrderNo) === INBOUND_ORDER_NO,
    )
    const inboundVersion = Number(inboundRow.match.version ?? 1)
    const noScopeInventoryQuery = {
      organizationId,
      environmentId,
      skuCode: materialSku,
      uomCode: materialUom,
      siteCode: SITE_CODE,
      locationCode: LINE_SIDE_LOCATION,
      lotNo: 'LOT-WALK-RM-001',
      qualityStatus: 'unrestricted',
      ownerType: 'company',
    }
    const inventoryBeforeNoScope = await workerCall(
      'GET',
      queryPath('/api/business-console/v1/inventory/availability', noScopeInventoryQuery),
    )
    const movementsBeforeNoScope = await workerCall(
      'GET',
      queryPath('/api/business-console/v1/inventory/movements', {
        organizationId,
        environmentId,
        sourceDocumentId: INBOUND_ORDER_NO,
        page: 1,
        pageSize: 100,
      }),
    )
    const noScopeCompletion = await workerCallExpecting(
      'POST',
      queryPath(
        `/api/business-console/v1/wms/inbound-orders/${encodeURIComponent(inboundOrderId)}/complete`,
        { organizationId, environmentId },
      ),
      {
        idempotencyKey: `issue1912-${INBOUND_ORDER_NO}-missing-scope`,
        lines: [{ lineNo: textOf(quoteLine.lineNo || '10'), lotNo: 'LOT-WALK-RM-001' }],
        expectedVersion: inboundVersion,
      },
      { code: 'missing-work-pool-assignment', message: 'missing-work-pool-assignment' },
    )
    const inboundAfterNoScope = await workerPollRows(
      '/api/business-console/v1/wms/inbound-orders',
      {
        organizationId,
        environmentId,
        keyword: INBOUND_ORDER_NO,
        scopeKind: receiptReadScopeKind,
        scopeId: receiptReadScopeId,
        siteCode: receiptReadSiteCode,
        skip: 0,
        take: 100,
      },
      (row) => textOf(row.inboundOrderNo) === INBOUND_ORDER_NO,
    )
    expect(Number(inboundAfterNoScope.match.version ?? 0)).toBe(inboundVersion)
    expect(textOf(inboundAfterNoScope.match.status).toLowerCase()).not.toBe('completed')
    const inventoryAfterNoScope = await workerCall(
      'GET',
      queryPath('/api/business-console/v1/inventory/availability', noScopeInventoryQuery),
    )
    const movementsAfterNoScope = await workerCall(
      'GET',
      queryPath('/api/business-console/v1/inventory/movements', {
        organizationId,
        environmentId,
        sourceDocumentId: INBOUND_ORDER_NO,
        page: 1,
        pageSize: 100,
      }),
    )
    const inventoryBeforeNoScopeFingerprint = inventoryStateFingerprint(
      inventoryBeforeNoScope.payload,
    )
    const inventoryAfterNoScopeFingerprint = inventoryStateFingerprint(
      inventoryAfterNoScope.payload,
    )
    const movementsBeforeNoScopeFingerprint = inventoryMovementFingerprint(
      movementsBeforeNoScope.payload,
    )
    const movementsAfterNoScopeFingerprint = inventoryMovementFingerprint(
      movementsAfterNoScope.payload,
    )
    expect(inventoryAfterNoScopeFingerprint).toEqual(inventoryBeforeNoScopeFingerprint)
    expect(movementsAfterNoScopeFingerprint).toEqual(movementsBeforeNoScopeFingerprint)
    const noScopeSideEffectProbe = {
      unchanged:
        JSON.stringify(inventoryAfterNoScopeFingerprint) ===
          JSON.stringify(inventoryBeforeNoScopeFingerprint) &&
        JSON.stringify(movementsAfterNoScopeFingerprint) ===
          JSON.stringify(movementsBeforeNoScopeFingerprint),
      inventoryAvailability: {
        path: inventoryBeforeNoScope.summary.path,
        before: inventoryBeforeNoScope.publicPayload,
        after: inventoryAfterNoScope.publicPayload,
        beforeFingerprint: inventoryBeforeNoScopeFingerprint,
        afterFingerprint: inventoryAfterNoScopeFingerprint,
      },
      inventoryMovements: {
        path: movementsBeforeNoScope.summary.path,
        before: movementsBeforeNoScope.publicPayload,
        after: movementsAfterNoScope.publicPayload,
        beforeFingerprint: movementsBeforeNoScopeFingerprint,
        afterFingerprint: movementsAfterNoScopeFingerprint,
      },
    }
    setup.push({
      kind: 'wms-no-scope-fail-closed',
      actor: workerRuntime.actor,
      principalId: workerRuntime.principalId,
      operation: 'inbound-complete',
      request: noScopeCompletion.summary,
      response: noScopeCompletion.publicPayload,
      publicError: noScopeCompletion.publicError,
      before: { version: inboundVersion, status: textOf(inboundRow.match.status) },
      after: {
        version: Number(inboundAfterNoScope.match.version ?? 0),
        status: textOf(inboundAfterNoScope.match.status),
      },
      sideEffect: false,
      sideEffectProbe: noScopeSideEffectProbe,
      scope: 'not-supplied',
    })
    const inboundAssignmentPlan = buildAuthorizedWorkPoolAssignment(
      {
        actor: workerRuntime.actor,
        principalId: workerRuntime.principalId,
      },
      receiptScope,
      inboundOrderId,
      `issue1912-${INBOUND_ORDER_NO}-assignment`,
      inboundVersion,
    )
    if (!inboundAssignmentPlan.called) {
      throw new Error(
        `WMS inbound ${INBOUND_ORDER_NO} cannot be assigned without an authorized work-pool scope: ${inboundAssignmentPlan.reason}`,
      )
    }
    const inboundAssignment = await workerCall(
      'POST',
      queryPath(
        `/api/business-console/v1/wms/inbound-orders/${encodeURIComponent(inboundAssignmentPlan.request.resourceId)}/assignment`,
        { organizationId, environmentId },
      ),
      inboundAssignmentPlan.request.body,
    )
    const inboundAssignmentData = asRecord(dataOf(inboundAssignment.payload))
    expect(textOf(inboundAssignmentData.resourceCategory)).toBe('inbound')
    expect(textOf(inboundAssignmentData.resourceId)).toBe(inboundOrderId)
    expect(textOf(inboundAssignmentData.siteCode)).toBe(receiptScope.siteCode)
    expect(textOf(inboundAssignmentData.poolCode)).toBe(receiptPoolCode)
    expect(textOf(inboundAssignmentData.operatorPrincipalId)).toBe(workerRuntime.principalId)
    expect(textOf(inboundAssignmentData.assignedByPrincipalId)).toBe(workerRuntime.principalId)
    const assignedInbound = await workerPollRows(
      '/api/business-console/v1/wms/inbound-orders',
      {
        organizationId,
        environmentId,
        keyword: INBOUND_ORDER_NO,
        scopeKind: receiptScopeKind,
        scopeId: receiptScopeId,
        skip: 0,
        take: 100,
      },
      (row) =>
        textOf(row.inboundOrderNo) === INBOUND_ORDER_NO &&
        textOf(row.assignedPoolCode) === receiptPoolCode &&
        textOf(row.assignedOperatorUserId) === workerRuntime.principalId,
    )
    const assignedInboundVersion = Number(assignedInbound.match.version ?? 0)
    expect(assignedInboundVersion).toBeGreaterThan(inboundVersion)
    setup.push({
      kind: 'wms-assignment',
      actor: workerRuntime.actor,
      principalId: workerRuntime.principalId,
      operation: 'inbound-order',
      resourceId: inboundOrderId,
      scope: publicJson(receiptScope),
      request: inboundAssignment.summary,
      response: inboundAssignment.publicPayload,
      bound: {
        poolCode: textOf(assignedInbound.match.assignedPoolCode),
        operatorPrincipalId: textOf(assignedInbound.match.assignedOperatorUserId),
        version: assignedInboundVersion,
      },
    })
    const putaway = await workerCall(
      'POST',
      queryPath(
        `/api/business-console/v1/wms/inbound-orders/${encodeURIComponent(inboundOrderId)}/putaway-tasks`,
        { organizationId, environmentId },
      ),
      {
        taskNo: PUTAWAY_TASK_NO,
        lineNo: textOf(quoteLine.lineNo || '10'),
        fromLocationCode: INBOUND_LOCATION,
        toLocationCode: LINE_SIDE_LOCATION,
        quantity: materialQuantity,
      },
    )
    const completedInbound = await workerCall(
      'POST',
      queryPath(
        `/api/business-console/v1/wms/inbound-orders/${encodeURIComponent(inboundOrderId)}/complete`,
        { organizationId, environmentId },
      ),
      {
        idempotencyKey: `issue1912-${INBOUND_ORDER_NO}-complete`,
        lines: [{ lineNo: textOf(quoteLine.lineNo || '10'), lotNo: 'LOT-WALK-RM-001' }],
        scopeKind: receiptScopeKind,
        scopeId: receiptScopeId,
        expectedVersion: assignedInboundVersion,
      },
    )
    const inventory = await workerPollData(
      '/api/business-console/v1/inventory/availability',
      {
        organizationId,
        environmentId,
        skuCode: materialSku,
        uomCode: materialUom,
        siteCode: SITE_CODE,
        locationCode: LINE_SIDE_LOCATION,
        lotNo: 'LOT-WALK-RM-001',
        qualityStatus: 'unrestricted',
        ownerType: 'company',
      },
      (data) => Number(data.availableQuantity ?? data.onHandQuantity ?? 0) >= materialQuantity,
    )
    const inboundUi = await provePageSafely('receipt-inbound-inventory', {
      actor: 'wms-worker',
      route: '/wms/inbound',
      listPath: '/api/business-console/v1/wms/inbound-orders',
      filterLabel: '关键字搜索',
      stableText: INBOUND_ORDER_NO,
      emptyText: '暂无入库单。收货作业产生入库单后会出现在这里。',
      screenshotName: '07-wms-inbound.png',
    })
    record({
      node: 'receipt-inbound-inventory',
      sourceObject: PURCHASE_RECEIPT_NO,
      downstreamObject: INBOUND_ORDER_NO,
      stableKey: `${PURCHASE_RECEIPT_NO} -> ${INBOUND_ORDER_NO} -> ${textOf(inventory.data.movementId ?? inventory.data.ledgerVersion)}`,
      automationMode: 'mixed',
      request: inbound.summary,
      responseOrLog: {
        inbound: inbound.publicPayload,
        putaway: putaway.publicPayload,
        completion: completedInbound.publicPayload,
        inventory: publicJson(inventory.data),
        ui: inboundUi,
      },
      conclusion: 'runtime-confirmed',
      demoWording:
        '真实 WMS 入库单在授权作业范围内完成，页面渲染 IN-WALK-001，库存公开可用量随后在 SITE-001/loc-line-01 出现。',
      responsibilityIssue: null,
    })

    const salesQuotation = await call(
      'GET',
      queryPath('/api/business-console/v1/erp/sales/quotations', {
        organizationId,
        environmentId,
        keyword: SALES_QUOTATION_NO,
        skip: 0,
        take: 100,
      }),
    )
    const quotationRow = rowsOf(salesQuotation.payload).find(
      (row) => textOf(row.quotationNo) === SALES_QUOTATION_NO,
    )
    if (!quotationRow || textOf(quotationRow.status).toLowerCase() !== 'approved')
      throw new Error(`Seed sales quotation ${SALES_QUOTATION_NO} was not approved.`)
    const salesQuotationUi = await provePageSafely('sales-quotation-sales-order', {
      route: '/erp/sales/quotations',
      listPath: '/api/business-console/v1/erp/sales/quotations',
      filterLabel: '报价单关键字',
      stableText: SALES_QUOTATION_NO,
      emptyText: '还没有报价单。可从销售机会或客户需求创建报价。',
      screenshotName: '08-sales-quotation.png',
    })
    const salesOrder = await call('POST', '/api/business-console/v1/erp/sales/sales-orders', {
      organizationId,
      environmentId,
      salesOrderNo: SALES_ORDER_NO,
      quotationNo: SALES_QUOTATION_NO,
      siteCode: SITE_CODE,
      idempotencyKey: `issue1912-${SALES_ORDER_NO}`,
    })
    const salesOrderRow = await pollRows(
      '/api/business-console/v1/erp/sales/sales-orders',
      {
        organizationId,
        environmentId,
        keyword: SALES_ORDER_NO,
        skip: 0,
        take: 100,
      },
      (row) => textOf(row.salesOrderNo) === SALES_ORDER_NO,
    )
    const salesOrderUi = await provePageSafely('sales-quotation-sales-order', {
      route: `/erp/sales/orders?keyword=${encodeURIComponent(SALES_ORDER_NO)}`,
      listPath: '/api/business-console/v1/erp/sales/sales-orders',
      filterLabel: '销售订单关键字',
      filterResponseMode: 'server',
      stableText: SALES_ORDER_NO,
      emptyText: '还没有销售订单。批准报价后可在这里生成订单。',
      screenshotName: '09-sales-order.png',
    })
    record({
      node: 'sales-quotation-sales-order',
      sourceObject: SALES_QUOTATION_NO,
      downstreamObject: SALES_ORDER_NO,
      stableKey: `${SALES_QUOTATION_NO} -> ${SALES_ORDER_NO}`,
      automationMode: 'manual',
      request: salesOrder.summary,
      responseOrLog: {
        quotation: publicJson(quotationRow),
        salesOrder: publicJson(salesOrderRow.match),
        ui: [salesQuotationUi, salesOrderUi],
      },
      conclusion: 'runtime-confirmed',
      demoWording:
        '销售报价页面证明 QUO-WALK-001 已批准，销售订单页面以 HTTP 200 渲染稳定 SO-WALK-001 行。',
      responsibilityIssue: null,
    })

    const demand = await pollRows(
      '/api/business-console/v1/planning/demands',
      { organizationId, environmentId },
      (row) => textOf(row.sourceReference) === SALES_ORDER_NO,
    )
    const demandUi = await provePageSafely('sales-order-demand', {
      route: '/planning',
      listPath: '/api/business-console/v1/planning/demands',
      filterLabel: '需求池关键字',
      filterResponseMode: 'client',
      stableText: SALES_ORDER_NO,
      emptyText: '当前范围没有计划需求。',
      screenshotName: '10-planning-demand.png',
    })
    record({
      node: 'sales-order-demand',
      sourceObject: SALES_ORDER_NO,
      downstreamObject: textOf(demand.match.demandSourceId),
      stableKey: `${SALES_ORDER_NO} -> ${textOf(demand.match.demandSourceId)}`,
      automationMode: 'automatic',
      request: demand.call.summary,
      responseOrLog: { demand: publicJson(demand.match), ui: demandUi },
      conclusion: 'runtime-confirmed',
      demoWording: '销售订单跨 Redis 后在 Planning 页面需求池出现同一 SO-WALK-001 来源行。',
      responsibilityIssue: null,
    })

    const horizonStart = dateOnly(new Date(generatedAtUtc.getTime() - 86_400_000))
    const mrp = await call('POST', '/api/business-console/v1/planning/mrp-runs', {
      organizationId,
      environmentId,
      horizonStart,
      horizonEnd: '2100-01-01',
    })
    const runId = textOf(asRecord(dataOf(mrp.payload)).runId)
    if (!runId) throw new Error('MRP run did not return a runId.')
    const pegging = await pollRows(
      `/api/business-console/v1/planning/mrp-runs/${encodeURIComponent(runId)}/pegging`,
      {
        organizationId,
        environmentId,
      },
      (row) => textOf(row.demandSourceReference) === SALES_ORDER_NO,
      120_000,
    )
    const suggestion = await pollRows(
      '/api/business-console/v1/planning/suggestions',
      { organizationId, environmentId },
      (row) =>
        textOf(row.runId) === runId &&
        textOf(row.suggestionType) === 'planned-work-order' &&
        textOf(row.skuCode) === FINISHED_SKU,
      120_000,
    )
    const suggestionUi = await provePageSafely('demand-mrp-suggestion', {
      route: '/planning',
      listPath: '/api/business-console/v1/planning/suggestions',
      stableText: FINISHED_SKU,
      tabText: /计划建议/,
      reuseCurrentRoute: true,
      refreshListBeforeProof: true,
      emptyText: '当前范围没有计划建议。',
      screenshotName: '11-planning-suggestion.png',
    })
    record({
      node: 'demand-mrp-suggestion',
      sourceObject: SALES_ORDER_NO,
      downstreamObject: textOf(suggestion.match.suggestionId),
      stableKey: `${SALES_ORDER_NO} -> ${runId} -> ${FINISHED_SKU}`,
      automationMode: 'manual',
      request: mrp.summary,
      responseOrLog: {
        mrp: mrp.publicPayload,
        pegging: publicJson(pegging.match),
        suggestion: publicJson(suggestion.match),
        ui: suggestionUi,
      },
      conclusion: 'runtime-confirmed',
      demoWording:
        'MRP 使用包含 2099-12-31 种子需求日期的公开窗口，保留 SO-WALK-001 pegging 并在真实计划建议页展示成品生产建议。',
      responsibilityIssue: null,
    })

    const mesReadContext = await call(
      'GET',
      queryPath('/api/business-console/v1/me/work-context', {
        organizationId,
        environmentId,
        permissionCode: 'business.mes.work-orders.read',
      }),
    )
    const mesReadData = asRecord(dataOf(mesReadContext.payload))
    const mesReadScope = asRecord(
      mesReadData.selectedScope ??
        (Array.isArray(mesReadData.authorizedScopes) ? mesReadData.authorizedScopes[0] : null),
    )
    const mesScopeKind = textOf(mesReadScope.kind ?? mesReadScope.scopeKind)
    const mesScopeId = textOf(mesReadScope.id ?? mesReadScope.scopeId)
    if (!mesScopeKind || !mesScopeId)
      throw new Error('MES work-order read context returned no authorized scope.')
    const accepted = await call(
      'POST',
      queryPath(
        `/api/business-console/v1/planning/suggestions/${encodeURIComponent(textOf(suggestion.match.suggestionId))}/accept`,
        { organizationId, environmentId },
      ),
      {
        downstreamService: 'BusinessMes',
        downstreamDocumentType: 'WorkOrder',
        downstreamDocumentId: null,
        idempotencyKey: `issue1912-accept-${textOf(suggestion.match.suggestionId)}`,
      },
    )
    const acceptedData = asRecord(dataOf(accepted.payload))
    const workOrderId = textOf(acceptedData.downstreamDocumentId)
    if (!workOrderId)
      throw new Error('Planning suggestion acceptance returned no MES work-order ID.')
    const workOrderDetail = await call(
      'GET',
      queryPath(`/api/business-console/v1/mes/work-orders/${encodeURIComponent(workOrderId)}`, {
        organizationId,
        environmentId,
        scopeKind: mesScopeKind,
        scopeId: mesScopeId,
      }),
    )
    const workOrder = asRecord(dataOf(workOrderDetail.payload))
    const sourcePlanReference = asRecord(workOrder.sourcePlanReference)
    if (textOf(sourcePlanReference.sourceDemandReference) !== SALES_ORDER_NO)
      throw new Error(`MES work order ${workOrderId} did not preserve ${SALES_ORDER_NO}.`)
    const workOrderNo = textOf(workOrder.workOrderNo || workOrderId)
    const workOrderUi = await provePageSafely('mrp-suggestion-mes-work-order', {
      route: `/mes/work-orders?keyword=${encodeURIComponent(workOrderNo)}`,
      listPath: '/api/business-console/v1/mes/work-orders',
      stableText: workOrderNo,
      emptyText: '当前筛选下没有工单。正常生产请先进入生产计划转工单，急单只处理临时插单。',
      screenshotName: '12-mes-work-order.png',
    })
    record({
      node: 'mrp-suggestion-mes-work-order',
      sourceObject: textOf(suggestion.match.suggestionId),
      downstreamObject: workOrderId,
      stableKey: `${SALES_ORDER_NO} -> ${workOrderNo} -> ${workOrderId}`,
      automationMode: 'automatic',
      request: accepted.summary,
      responseOrLog: { workOrder: publicJson(workOrder), ui: workOrderUi },
      conclusion: 'runtime-confirmed',
      demoWording:
        '计划建议接受后真实 MES 工单页渲染了生成的工单业务号，并通过详情公开关联回 SO-WALK-001。',
      responsibilityIssue: null,
    })

    const mesManageContext = await call(
      'GET',
      queryPath('/api/business-console/v1/me/work-context', {
        organizationId,
        environmentId,
        permissionCode: 'business.mes.work-orders.manage',
      }),
    )
    const mesManageData = asRecord(dataOf(mesManageContext.payload))
    const mesManageScope = asRecord(
      mesManageData.selectedScope ??
        (Array.isArray(mesManageData.authorizedScopes) ? mesManageData.authorizedScopes[0] : null),
    )
    const manageScopeKind = textOf(
      (mesManageScope.kind ?? mesManageScope.scopeKind) || mesScopeKind,
    )
    const manageScopeId = textOf((mesManageScope.id ?? mesManageScope.scopeId) || mesScopeId)
    await call(
      'POST',
      queryPath(
        `/api/business-console/v1/mes/work-orders/${encodeURIComponent(workOrderId)}/release`,
        { organizationId, environmentId, scopeKind: manageScopeKind, scopeId: manageScopeId },
      ),
      {
        confirmWarnings: true,
        idempotencyKey: `issue1912-release-${workOrderId}`,
      },
    )
    let releasedDetail = await pollData(
      `/api/business-console/v1/mes/work-orders/${encodeURIComponent(workOrderId)}`,
      { organizationId, environmentId, scopeKind: mesScopeKind, scopeId: mesScopeId },
      (data) => Array.isArray(data.operationTasks) && data.operationTasks.length > 0,
    )
    let operationTasks = (
      Array.isArray(releasedDetail.data.operationTasks) ? releasedDetail.data.operationTasks : []
    )
      .map(asRecord)
      .sort((a, b) => Number(a.operationSequence ?? 0) - Number(b.operationSequence ?? 0))
    if (operationTasks.length === 0)
      throw new Error(`Released work order ${workOrderNo} has no operation tasks.`)

    const reportContext = await call(
      'GET',
      queryPath('/api/business-console/v1/me/work-context', {
        organizationId,
        environmentId,
        permissionCode: 'business.mes.reporting.write',
      }),
    )
    const reportContextData = asRecord(dataOf(reportContext.payload))
    const reportScope = asRecord(
      reportContextData.selectedScope ??
        (Array.isArray(reportContextData.authorizedScopes)
          ? reportContextData.authorizedScopes[0]
          : null),
    )
    const reportScopeKind = textOf((reportScope.kind ?? reportScope.scopeKind) || mesScopeKind)
    const reportScopeId = textOf((reportScope.id ?? reportScope.scopeId) || mesScopeId)
    const reportFacts: JsonRecord[] = []
    for (const task of operationTasks) {
      const taskId = textOf(task.operationTaskId)
      if (!taskId) throw new Error(`Work order ${workOrderNo} exposed an operation without an ID.`)
      const taskStart = await call(
        'POST',
        queryPath(
          `/api/business-console/v1/mes/operation-tasks/${encodeURIComponent(taskId)}/start`,
          {
            organizationId,
            environmentId,
            scopeKind: manageScopeKind,
            scopeId: manageScopeId,
          },
        ),
        {
          reasonCode: 'manual-evidence-transition',
          idempotencyKey: `issue1912-start-${taskId}`,
        },
      )
      const reportRequest: JsonRecord = {
        organizationId,
        environmentId,
        workOrderId,
        operationTaskId: taskId,
        goodQuantity: QUANTITY,
        scrapQuantity: 0,
        completesOperation: true,
        reportedAtUtc: new Date().toISOString(),
        idempotencyKey: `issue1912-report-${taskId}`,
        scopeKind: reportScopeKind,
        scopeId: reportScopeId,
        consumedMaterialLots: [],
        reworkQuantity: 0,
      }
      if (task === operationTasks[operationTasks.length - 1])
        reportRequest.producedLotNo = PRODUCED_LOT_NO
      const report = await call(
        'POST',
        '/api/business-console/v1/mes/production-reports',
        reportRequest,
      )
      reportFacts.push({
        task: publicJson(task),
        start: taskStart.publicPayload,
        report: report.publicPayload,
      })
      releasedDetail = await pollData(
        `/api/business-console/v1/mes/work-orders/${encodeURIComponent(workOrderId)}`,
        { organizationId, environmentId, scopeKind: mesScopeKind, scopeId: mesScopeId },
        (data) => {
          const currentTask = (Array.isArray(data.operationTasks) ? data.operationTasks : [])
            .map(asRecord)
            .find((item) => textOf(item.operationTaskId) === taskId)
          return textOf(currentTask?.status).toLowerCase() === 'completed'
        },
        120_000,
      )
      operationTasks = (
        Array.isArray(releasedDetail.data.operationTasks) ? releasedDetail.data.operationTasks : []
      )
        .map(asRecord)
        .sort((a, b) => Number(a.operationSequence ?? 0) - Number(b.operationSequence ?? 0))
    }
    const productionReports = await pollRows(
      '/api/business-console/v1/mes/production-reports',
      { organizationId, environmentId, keyword: workOrderNo, skip: 0, take: 100 },
      (row) =>
        textOf(row.workOrderId) === workOrderId && textOf(row.producedLotNo) === PRODUCED_LOT_NO,
      120_000,
    )
    const productionUi = await provePageSafely('mes-work-order-production', {
      route: '/mes/production-reports',
      listPath: '/api/business-console/v1/mes/production-reports',
      stableText: workOrderNo,
      emptyText: '还没有报工记录。报工后这里会出现对应记录，去工序执行报工。',
      screenshotName: '13-mes-production-reports.png',
    })
    record({
      node: 'mes-work-order-production',
      sourceObject: workOrderNo,
      downstreamObject: textOf(
        productionReports.match.reportNo ?? productionReports.match.productionReportId,
      ),
      stableKey: `${SALES_ORDER_NO} -> ${workOrderNo} -> ${PRODUCED_LOT_NO}`,
      automationMode: 'mixed',
      request: null,
      responseOrLog: {
        reports: publicJson(productionReports.match),
        operationCount: reportFacts.length,
        facts: reportFacts,
        ui: productionUi,
      },
      conclusion: 'runtime-confirmed',
      demoWording:
        'MES 真实工单的全部工序通过公开 start/report 生命周期完成，最终报工产生固定成品批次；报工页面渲染工单业务号。',
      responsibilityIssue: null,
    })

    const finishedReceipt = await call(
      'POST',
      '/api/business-console/v1/mes/finished-goods-receipt-requests',
      {
        organizationId,
        environmentId,
        workOrderId,
        skuId: FINISHED_SKU,
        quantity: QUANTITY,
        uomCode: 'pcs',
        requestedAtUtc: new Date().toISOString(),
        idempotencyKey: `issue1912-receipt-${PRODUCED_LOT_NO}`,
        producedLotNo: PRODUCED_LOT_NO,
      },
    )
    const receiptRequestNo = textOf(asRecord(dataOf(finishedReceipt.payload)).requestNo)
    if (!receiptRequestNo)
      throw new Error(`Finished goods receipt for ${workOrderNo} did not return requestNo.`)
    const finishedReceiptRow = await pollRows(
      '/api/business-console/v1/mes/finished-goods-receipt-requests',
      { organizationId, environmentId, workOrderId, keyword: receiptRequestNo, skip: 0, take: 100 },
      (row) =>
        textOf(row.requestNo) === receiptRequestNo && textOf(row.producedLotNo) === PRODUCED_LOT_NO,
      180_000,
    )
    const receiptPageUi = await provePageSafely('production-finished-goods-receipt', {
      route: '/mes/receipts',
      listPath: '/api/business-console/v1/mes/finished-goods-receipt-requests',
      stableText: receiptRequestNo,
      emptyText: '还没有完工入库登记。末道工序报完工后，在此把成品登记入库即会出现对应记录。',
      screenshotName: '14-finished-goods-receipt.png',
    })
    const finishedInventory = await workerPollData(
      '/api/business-console/v1/inventory/availability',
      {
        organizationId,
        environmentId,
        skuCode: FINISHED_SKU,
        uomCode: 'pcs',
        siteCode: SITE_CODE,
        locationCode: FINISHED_GOODS_LOCATION,
        lotNo: PRODUCED_LOT_NO,
      },
      (data) => Number(data.onHandQuantity ?? 0) >= QUANTITY,
      180_000,
    )
    record({
      node: 'production-finished-goods-receipt',
      sourceObject: textOf(
        productionReports.match.reportNo ?? productionReports.match.productionReportId,
      ),
      downstreamObject: receiptRequestNo,
      stableKey: `${workOrderNo} -> ${receiptRequestNo} -> ${PRODUCED_LOT_NO}`,
      automationMode: 'manual',
      request: finishedReceipt.summary,
      responseOrLog: { receipt: publicJson(finishedReceiptRow.match), ui: receiptPageUi },
      conclusion: 'runtime-confirmed',
      demoWording:
        '最终报工通过公开完工入库请求登记固定批次，MES 完工入库页面以 HTTP 200 渲染 requestNo；随后 Inventory 公开可用量为正。',
      responsibilityIssue: null,
    })
    record({
      node: 'finished-goods-inventory',
      sourceObject: receiptRequestNo,
      downstreamObject: PRODUCED_LOT_NO,
      stableKey: `${receiptRequestNo} -> ${SITE_CODE}/${FINISHED_GOODS_LOCATION}/${PRODUCED_LOT_NO}`,
      automationMode: 'automatic',
      request: finishedInventory.call.summary,
      responseOrLog: {
        availability: publicJson(finishedInventory.data),
        receipt: publicJson(finishedReceiptRow.match),
        ui: await provePageSafely('finished-goods-inventory', {
          actor: 'wms-worker',
          route: queryPath('/inventory/availability', {
            skuCode: FINISHED_SKU,
            siteCode: SITE_CODE,
            locationCode: FINISHED_GOODS_LOCATION,
            lotNo: PRODUCED_LOT_NO,
          }),
          listPath: '/api/business-console/v1/inventory/availability',
          stableText: FINISHED_SKU,
          selectOptions: [
            { label: '质量状态', option: '全部状态' },
            { label: '货主类型', option: '生产领用' },
          ],
          emptyText: '没有查到库存明细。换个物料、工厂或库位再查一次。',
          screenshotName: '15-finished-goods-inventory.png',
        }),
      },
      conclusion: 'runtime-confirmed',
      demoWording:
        '完工入库跨边界落到 Inventory 的固定成品批次分区，公开 availability 证明库存为正。',
      responsibilityIssue: null,
    })

    const delivery = await call('POST', '/api/business-console/v1/erp/sales/delivery-orders', {
      organizationId,
      environmentId,
      deliveryOrderNo: DELIVERY_ORDER_NO,
      salesOrderNo: SALES_ORDER_NO,
      lines: [
        {
          salesOrderLineNo: '10',
          quantity: QUANTITY,
          locationCode: FINISHED_GOODS_LOCATION,
          lotNo: PRODUCED_LOT_NO,
        },
      ],
      idempotencyKey: `issue1912-${DELIVERY_ORDER_NO}`,
    })
    const deliveryRow = await pollRows(
      '/api/business-console/v1/erp/sales/delivery-orders',
      { organizationId, environmentId, keyword: DELIVERY_ORDER_NO, skip: 0, take: 100 },
      (row) => textOf(row.deliveryOrderNo) === DELIVERY_ORDER_NO,
    )
    const deliveryUi = await provePageSafely('sales-order-delivery', {
      route: '/erp/sales/deliveries',
      listPath: '/api/business-console/v1/erp/sales/delivery-orders',
      filterLabel: '发货关键字',
      stableText: DELIVERY_ORDER_NO,
      emptyText: '还没有发货单',
      screenshotName: '16-sales-delivery.png',
    })
    record({
      node: 'sales-order-delivery',
      sourceObject: SALES_ORDER_NO,
      downstreamObject: DELIVERY_ORDER_NO,
      stableKey: `${SALES_ORDER_NO} -> ${DELIVERY_ORDER_NO}`,
      automationMode: 'manual',
      request: delivery.summary,
      responseOrLog: { delivery: publicJson(deliveryRow.match), ui: deliveryUi },
      conclusion: 'runtime-confirmed',
      demoWording:
        '成品库存有正数后，公开 ERP 发货释放从同一 SO 生成固定 DO，销售发货页面渲染稳定业务编号。',
      responsibilityIssue: null,
    })

    const shipmentScopes = await workerCall(
      'GET',
      queryPath('/api/business-console/v1/wms/work-scopes/shipments', {
        organizationId,
        environmentId,
      }),
    )
    const shipmentScope: AuthorizedWorkPoolScope | undefined = selectAuthorizedWorkPoolScope(
      shipmentScopes.payload,
      SITE_CODE,
    )
    const shipmentReadScope: AuthorizedWorkSiteScope | undefined = selectAuthorizedWorkSiteScope(
      shipmentScopes.payload,
      SITE_CODE,
    )
    if (!shipmentScope || !shipmentReadScope)
      throw new Error('WMS shipment scope catalog returned no authorized read or work-pool scope.')
    expect(textOf(asRecord(dataOf(shipmentScopes.payload)).actorPrincipalId)).toBe(
      workerRuntime.principalId,
    )
    const shipmentScopeKind = textOf(shipmentScope.scopeKind).trim()
    const shipmentScopeId = textOf(shipmentScope.scopeId).trim()
    const shipmentPoolCode = textOf(shipmentScope.poolCode).trim()
    const shipmentReadScopeKind = textOf(shipmentReadScope.scopeKind).trim()
    const shipmentReadScopeId = textOf(shipmentReadScope.scopeId).trim()
    const shipmentReadSiteCode = textOf(shipmentReadScope.siteCode).trim()
    expect(shipmentPoolCode).not.toBe('')
    expect(textOf(shipmentScope.siteCode)).toBe(SITE_CODE)
    expect(shipmentReadScopeKind.toLowerCase()).toBe('site')
    expect(shipmentReadScopeId).toBe(shipmentReadSiteCode)
    expect(shipmentReadSiteCode).toBe(SITE_CODE)
    setup.push({
      kind: 'wms-scope-catalog',
      actor: workerRuntime.actor,
      principalId: workerRuntime.principalId,
      operation: 'shipments',
      source: 'authorized WarehouseWorkScopeCatalogItem',
      scope: publicJson(shipmentScope),
      readScope: publicJson(shipmentReadScope),
      request: shipmentScopes.summary,
    })
    const outbound = await workerPollRows(
      '/api/business-console/v1/wms/outbound-orders',
      {
        organizationId,
        environmentId,
        keyword: DELIVERY_ORDER_NO,
        scopeKind: shipmentReadScopeKind,
        scopeId: shipmentReadScopeId,
        siteCode: shipmentReadSiteCode,
        skip: 0,
        take: 100,
      },
      (row) => textOf(row.outboundOrderNo) === DELIVERY_ORDER_NO,
      180_000,
    )
    const outboundId = textOf(outbound.match.outboundOrderId)
    const outboundVersion = Number(outbound.match.version ?? 1)
    const outboundAssignmentPlan = buildAuthorizedWorkPoolAssignment(
      {
        actor: workerRuntime.actor,
        principalId: workerRuntime.principalId,
      },
      shipmentScope,
      outboundId,
      `issue1912-${DELIVERY_ORDER_NO}-assignment`,
      outboundVersion,
    )
    if (!outboundAssignmentPlan.called) {
      throw new Error(
        `WMS outbound ${DELIVERY_ORDER_NO} cannot be assigned without an authorized work-pool scope: ${outboundAssignmentPlan.reason}`,
      )
    }
    const outboundAssignment = await workerCall(
      'POST',
      queryPath(
        `/api/business-console/v1/wms/outbound-orders/${encodeURIComponent(outboundAssignmentPlan.request.resourceId)}/assignment`,
        { organizationId, environmentId },
      ),
      outboundAssignmentPlan.request.body,
    )
    const outboundAssignmentData = asRecord(dataOf(outboundAssignment.payload))
    expect(textOf(outboundAssignmentData.resourceCategory)).toBe('outbound')
    expect(textOf(outboundAssignmentData.resourceId)).toBe(outboundId)
    expect(textOf(outboundAssignmentData.siteCode)).toBe(shipmentScope.siteCode)
    expect(textOf(outboundAssignmentData.poolCode)).toBe(shipmentPoolCode)
    expect(textOf(outboundAssignmentData.operatorPrincipalId)).toBe(workerRuntime.principalId)
    expect(textOf(outboundAssignmentData.assignedByPrincipalId)).toBe(workerRuntime.principalId)
    const assignedOutbound = await workerPollRows(
      '/api/business-console/v1/wms/outbound-orders',
      {
        organizationId,
        environmentId,
        keyword: DELIVERY_ORDER_NO,
        scopeKind: shipmentScopeKind,
        scopeId: shipmentScopeId,
        skip: 0,
        take: 100,
      },
      (row) =>
        textOf(row.outboundOrderNo) === DELIVERY_ORDER_NO &&
        textOf(row.assignedPoolCode) === shipmentPoolCode &&
        textOf(row.assignedOperatorUserId) === workerRuntime.principalId,
      180_000,
    )
    const assignedOutboundVersion = Number(assignedOutbound.match.version ?? 0)
    expect(assignedOutboundVersion).toBeGreaterThan(outboundVersion)
    setup.push({
      kind: 'wms-assignment',
      actor: workerRuntime.actor,
      principalId: workerRuntime.principalId,
      operation: 'outbound-order',
      resourceId: outboundId,
      scope: publicJson(shipmentScope),
      request: outboundAssignment.summary,
      response: outboundAssignment.publicPayload,
      bound: {
        poolCode: textOf(assignedOutbound.match.assignedPoolCode),
        operatorPrincipalId: textOf(assignedOutbound.match.assignedOperatorUserId),
        version: assignedOutboundVersion,
      },
    })
    const outboundUi = await provePageSafely('delivery-wms-outbound', {
      actor: 'wms-worker',
      route: '/wms/outbound',
      listPath: '/api/business-console/v1/wms/outbound-orders',
      filterLabel: '关键字搜索',
      stableText: DELIVERY_ORDER_NO,
      emptyText: '暂无出库单。发货作业产生出库单后会出现在这里。',
      screenshotName: '17-wms-outbound.png',
    })
    record({
      node: 'delivery-wms-outbound',
      sourceObject: DELIVERY_ORDER_NO,
      downstreamObject: textOf(assignedOutbound.match.outboundOrderId),
      stableKey: `${DELIVERY_ORDER_NO} -> ${textOf(assignedOutbound.match.outboundOrderNo)}`,
      automationMode: 'automatic',
      request: outbound.call.summary,
      responseOrLog: { outbound: publicJson(assignedOutbound.match), ui: outboundUi },
      conclusion: 'runtime-confirmed',
      demoWording:
        'ERP 发货释放跨 Redis 生成 WMS 出库单，授权作业池绑定后真实出库页面以 HTTP 200 渲染 DO-WALK-001。',
      responsibilityIssue: null,
    })
    const completedOutbound = await workerCall(
      'POST',
      queryPath(
        `/api/business-console/v1/wms/outbound-orders/${encodeURIComponent(outboundId)}/complete`,
        { organizationId, environmentId },
      ),
      {
        packReviewNo: PACK_REVIEW_NO,
        passed: true,
        idempotencyKey: `issue1912-complete-${DELIVERY_ORDER_NO}`,
        scopeKind: shipmentScopeKind,
        scopeId: shipmentScopeId,
        expectedVersion: assignedOutboundVersion,
      },
    )
    const completedDelivery = await pollRows(
      '/api/business-console/v1/erp/sales/delivery-orders',
      { organizationId, environmentId, keyword: DELIVERY_ORDER_NO, skip: 0, take: 100 },
      (row) =>
        textOf(row.deliveryOrderNo) === DELIVERY_ORDER_NO &&
        textOf(row.status).toLowerCase() === 'completed',
      180_000,
    )
    record({
      node: 'wms-completed-erp-delivery',
      sourceObject: outboundId,
      downstreamObject: DELIVERY_ORDER_NO,
      stableKey: `${outboundId} -> ${DELIVERY_ORDER_NO} -> completed`,
      automationMode: 'automatic',
      request: completedOutbound.summary,
      responseOrLog: {
        completedOutbound: completedOutbound.publicPayload,
        delivery: publicJson(completedDelivery.match),
      },
      conclusion: 'runtime-confirmed',
      demoWording: 'WMS 出库在真实作业范围内完成并以公开 ERP 读面证明对应 DO 已 completed。',
      responsibilityIssue: null,
    })
    const receivable = await pollRows(
      '/api/business-console/v1/erp/finance/receivables',
      { organizationId, environmentId, keyword: DELIVERY_ORDER_NO, skip: 0, take: 100 },
      (row) => textOf(row.sourceDocumentNo) === DELIVERY_ORDER_NO,
      180_000,
    )
    const receivableNo = textOf(
      receivable.match.receivableNo || receivable.match.accountReceivableNo,
    )
    if (!receivableNo)
      throw new Error(`Delivery ${DELIVERY_ORDER_NO} produced no stable receivable number.`)
    const arUi = await provePageSafely('erp-account-receivable', {
      route: '/erp/finance/ar-ap',
      listPath: '/api/business-console/v1/erp/finance/receivables',
      filterLabel: '应收关键字',
      stableText: DELIVERY_ORDER_NO,
      emptyText: '还没有应收账款。销售出货或手工登记后会在这里形成应收。',
      screenshotName: '18-account-receivable.png',
    })
    record({
      node: 'erp-account-receivable',
      sourceObject: DELIVERY_ORDER_NO,
      downstreamObject: receivableNo,
      stableKey: `${DELIVERY_ORDER_NO} -> ${receivableNo}`,
      automationMode: 'automatic',
      request: receivable.call.summary,
      responseOrLog: { receivable: publicJson(receivable.match), ui: arUi },
      conclusion: 'runtime-confirmed',
      demoWording:
        '只有完成 WMS 出库后，ERP 应收读面以 HTTP 200 渲染由同一 DO 生成的稳定应收单号。',
      responsibilityIssue: null,
    })
  } catch (error) {
    const firstUnverified = REQUIRED_NODES.find(
      (node) => evidence.get(node)?.conclusion === 'not-verified',
    )
    if (firstUnverified) markFailure(firstUnverified, error, 'mixed')
    throw error
  } finally {
    const entries = REQUIRED_NODES.map((node) => evidence.get(node)!)
    try {
      await withSessionCredentialCleanup(
        () =>
          writeFile(
            evidencePath!,
            JSON.stringify(
              {
                issue: 'GitHub #1912 / NERV-1127',
                generatedAtUtc: generatedAtUtc.toISOString(),
                organizationId,
                environmentId,
                adminPrincipalId: principalId,
                workerPrincipalId,
                rfqNo: RFQ_NO,
                supplierQuotationNo: SUPPLIER_QUOTATION_NO,
                salesQuotationNo: SALES_QUOTATION_NO,
                purchaseOrderNo: PURCHASE_ORDER_NO,
                purchaseReceiptNo: PURCHASE_RECEIPT_NO,
                salesOrderNo: SALES_ORDER_NO,
                deliveryOrderNo: DELIVERY_ORDER_NO,
                runtimeProfileSource: runtimeProfileSource ?? 'not-supplied',
                transport: transport ?? 'not-supplied',
                persistence: persistence ?? 'not-supplied',
                worldEnabled: worldEnabled ?? 'not-supplied',
                historyEnabled: historyEnabled ?? 'not-supplied',
                scaleOrderCount: scaleOrderCount ?? 'not-supplied',
                assertionBoundary:
                  'public BusinessGateway HTTP plus rendered browser pages in two isolated ERP/WMS contexts; no database reads as business assertions',
                requestFailurePolicy:
                  'Only ERR_ABORTED document/resource requests, plus fetch/xhr API requests observed before a confirmed navigation or a confirmed inactive/hidden tab panel whose prior slot content disappeared, are separated as expected cancellations; the evidence window closes immediately after the transition. API aborts without that evidence, including requests started after the transition or reported after completion, API HTTP errors, other navigation failures, and other resource failures remain fail-closed. Client-side planning demand filtering proves the rendered row without a second list wait, while same-route suggestion proof refreshes its completed list before confirming tab unmount.',
                setup,
                identityIsolation: setup.find((item) => item.kind === 'identityIsolation') ?? null,
                expectedBusinessRejections,
                uiEvidence,
                failedRequests,
                expectedRequestCancellations,
                pageErrors,
                entries,
                summary: Object.fromEntries(
                  (['runtime-confirmed', 'gap', 'not-verified'] as const).map((conclusion) => [
                    conclusion,
                    entries.filter((entry) => entry.conclusion === conclusion).length,
                  ]),
                ),
                conclusion:
                  entries.every((entry) => entry.conclusion === 'runtime-confirmed') &&
                  failedRequests.length === 0 &&
                  pageErrors.length === 0
                    ? 'runtime-confirmed'
                    : 'not-verified',
              },
              null,
              2,
            ),
            'utf8',
          ),
        () => {
          sessionCredentialTracker.clear()
          workerSessionCredentialTracker.clear()
        },
      )
    } finally {
      await workerContext?.close()
    }
  }

  const entries = REQUIRED_NODES.map((node) => evidence.get(node)!)
  expect(
    entries
      .filter((entry) => entry.conclusion !== 'runtime-confirmed')
      .map((entry) => ({ node: entry.node, conclusion: entry.conclusion })),
    'all #1912 walkthrough nodes must be runtime-confirmed through public HTTP and rendered pages',
  ).toEqual([])
  expect([...new Set(uiEvidence.map((proof) => proof.node))].sort()).toEqual(
    [...REQUIRED_NODES].sort(),
  )
  expect(failedRequests, 'the real browser run must not leave failed requests').toEqual([])
  expect(pageErrors, 'the real browser run must not leave page errors').toEqual([])
  expect(expectedBusinessRejections).toHaveLength(1)
  expect(expectedBusinessRejections[0]).toMatchObject({
    actor: 'wms-worker',
    principalId: 'user-emp-049',
    status: 403,
    publicError: {
      code: 'missing-work-pool-assignment',
      message: 'missing-work-pool-assignment',
    },
  })
})
