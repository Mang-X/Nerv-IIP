import { readFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'
import {
  callWithSessionCredential,
  createSessionCredentialTracker,
  withSessionCredentialCleanup,
} from '../e2e/session-credential-tracker'

const scenarioSource = readFileSync(
  resolve(
    dirname(fileURLToPath(import.meta.url)),
    '../e2e/issue1912-real-machine-walkthrough.spec.ts',
  ),
  'utf8',
)

const trackerScope = (page: object) => ({
  origin: 'https://console.fixture',
  page,
  businessPathPrefix: '/api/business-console/',
  refreshPath: '/api/console/v1/auth/refresh',
})

const requestSource = (
  page: object,
  path: string,
  authorization: string,
  origin = 'https://console.fixture',
) => ({
  page,
  request: {
    url: () => `${origin}${path}`,
    headers: () => ({ authorization }),
  },
})

describe('NERV-1127 / GitHub #1912 real-machine walkthrough contract', () => {
  it('uses the access token from a successful refresh response for the next call', async () => {
    const page = {}
    const tracker = createSessionCredentialTracker(trackerScope(page))

    tracker.observeRequest(
      requestSource(page, '/api/business-console/v1/master-data/skus', 'fixture-before-refresh'),
    )
    let resolveRefreshBody: (body: unknown) => void = () => undefined
    const refreshBody = new Promise<unknown>((resolve) => {
      resolveRefreshBody = resolve
    })
    const refreshCapture = tracker.observeRefreshResponse({
      page,
      response: {
        url: () => 'https://console.fixture/api/console/v1/auth/refresh',
        status: () => 200,
        json: () => refreshBody,
      },
    })
    const nextCall = callWithSessionCredential(tracker, async (headers) => headers)
    resolveRefreshBody({ data: { accessToken: 'fixture-after-refresh' } })

    await refreshCapture
    await expect(nextCall).resolves.toEqual({
      authorization: 'Bearer fixture-after-refresh',
    })
    expect(scenarioSource).toContain('sessionCredentialTracker.observeRequest')
    expect(scenarioSource).toContain('observeRefreshResponse')
    expect(scenarioSource).toContain('callWithSessionCredential')
  })

  it('clears the credential when evidence writing fails', async () => {
    const page = {}
    const tracker = createSessionCredentialTracker(trackerScope(page))

    tracker.observeRequest(
      requestSource(page, '/api/business-console/v1/master-data/skus', 'fixture-current'),
    )

    await expect(
      withSessionCredentialCleanup(
        () => Promise.reject(new Error('fixture evidence write failed')),
        () => tracker.clear(),
      ),
    ).rejects.toThrow('fixture evidence write failed')
    await expect(tracker.headers()).resolves.toBeUndefined()
    expect(scenarioSource).toContain('withSessionCredentialCleanup')
  })

  it('ignores credentials from another page, origin, or path', async () => {
    const page = {}
    const otherPage = {}
    const tracker = createSessionCredentialTracker(trackerScope(page))

    tracker.observeRequest(
      requestSource(otherPage, '/api/business-console/v1/master-data/skus', 'fixture-other-page'),
    )
    tracker.observeRequest(
      requestSource(
        page,
        '/api/business-console/v1/master-data/skus',
        'fixture-other-origin',
        'https://other.fixture',
      ),
    )
    tracker.observeRequest(
      requestSource(page, '/api/console/v1/auth/refresh', 'fixture-wrong-path'),
    )
    await tracker.observeRefreshResponse({
      page,
      response: {
        url: () => 'https://other.fixture/api/console/v1/auth/refresh',
        status: () => 200,
        json: async () => ({ data: { accessToken: 'fixture-other-origin-refresh' } }),
      },
    })
    await tracker.observeRefreshResponse({
      page,
      response: {
        url: () => 'https://console.fixture/api/business-console/v1/master-data/skus',
        status: () => 200,
        json: async () => ({ data: { accessToken: 'fixture-wrong-path-refresh' } }),
      },
    })
    await expect(tracker.headers()).resolves.toBeUndefined()

    tracker.observeRequest(
      requestSource(page, '/api/business-console/v1/master-data/skus', 'fixture-current'),
    )
    await tracker.observeRefreshResponse({
      page: otherPage,
      response: {
        url: () => 'https://console.fixture/api/console/v1/auth/refresh',
        status: () => 200,
        json: async () => ({ data: { accessToken: 'fixture-other-page-refresh' } }),
      },
    })
    tracker.observeRequest(
      requestSource(otherPage, '/api/business-console/v1/master-data/skus', 'fixture-other-page-2'),
    )

    await expect(tracker.headers()).resolves.toEqual({ authorization: 'fixture-current' })
  })

  it.each([
    {
      label: 'non-200 response',
      status: 401,
      payload: { data: { accessToken: 'fixture-stale-token' } },
    },
    {
      label: 'malformed envelope',
      status: 200,
      payload: { result: { token: 'fixture-malformed-token' } },
    },
    {
      label: 'missing access token',
      status: 200,
      payload: { data: {} },
    },
  ])('fails closed after a $label refresh', async ({ status, payload }) => {
    const page = {}
    const tracker = createSessionCredentialTracker(trackerScope(page))
    tracker.observeRequest(
      requestSource(page, '/api/business-console/v1/master-data/skus', 'fixture-before-failure'),
    )

    await expect(
      tracker.observeRefreshResponse({
        page,
        response: {
          url: () => 'https://console.fixture/api/console/v1/auth/refresh',
          status: () => status,
          json: async () => payload,
        },
      }),
    ).rejects.toThrow()
    await expect(tracker.headers()).resolves.toBeUndefined()

    let operationInvoked = false
    await expect(
      callWithSessionCredential(tracker, async (headers) => {
        operationInvoked = true
        return headers
      }),
    ).rejects.toThrow('session credential unavailable')
    expect(operationInvoked).toBe(false)
  })

  it('does not repopulate credentials from a response observed after clear', async () => {
    const page = {}
    const tracker = createSessionCredentialTracker(trackerScope(page))

    await withSessionCredentialCleanup(
      async () => undefined,
      () => tracker.clear(),
    )
    await tracker.observeRefreshResponse({
      page,
      response: {
        url: () => 'https://console.fixture/api/console/v1/auth/refresh',
        status: () => 200,
        json: async () => ({ data: { accessToken: 'fixture-late-token' } }),
      },
    })

    await expect(tracker.headers()).resolves.toBeUndefined()
  })

  it('starts only from the reserved walkthrough facts and keeps downstream numbers stable', () => {
    expect(scenarioSource).toContain("const RFQ_NO = 'RFQ-WALK-001'")
    expect(scenarioSource).toContain("const SUPPLIER_QUOTATION_NO = 'SQ-WALK-001'")
    expect(scenarioSource).toContain("const SALES_QUOTATION_NO = 'QUO-WALK-001'")
    expect(scenarioSource).toContain("const PURCHASE_ORDER_NO = 'PO-WALK-001'")
    expect(scenarioSource).toContain("const PURCHASE_RECEIPT_NO = 'PR-WALK-001'")
    expect(scenarioSource).toContain("const SALES_ORDER_NO = 'SO-WALK-001'")
    expect(scenarioSource).toContain("const DELIVERY_ORDER_NO = 'DO-WALK-001'")
    expect(scenarioSource).not.toContain('/api/business-console/v1/approval/templates')
  })

  it('uses public business writes for the two chains and records every cross-boundary identifier', () => {
    expect(scenarioSource).toContain('/api/business-console/v1/erp/procurement/supplier-quotations')
    expect(scenarioSource).toContain('/api/business-console/v1/erp/procurement/purchase-orders')
    expect(scenarioSource).toContain('/api/business-console/v1/approval/chains')
    expect(scenarioSource).toContain(
      "PURCHASE_ORDER_APPROVAL_TEMPLATE_CODE = 'purchase-order-release'",
    )
    expect(scenarioSource).toContain('approvalChainDetail')
    expect(scenarioSource).toContain("approverRef) !== 'user-admin'")
    expect(scenarioSource).toContain('/api/business-console/v1/erp/procurement/purchase-receipts')
    expect(scenarioSource).toContain('/api/business-console/v1/erp/sales/sales-orders')
    expect(scenarioSource).toContain('/api/business-console/v1/planning/demands')
    expect(scenarioSource).toContain('/api/business-console/v1/planning/mrp-runs')
    expect(scenarioSource).toContain('/api/business-console/v1/planning/suggestions')
    expect(scenarioSource).toContain('/api/business-console/v1/mes/work-orders')
    expect(scenarioSource).toContain('/api/business-console/v1/erp/sales/delivery-orders')
    expect(scenarioSource).toContain('/api/business-console/v1/erp/finance/receivables')
    expect(scenarioSource).toContain('stableKey')
    expect(scenarioSource).toContain('sourceObject')
    expect(scenarioSource).toContain('downstreamObject')
  })

  it('proves real page responses and rendered rows instead of treating API success as UI evidence', () => {
    expect(scenarioSource).toContain('page.waitForResponse')
    expect(scenarioSource).toContain('response.status() === 200')
    expect(scenarioSource).toContain('await page.goto')
    expect(scenarioSource).toContain('await expect(row).toContainText')
    expect(scenarioSource).toContain('emptyText')
    expect(scenarioSource).toContain('await page.screenshot')
    expect(scenarioSource).toContain('failedRequests')
    expect(scenarioSource).toContain('classifyRequestFailure')
    expect(scenarioSource).toContain('expectedRequestCancellations')
    expect(scenarioSource).toContain('requestFailurePolicy')
    expect(scenarioSource).toContain('connectionstring')
    expect(scenarioSource).toContain('jwt')
    expect(scenarioSource).toContain('NERV_IIP_ISSUE_1912_EVIDENCE_PATH')
    expect(scenarioSource).toContain('NERV_IIP_ISSUE_1912_WORLD_ENABLED')
    expect(scenarioSource).toContain('NERV_IIP_ISSUE_1912_HISTORY_ENABLED')
    expect(scenarioSource).toContain('NERV_IIP_ISSUE_1912_SCALE_ORDER_COUNT')
  })

  it('fails closed when the real stack or evidence destination is not supplied', () => {
    expect(scenarioSource).toContain('NERV_IIP_PLAYWRIGHT_BASE_URL')
    expect(scenarioSource).toContain('NERV_IIP_FULLSTACK_ADMIN_PASSWORD')
    expect(scenarioSource).toContain('NERV_IIP_ISSUE_1912_EVIDENCE_PATH')
    expect(scenarioSource).toContain(
      'requires a managed full-stack session and an evidence destination',
    )
    expect(scenarioSource).toContain('node: options.node')
    expect(scenarioSource).toContain('proof.node))].sort())')
    expect(scenarioSource).toContain("conclusion: 'not-verified'")
  })
})
