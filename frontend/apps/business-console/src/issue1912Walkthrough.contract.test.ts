import { readFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'
import {
  callWithSessionCredential,
  createSessionCredentialTracker,
  withSessionCredentialCleanup,
} from '../e2e/session-credential-tracker'
import {
  buildAuthorizedWorkPoolAssignment,
  extractPublicError,
  runWithActorContext,
  runWithAuthorizedScope,
  selectAuthorizedWorkPoolScope,
  selectAuthorizedWorkSiteScope,
  selectAuthorizedWorkScope,
} from '../e2e/issue1912-walkthrough-runtime'

const scenarioSource = readFileSync(
  resolve(
    dirname(fileURLToPath(import.meta.url)),
    '../e2e/issue1912-real-machine-walkthrough.spec.ts',
  ),
  'utf8',
)

const consoleOpenApi = JSON.parse(
  readFileSync(
    resolve(
      dirname(fileURLToPath(import.meta.url)),
      '../../../packages/api-client/openapi/business-gateway-console.v1.json',
    ),
    'utf8',
  ),
) as {
  components?: {
    schemas?: Record<string, { properties?: Record<string, unknown> }>
  }
}

const workScopeCatalogItemSchema =
  consoleOpenApi.components?.schemas
    ?.NervIIPBusinessGatewayWebApplicationBusinessServicesBusinessConsoleWmsWorkScopeCatalogItem

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
    expect(scenarioSource).toContain('await targetPage.screenshot')
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
    expect(scenarioSource).toContain('node,')
    expect(scenarioSource).toContain('proof.node))].sort())')
    expect(scenarioSource).toContain("conclusion: 'not-verified'")
  })

  it('uses two isolated identities and contexts for ERP approval versus WMS execution', () => {
    expect(scenarioSource).toContain('NERV_IIP_LEADER_DEMO_WORKER_PASSWORD')
    expect(scenarioSource).toContain(
      'const workerContext: BrowserContext = await browser.newContext',
    )
    expect(scenarioSource).toContain('user-admin')
    expect(scenarioSource).toContain('user-emp-049')
    expect(scenarioSource).toContain("workerLoginName.fill('emp049')")
    expect(scenarioSource).toContain('workerSessionCredentialTracker')
    expect(scenarioSource).toContain('credentialDigest')
    expect(scenarioSource).toContain('identityIsolation')
  })

  it('follows the public WarehouseWorkScopeCatalogItem contract and records fail-closed scope behavior', () => {
    expect(workScopeCatalogItemSchema).toBeDefined()
    expect(Object.keys(workScopeCatalogItemSchema?.properties ?? {}).sort()).toEqual([
      'displayName',
      'poolCode',
      'scopeId',
      'scopeKind',
      'siteCode',
    ])
    expect(scenarioSource).not.toContain('movementAllowed')
    expect(scenarioSource).not.toContain('isBlocked')
    expect(scenarioSource).not.toContain('isExpired')
    expect(scenarioSource).toContain('wms-no-scope-fail-closed')
    expect(scenarioSource).toContain('sideEffect: false')
    expect(scenarioSource).toContain('expectedVersion')
    expect(scenarioSource).toContain('scope catalog')
  })

  it('runs an actor-aware scope fixture against public payloads and blocks missing-scope mutations', async () => {
    const calls: Array<{
      actor: string
      principalId: string
      authorization: string
      scopeId: string
    }> = []
    const workerContext = {
      actor: 'wms-worker' as const,
      principalId: 'user-emp-049',
      authorization: 'Bearer worker-fixture-token',
    }
    const adminContext = {
      actor: 'erp-admin' as const,
      principalId: 'user-admin',
      authorization: 'Bearer admin-fixture-token',
    }
    const catalogPayload = {
      data: {
        actorPrincipalId: workerContext.principalId,
        items: [
          {
            displayName: '收货作业池',
            poolCode: 'POOL-WMS-RECEIVING',
            scopeId: 'pool-receiving-001',
            scopeKind: 'work-pool',
            siteCode: 'SITE-001',
          },
        ],
      },
    }
    const scope = selectAuthorizedWorkScope(catalogPayload)
    expect(scope).toEqual({
      displayName: '收货作业池',
      poolCode: 'POOL-WMS-RECEIVING',
      scopeId: 'pool-receiving-001',
      scopeKind: 'work-pool',
      siteCode: 'SITE-001',
    })
    expect(
      selectAuthorizedWorkScope({
        data: {
          items: [{ scopeKind: 'work-pool', scopeId: '', displayName: '不完整作业池' }],
        },
      }),
    ).toBeUndefined()

    const scopedCall = await runWithActorContext(workerContext, async (context) => {
      calls.push({
        actor: context.actor,
        principalId: context.principalId,
        authorization: context.authorization,
        scopeId: scope!.scopeId,
      })
      return { status: 200 }
    })
    expect(scopedCall).toEqual({ status: 200 })
    expect(calls).toEqual([
      {
        actor: 'wms-worker',
        principalId: 'user-emp-049',
        authorization: 'Bearer worker-fixture-token',
        scopeId: 'pool-receiving-001',
      },
    ])
    expect(calls).not.toContainEqual(
      expect.objectContaining({ authorization: adminContext.authorization }),
    )

    let mutationCalls = 0
    const missingScope = await runWithAuthorizedScope(undefined, async () => {
      mutationCalls += 1
      return { status: 200 }
    })
    expect(missingScope).toEqual({ called: false, reason: 'missing-authorized-scope' })
    expect(mutationCalls).toBe(0)

    expect(
      extractPublicError({
        success: false,
        message: 'work-scope-not-authorized',
        statusCode: 403,
        errors: [],
      }),
    ).toEqual({ code: 'work-scope-not-authorized', message: 'work-scope-not-authorized' })
    expect(
      extractPublicError({
        success: false,
        message: 'missing-work-pool-assignment',
        statusCode: 403,
        errors: [],
      }),
    ).toEqual({ code: 'missing-work-pool-assignment', message: 'missing-work-pool-assignment' })
    expect(
      extractPublicError({ success: false, message: 'missing-work-pool-assignment' }),
    ).not.toEqual(extractPublicError({ success: false, message: 'work-scope-not-authorized' }))
  })

  it('builds WMS assignments from the worker context and an authorized work-pool scope', async () => {
    const workerContext = {
      actor: 'wms-worker' as const,
      principalId: 'user-emp-049',
      authorization: 'Bearer worker-fixture-token',
    }
    const adminContext = {
      actor: 'erp-admin' as const,
      principalId: 'user-admin',
      authorization: 'Bearer admin-fixture-token',
    }
    const catalogPayload = {
      data: {
        actorPrincipalId: workerContext.principalId,
        items: [
          {
            displayName: '我的任务',
            poolCode: null,
            scopeId: workerContext.principalId,
            scopeKind: 'self',
            siteCode: null,
          },
          {
            displayName: '收货作业池',
            poolCode: 'POOL-WMS-RECEIVING',
            scopeId: 'pool-receiving-001',
            scopeKind: 'work-pool',
            siteCode: 'SITE-001',
          },
          {
            displayName: 'SITE-001',
            poolCode: null,
            scopeId: 'SITE-001',
            scopeKind: 'site',
            siteCode: 'SITE-001',
          },
        ],
      },
    }
    const readScope = selectAuthorizedWorkSiteScope(catalogPayload, 'SITE-001')
    expect(readScope).toEqual({
      displayName: 'SITE-001',
      poolCode: null,
      scopeId: 'SITE-001',
      scopeKind: 'site',
      siteCode: 'SITE-001',
    })
    const scope = selectAuthorizedWorkPoolScope(catalogPayload, 'SITE-001')
    expect(scope).toEqual({
      displayName: '收货作业池',
      poolCode: 'POOL-WMS-RECEIVING',
      scopeId: 'pool-receiving-001',
      scopeKind: 'work-pool',
      siteCode: 'SITE-001',
    })

    const assignmentCalls: Array<{
      actor: string
      principalId: string
      authorization: string
      resourceId: string
      poolCode: string
      operatorPrincipalId: string
      scopeKind: string
      scopeId: string
    }> = []
    const assignment = await runWithActorContext(workerContext, async (context) => {
      const plan = buildAuthorizedWorkPoolAssignment(
        context,
        scope,
        'inbound-fixture-001',
        'issue1912-inbound-assignment',
        3,
      )
      if (plan.called) {
        assignmentCalls.push({
          actor: context.actor,
          principalId: context.principalId,
          authorization: context.authorization,
          resourceId: plan.request.resourceId,
          poolCode: plan.request.body.poolCode,
          operatorPrincipalId: plan.request.body.operatorPrincipalId,
          scopeKind: plan.request.scope.scopeKind,
          scopeId: plan.request.scope.scopeId,
        })
      }
      return plan
    })
    expect(assignment).toEqual({
      called: true,
      request: {
        resourceId: 'inbound-fixture-001',
        scope: {
          displayName: '收货作业池',
          poolCode: 'POOL-WMS-RECEIVING',
          scopeId: 'pool-receiving-001',
          scopeKind: 'work-pool',
          siteCode: 'SITE-001',
        },
        body: {
          poolCode: 'POOL-WMS-RECEIVING',
          operatorPrincipalId: 'user-emp-049',
          idempotencyKey: 'issue1912-inbound-assignment',
          expectedVersion: 3,
        },
      },
    })
    expect(assignmentCalls).toEqual([
      {
        actor: 'wms-worker',
        principalId: 'user-emp-049',
        authorization: workerContext.authorization,
        resourceId: 'inbound-fixture-001',
        poolCode: 'POOL-WMS-RECEIVING',
        operatorPrincipalId: 'user-emp-049',
        scopeKind: 'work-pool',
        scopeId: 'pool-receiving-001',
      },
    ])
    expect(assignmentCalls).not.toContainEqual(
      expect.objectContaining({
        actor: adminContext.actor,
        principalId: adminContext.principalId,
        authorization: adminContext.authorization,
      }),
    )

    expect(
      selectAuthorizedWorkPoolScope({
        data: {
          items: [
            {
              displayName: '不完整作业池',
              poolCode: null,
              scopeId: 'pool-missing-code',
              scopeKind: 'work-pool',
              siteCode: 'SITE-001',
            },
          ],
        },
      }),
    ).toBeUndefined()
    expect(
      buildAuthorizedWorkPoolAssignment(
        workerContext,
        undefined,
        'inbound-fixture-001',
        'issue1912-missing-assignment',
        3,
      ),
    ).toEqual({ called: false, reason: 'missing-authorized-scope' })
    expect(
      buildAuthorizedWorkPoolAssignment(
        adminContext,
        scope,
        'inbound-fixture-001',
        'issue1912-admin-assignment',
        3,
      ),
    ).toEqual({ called: false, reason: 'wms-worker-context-required' })
  })
})
