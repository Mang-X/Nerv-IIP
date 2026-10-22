import {
  expect,
  type Browser,
  type BrowserContext,
  type Page,
  type Response,
} from '@playwright/test'
import {
  callWithSessionCredential,
  createSessionCredentialTracker,
} from './session-credential-tracker'
import { runWithActorContext, extractPublicError } from './issue1912-walkthrough-runtime'
import {
  classifyRequestFailure,
  RequestFailureEvidenceTracker,
} from './issue1912-walkthrough-policy'
import {
  asRecord,
  dataOf,
  rowsOf,
  textOf,
  jsonOf,
  publicJson,
  safeText,
  errorText,
  credentialDigest,
  failureDetail,
  PublicCallError,
  PollTimeoutError,
  type JsonRecord,
  type WalkthroughActor,
} from './issue1912-walkthrough-evidence'
import { queryPath as canonicalQueryPath } from './issue1912-walkthrough-query'

type SessionCredentialTracker = ReturnType<typeof createSessionCredentialTracker>

export type ActorRuntime = {
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

export async function createWalkthroughSession({
  page,
  browser,
  baseURL,
  setup,
  failedRequests,
  expectedRequestCancellations,
  expectedBusinessRejections,
  pageErrors,
}: {
  page: Page
  browser: Browser
  baseURL: string
  setup: JsonRecord[]
  failedRequests: JsonRecord[]
  expectedRequestCancellations: JsonRecord[]
  expectedBusinessRejections: JsonRecord[]
  pageErrors: string[]
}) {
  const queryPath = (path: string, query: JsonRecord) => canonicalQueryPath(path, query, baseURL)
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
          failure:
            request.failure()?.errorText === 'net::ERR_ABORTED'
              ? 'net::ERR_ABORTED'
              : failureDetail(request.failure()?.errorText),
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
      pageErrors.push(`${runtime.actor}: ${failureDetail(error.message)}`),
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
  const workerPollData = (
    path: string,
    query: JsonRecord,
    predicate: (data: JsonRecord) => boolean,
    timeoutMs = 90_000,
  ) => pollDataFor(workerRuntime, path, query, predicate, timeoutMs)

  const login = async (adminPassword: string, workerPassword: string) => {
    let organizationId = '',
      environmentId = '',
      principalId = '',
      principalType = '',
      workerPrincipalId = '',
      workerPrincipalType = ''
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
    const workerBusinessRequest = workerPage.waitForRequest(
      (request) => {
        const path = new URL(request.url()).pathname
        return path.startsWith('/api/business-console/') && Boolean(request.headers().authorization)
      },
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

    return { organizationId, environmentId, principalId, workerPrincipalId }
  }
  return {
    adminRuntime,
    workerRuntime,
    workerPage,
    sessionCredentialTracker,
    workerSessionCredentialTracker,
    workerContext,
    login,
    call,
    workerCall,
    workerCallExpecting,
    pollRows,
    workerPollRows,
    workerPollData,
  }
}
