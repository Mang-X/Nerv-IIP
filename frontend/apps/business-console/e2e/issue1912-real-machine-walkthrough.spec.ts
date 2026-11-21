import {
  baseURL,
  type WalkthroughContext,
  RFQ_NO,
  SUPPLIER_QUOTATION_NO,
  SALES_QUOTATION_NO,
  PURCHASE_ORDER_NO,
  SALES_ORDER_NO,
  DELIVERY_ORDER_NO,
} from './walkthrough-steps/context'
import { test, type Response, expect } from '@playwright/test'
import {
  type JsonRecord,
  safeText,
  textOf,
  createWalkthroughEvidence,
  REQUIRED_NODES,
} from './issue1912-walkthrough-evidence'
import { classifyRequestFailure } from './issue1912-walkthrough-policy'
import { dirname, join } from 'node:path'
import { mkdir } from 'node:fs/promises'
import { createWalkthroughSession } from './issue1912-walkthrough-session'
import { createWalkthroughPageProofs } from './issue1912-walkthrough-pages'
import { runProcurement } from './walkthrough-steps/procurement'
import { runInbound } from './walkthrough-steps/inbound'
import { runPlanning } from './walkthrough-steps/planning'
import { runProduction } from './walkthrough-steps/production'
import { runOutbound } from './walkthrough-steps/outbound'
import { withSessionCredentialCleanup } from './session-credential-tracker'

const adminPassword = process.env.NERV_IIP_FULLSTACK_ADMIN_PASSWORD
const workerPassword = process.env.NERV_IIP_FULLSTACK_WMS_WORKER_PASSWORD
const evidencePath = process.env.NERV_IIP_ISSUE_1912_EVIDENCE_PATH
const runtimeProfileSource = process.env.NERV_IIP_ISSUE_1912_RUNTIME_PROFILE_SOURCE
const transport = process.env.NERV_IIP_ISSUE_1912_TRANSPORT
const persistence = process.env.NERV_IIP_ISSUE_1912_PERSISTENCE
const walkthroughSeedEnabled = process.env.NERV_IIP_ISSUE_1912_WALKTHROUGH_SEED_ENABLED

const requiresManagedSession = !baseURL || !adminPassword || !workerPassword || !evidencePath

test.setTimeout(25 * 60 * 1000)
test.describe.configure({ mode: 'serial' })
// 我方收货单号由 ERP 编码规则生成（#3900），收货后从响应读回。
let purchaseReceiptNo = ''

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
  let workerPrincipalId = ''
  const ledger = createWalkthroughEvidence()
  const {
    evidence,
    markFailure,
    setup,
    uiEvidence,
    failedRequests,
    expectedRequestCancellations,
    expectedBusinessRejections,
    pageErrors,
  } = ledger

  const session = await createWalkthroughSession({
    page,
    browser,
    baseURL: baseURL!,
    setup,
    failedRequests,
    expectedRequestCancellations,
    expectedBusinessRejections,
    pageErrors,
  })
  const {
    adminRuntime,
    workerRuntime,
    sessionCredentialTracker,
    workerSessionCredentialTracker,
    workerContext,
  } = session

  const erpListQuery = (query: JsonRecord = {}): JsonRecord => ({
    organizationId,
    environmentId,
    skip: 0,
    take: 10,
    ...query,
  })

  const proofs = createWalkthroughPageProofs({
    adminRuntime,
    workerRuntime,
    screenshotDirectory,
    uiEvidence,
    markFailure,
    baseURL: baseURL!,
  })

  try {
    ;({ organizationId, environmentId, principalId, workerPrincipalId } = await session.login(
      adminPassword!,
      workerPassword!,
    ))

    const context: WalkthroughContext = {
      browser,
      page,
      organizationId,
      environmentId,
      principalId,
      generatedAtUtc,
      evidenceDirectory,
      screenshotDirectory,
      session,
      workerPassword: workerPassword!,
      ledger,
      proofs,
      erpListQuery,
      setPurchaseReceiptNo: (value) => {
        purchaseReceiptNo = value
      },
    }
    const procurement = await runProcurement(context)
    await runInbound(context, procurement)
    const workOrder = await runPlanning(context)
    const producedLotNo = await runProduction(context, workOrder)
    await runOutbound(context, producedLotNo)
  } catch (error) {
    const firstUnverified = REQUIRED_NODES.find(
      (node) => evidence.get(node)?.conclusion === 'not-verified',
    )
    if (firstUnverified) markFailure(firstUnverified, error, 'mixed')
    throw error
  } finally {
    try {
      await withSessionCredentialCleanup(
        () =>
          ledger.write(evidencePath!, {
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
            purchaseReceiptNo,
            salesOrderNo: SALES_ORDER_NO,
            deliveryOrderNo: DELIVERY_ORDER_NO,
            runtimeProfileSource: runtimeProfileSource ?? 'not-supplied',
            transport: transport ?? 'not-supplied',
            persistence: persistence ?? 'not-supplied',
            walkthroughSeedEnabled: walkthroughSeedEnabled ?? 'not-supplied',
            assertionBoundary:
              'public BusinessGateway HTTP plus rendered browser pages in two isolated ERP/WMS contexts; no database reads as business assertions',
            requestFailurePolicy:
              'Only ERR_ABORTED document/resource requests, plus fetch/xhr API requests observed before a confirmed navigation or a confirmed inactive/hidden tab panel whose prior slot content disappeared, are separated as expected cancellations; the evidence window closes immediately after the transition. API aborts without that evidence, including requests started after the transition or reported after completion, API HTTP errors, other navigation failures, and other resource failures remain fail-closed. Client-side planning demand filtering proves the rendered row without a second list wait, while same-route suggestion proof refreshes its completed list before confirming tab unmount.',
          }),
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
