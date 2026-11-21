import { test as base, type Page, expect, type Route } from '@playwright/test'

const STORAGE_KEY = 'nerv-iip.business-console.auth'

export const principal = {
  principalId: 'principal-1',
  principalType: 'User',
  loginName: 'admin',
  email: 'admin@example.test',
  organizationId: 'org-001',
  environmentId: 'env-dev',
  permissionVersion: 1,
  // 只授予被 e2e 覆盖、且页面 meta 声明了 requiredPermissions 的能力码；
  // 未声明的页面本来就放行，这里只做加法，不会收紧任何已有用例。
  // 路由守卫（src/router/guards/auth.ts）逐页比对 meta.requiredPermissions，
  // 缺一个码整页跳 /forbidden，故本 spec 访问到的页面须逐条列全。
  permissionCodes: [
    'business.erp.sales.read', // /erp/sales(/*)
    'business.erp.finance.read', // /erp/finance(/*)
    'business.masterdata.products.read', // /master-data/skus
    'business.masterdata.resources.read', // /master-data/partners
    'business.inventory.ledger.read', // /inventory/availability
    'business.quality.ncr.read', // /quality/ncrs
    'business.mes.overview.read', // /mes
    'business.mes.foundation.read', // /mes/foundation
    'business.mes.plans.read', // /mes/plans
    'business.mes.work-orders.read', // /mes/work-orders(/:id)
    'business.mes.materials.read', // /mes/materials
    'business.mes.dispatch.read', // /mes/dispatch
    'business.mes.dispatch.manage', // 派工看板改派
    'business.mes.operations.read', // /mes/operation-tasks, /mes/wip
    'business.mes.reporting.read', // /mes/production-reports
    'business.mes.quality.read', // /mes/quality
    'business.mes.receipts.read', // /mes/receipts
    // 完工入库用例断言的是 manage 用户才渲染的登记入口（receipts.vue 里 canManageReceipts
    // 控制「从工单详情发起」按钮与带 query 自动开弹窗），故额外给操作级 manage 码。
    'business.mes.receipts.manage',
    'business.mes.downtime.read', // /mes/downtime
    'business.mes.handovers.read', // /mes/handovers
    'business.mes.traceability.read', // /mes/traceability
    'business.mes.capacity.read', // /mes/capacity
  ],
}

const session = {
  accessToken: 'access-token',
  refreshToken: 'refresh-token',
  sessionId: 'session-1',
  expiresAtUtc: '2099-01-01T00:00:00.000Z',
  principal,
}

export const test = base.extend<{ permissionCodes: string[] }>({
  permissionCodes: [principal.permissionCodes, { option: true }],
})

export async function expectHeading(page: Page, path: string, heading: string) {
  await page.goto(path, { waitUntil: 'domcontentloaded' })
  // 慢的 dev 环境 + 连续多页导航，放宽到 15s。
  await expect(
    page.locator('[data-slot="breadcrumb-page"]').filter({ hasText: heading }),
  ).toBeVisible({ timeout: 15_000 })
}

export async function expandFoundationIssues(page: Page) {
  const issueDetails = page.getByText('查看 1 个对象明细', { exact: true })
  await expect(issueDetails).toHaveCount(3)
  for (const summary of await issueDetails.all()) await summary.click()
}

export async function seedStoredSession(page: Page, currentPrincipal: typeof principal) {
  await page.addInitScript(
    ({ key, storedSession }) => {
      localStorage.setItem(key, JSON.stringify(storedSession))
    },
    {
      key: STORAGE_KEY,
      storedSession: {
        principal: currentPrincipal,
        refreshToken: session.refreshToken,
        sessionId: session.sessionId,
      },
    },
  )
}

export async function routeConsoleApi(route: Route, currentPrincipal: typeof principal) {
  const url = new URL(route.request().url())

  if (url.pathname === '/api/console/v1/auth/refresh') {
    return fulfillJson(route, envelope({ ...session, principal: currentPrincipal }))
  }

  if (url.pathname === '/api/console/v1/auth/me') {
    return fulfillJson(route, envelope(currentPrincipal))
  }

  return route.fallback()
}

export async function routeBusinessConsoleApi(route: Route) {
  const url = new URL(route.request().url())
  const { pathname } = url

  if (pathname === '/api/business-console/v1/me/work-context') {
    const scope = { kind: 'organization', id: 'org-001', displayName: '一号工厂' }
    return fulfillJson(
      route,
      envelope({
        organizationId: 'org-001',
        environmentId: 'env-dev',
        applicablePermissionCode: url.searchParams.get('permissionCode'),
        resolvedAtUtc: '2026-08-26T01:00:00.000Z',
        principal: { id: principal.principalId, principalType: principal.principalType },
        resolutionStatus: 'resolved',
        authorizedScopes: [scope],
        availableScopeKinds: ['organization'],
        selectedScope: scope,
        issues: [],
      }),
    )
  }

  if (pathname === '/api/business-console/v1/master-data/skus') {
    return fulfillJson(
      route,
      envelope({
        resources: [
          {
            resourceType: 'sku',
            code: 'SKU-001',
            displayName: '前减振器总成',
            active: true,
            snapshotVersion: 'v1',
          },
        ],
        total: 1,
      }),
    )
  }

  if (pathname === '/api/business-console/v1/master-data/resources') {
    const resourceType = url.searchParams.get('resourceType') ?? 'site'
    return fulfillJson(
      route,
      envelope({
        resources: [
          {
            resourceType,
            code: `${resourceType.toUpperCase()}-001`,
            displayName: `${resourceType} 主数据`,
            active: true,
            snapshotVersion: 'v1',
          },
        ],
        total: 1,
      }),
    )
  }

  if (pathname === '/api/business-console/v1/inventory/availability') {
    return fulfillJson(
      route,
      envelope({
        skuCode: 'SKU-001',
        uomCode: 'EA',
        onHandQuantity: 12,
        reservedQuantity: 2,
        availableQuantity: 10,
        lines: [
          {
            locationCode: 'A-01',
            qualityStatus: 'available',
            ownerType: 'owned',
            onHandQuantity: 12,
            availableQuantity: 10,
          },
        ],
      }),
    )
  }

  if (pathname === '/api/business-console/v1/quality/ncrs') {
    return fulfillJson(
      route,
      envelope({
        items: [
          {
            id: 'ncr-1',
            code: 'NCR-001',
            status: 'open',
            summary: 'Dimension out of tolerance',
          },
        ],
      }),
    )
  }

  if (pathname === '/api/business-console/v1/mes/work-orders') {
    return fulfillJson(
      route,
      envelope({
        items: [
          {
            workOrderId: 'WO-001',
            skuId: 'SKU-001',
            quantity: 10,
            priority: 1,
            dueUtc: '2026-05-25T12:00:00.000Z',
            status: 'released',
            operationTasks: [
              {
                operationTaskId: 'op-1',
                status: 'ready',
                operationSequence: 10,
                workCenterId: 'WC-001',
              },
            ],
          },
        ],
      }),
    )
  }

  if (pathname === '/api/business-console/v1/mes/overview') {
    return fulfillJson(
      route,
      envelope({
        counts: [
          { key: 'WorkOrders', count: 1, status: 'Released' },
          { key: 'OperationTasks', count: 1, status: 'Ready' },
        ],
        blockers: [],
        pendingWork: [],
      }),
    )
  }

  if (pathname === '/api/business-console/v1/mes/foundation-readiness') {
    const issues = [
      {
        code: 'WORK_CENTER_COST_RATE_MISSING',
        message: '缺少总装一线的人工费率',
        referenceDisplayName: '总装一线',
      },
      {
        code: 'WORK_CENTER_MACHINE_OVERHEAD_RATE_MISSING',
        message: '缺少总装设备的制造费用率',
        referenceDisplayName: '总装设备',
      },
      {
        code: 'ACCOUNTING_PERIOD_MISSING',
        message: '缺少 11 月会计期间',
        referenceDisplayName: '11 月',
        referenceType: 'AccountingPeriod',
        referenceId: '2026-11-20',
      },
    ]
    return fulfillJson(
      route,
      envelope({
        status: 'Blocked',
        areas: [{ areaCode: 'master-data', status: 'Blocked', issues }],
        blockingIssues: issues,
        warningIssues: [],
      }),
    )
  }

  if (pathname === '/api/business-console/v1/mes/production-plans') {
    return fulfillJson(
      route,
      envelope({
        items: [
          {
            productionPlanId: 'PLAN-READY',
            sourceSystem: 'sales',
            skuId: 'sku-1',
            plannedQuantity: 10,
            uomCode: 'EA',
            readinessStatus: 'Ready',
            plannedStartUtc: '2026-06-01T08:00:00.000Z',
            blockingReasons: [],
          },
          {
            productionPlanId: 'PLAN-BLOCKED',
            sourceSystem: 'forecast',
            skuId: 'sku-2',
            plannedQuantity: 5,
            readinessStatus: 'Blocked',
            blockingReasons: ['material_shortage'],
          },
        ],
        total: 2,
      }),
    )
  }

  if (pathname === '/api/business-console/v1/mes/work-orders/WO-001') {
    return fulfillJson(
      route,
      envelope({
        workOrderId: 'WO-001',
        skuId: 'SKU-001',
        quantity: 10,
        status: 'released',
        readinessStatus: 'Ready',
        blockingReasons: [],
        operationTasks: [
          {
            operationTaskId: 'op-1',
            workOrderId: 'WO-001',
            status: 'Ready',
            operationSequence: 10,
            workCenterId: 'WC-001',
            qualityStatus: 'Ready',
          },
        ],
      }),
    )
  }

  if (pathname === '/api/business-console/v1/mes/work-orders/WO-001/material-readiness') {
    return fulfillJson(
      route,
      envelope({
        workOrderId: 'WO-001',
        readinessStatus: 'Ready',
        blockingReasons: [],
        items: [],
      }),
    )
  }

  if (pathname === '/api/business-console/v1/mes/operation-tasks') {
    return fulfillJson(
      route,
      envelope({
        items: [
          {
            operationTaskId: 'WO-2026-08001-OP-10',
            operationTaskNo: 'WO-2026-08001-OP-10',
            workOrderId: 'WO-2026-08001',
            workOrderNo: 'WO-2026-08001',
            status: 'Completed',
            operationSequence: 10,
            workCenterId: 'WC-001',
            qualityStatus: 'Ready',
            actualLaborHours: 1.25,
            actualMachineHours: 0.5,
          },
          {
            operationTaskId: 'op-1',
            workOrderId: 'WO-001',
            status: 'Ready',
            operationSequence: 10,
            workCenterId: 'WC-001',
            qualityStatus: 'Ready',
          },
        ],
        total: 2,
      }),
    )
  }

  if (pathname === '/api/business-console/v1/mes/material-issue-requests') {
    return fulfillJson(
      route,
      envelope({
        items: [
          {
            requestId: 'MIR-001',
            workOrderId: 'WO-001',
            materialId: 'mat-1',
            requestedQuantity: 10,
            receivedQuantity: 4,
            status: 'PartiallyReceived',
            wmsRequestId: 'WMS-OUT-001',
            requestedAtUtc: '2026-06-01T08:00:00.000Z',
          },
        ],
        total: 1,
      }),
    )
  }

  if (pathname === '/api/business-console/v1/mes/line-side-inventory-balances') {
    const requestedPage = Number(url.searchParams.get('page') ?? 1)
    const items =
      requestedPage === 2
        ? [
            {
              siteCode: 'SITE-SH',
              locationCode: 'LINE-A04',
              skuCode: 'SKU-PAGE-201',
              uomCode: 'pcs',
              onHandQuantity: 8,
              reservedQuantity: 1,
              availableQuantity: 7,
              lotCount: 1,
              oldestProductionDate: '2026-08-25',
              ageDays: 1,
              ageCompleteness: 'complete',
            },
          ]
        : [
            {
              siteCode: 'SITE-SH',
              locationCode: 'LINE-A01',
              skuCode: 'SKU-DAMPER-001',
              uomCode: 'pcs',
              onHandQuantity: 120,
              reservedQuantity: 20,
              availableQuantity: 100,
              lotCount: 3,
              oldestProductionDate: '2026-08-20',
              ageDays: 6,
              ageCompleteness: 'complete',
            },
            {
              siteCode: 'SITE-SH',
              locationCode: 'LINE-A02',
              skuCode: 'SKU-SEAL-008',
              uomCode: 'pcs',
              onHandQuantity: 45,
              reservedQuantity: 5,
              availableQuantity: 40,
              lotCount: 2,
              oldestProductionDate: '2026-08-22',
              ageDays: 4,
              ageCompleteness: 'partial',
            },
            {
              siteCode: 'SITE-SH',
              locationCode: 'LINE-A03',
              skuCode: 'SKU-OIL-012',
              uomCode: 'l',
              onHandQuantity: 18,
              reservedQuantity: 0,
              availableQuantity: 18,
              lotCount: 1,
              oldestProductionDate: null,
              ageDays: null,
              ageCompleteness: 'unavailable',
            },
            ...Array.from({ length: 197 }, (_, index) => ({
              siteCode: 'SITE-SH',
              locationCode: `LINE-${String(index + 4).padStart(3, '0')}`,
              skuCode: `SKU-PAGE-${String(index + 4).padStart(3, '0')}`,
              uomCode: 'pcs',
              onHandQuantity: index + 1_000,
              reservedQuantity: 0,
              availableQuantity: index + 1_000,
              lotCount: 1,
              oldestProductionDate: '2026-08-25',
              ageDays: 1,
              ageCompleteness: 'complete',
            })),
          ]
    return fulfillJson(
      route,
      envelope({
        items,
        totalCount: 201,
        page: requestedPage,
        pageSize: 200,
        asOfDate: '2026-08-26',
      }),
    )
  }

  if (pathname === '/api/business-console/v1/mes/dispatch-tasks') {
    return fulfillJson(
      route,
      envelope({
        items: [{ operationTaskId: 'OP-001', workOrderId: 'WO-001', status: 'Ready' }],
        total: 1,
      }),
    )
  }

  if (pathname === '/api/business-console/v1/mes/related-quality-items') {
    return fulfillJson(route, envelope({ items: [] }))
  }

  if (pathname === '/api/business-console/v1/mes/downtime-events') {
    return fulfillJson(route, envelope({ items: [] }))
  }

  if (pathname === '/api/business-console/v1/mes/shift-handovers') {
    return fulfillJson(route, envelope({ items: [] }))
  }

  if (pathname.startsWith('/api/business-console/v1/mes/traceability/')) {
    return fulfillJson(route, envelope({ nodes: [], edges: [] }))
  }

  if (pathname === '/api/business-console/v1/mes/wip') {
    return fulfillJson(
      route,
      envelope({
        items: [
          {
            workOrderId: 'WO-001',
            operationTaskId: 'op-1',
            workCenterId: 'WC-001',
            status: 'Ready',
            plannedQuantity: 10,
            goodQuantity: 5,
            scrapQuantity: 0,
            blockingReasons: [],
          },
        ],
      }),
    )
  }

  if (pathname === '/api/business-console/v1/mes/production-reports') {
    return fulfillJson(
      route,
      envelope({
        items: [
          {
            productionReportId: 'report-1',
            reportNo: 'PRPT-20260826-001',
            workOrderId: 'WO-001',
            workOrderNo: 'WO-001',
            operationTaskId: 'op-1',
            operationTaskNo: 'WO-001-OP-10',
            goodQuantity: 5,
            scrapQuantity: 0,
            reportedAtUtc: '2026-05-25T13:00:00.000Z',
            operationActualLaborHours: 2.75,
            operationActualMachineHours: 1.5,
          },
        ],
        total: 1,
      }),
    )
  }

  if (pathname === '/api/business-console/v1/mes/finished-goods-receipt-requests') {
    return fulfillJson(
      route,
      envelope({
        items: [
          {
            receiptRequestId: 'receipt-1',
            workOrderId: 'WO-001',
            skuId: 'SKU-001',
            quantity: 5,
            receiptStatus: 'Pending',
            requestedAtUtc: '2026-05-25T14:00:00.000Z',
          },
        ],
      }),
    )
  }

  if (pathname === '/api/business-console/v1/mes/capacity-impacts') {
    return fulfillJson(
      route,
      envelope({
        items: [
          {
            impactId: 'impact-1',
            workCenterId: 'WC-001',
            deviceAssetId: 'DEV-001',
            status: 'Active',
            effectiveFromUtc: '2026-05-25T15:00:00.000Z',
            reasonCode: 'MAINTENANCE',
          },
        ],
      }),
    )
  }

  return fulfillJson(route, envelope({}))
}

export function envelope<T>(data: T) {
  return {
    success: true,
    data,
  }
}

export async function fulfillJson(route: Route, body: unknown) {
  await route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify(body),
  })
}
