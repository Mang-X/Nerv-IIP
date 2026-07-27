import { expect, type Page, type Route } from '@playwright/test'

export const STORAGE_KEY = 'nerv-iip.business-pda.auth'

export const principal = {
  principalId: 'principal-1',
  principalType: 'User',
  loginName: 'operator01',
  email: 'operator01@example.test',
  organizationId: 'org-001',
  environmentId: 'env-dev',
  permissionVersion: 1,
  // 首页按权限裁剪板块/应用墙 —— e2e 主体给全量 PDA 读写权限（等价产线+仓储+质检复合）。
  permissionCodes: [
    'business.mes.dispatch.read',
    'business.mes.work-orders.read',
    'business.mes.operations.read',
    'business.mes.reporting.read',
    'business.mes.materials.read',
    'business.mes.receipts.read',
    'business.wms.receipts.read',
    'business.wms.shipments.read',
    'business.quality.inspection-records.read',
    'business.iiot.alarms.read',
    'business.maintenance.work-orders.read',
    'business.maintenance.plans.read',
    'business.masterdata.resources.read',
  ],
  roleIds: [],
}

/** 首页身份行的员工目录档案（master-data workers 精确查 userId 命中）。 */
export const workerProfile = {
  userId: principal.principalId,
  employeeNo: 'EMP-012',
  displayName: '李秀英',
  jobTitle: '操作工',
  employmentStatus: 'active',
  active: true,
  teams: [{ teamCode: 'TEAM-WB-AS-A', teamName: '装配车间早班组' }],
  skills: [],
}

const deviceAssets = [
  {
    deviceAssetId: 'device-asset-cnc-01',
    code: 'CNC-01',
    displayName: '一号数控机床',
    active: true,
    workshopCode: 'WS-1',
    lineCode: 'LINE-A',
    stationCode: 'ST-9',
  },
  {
    deviceAssetId: 'device-asset-lathe-02',
    code: 'LATHE-02',
    displayName: '二号车床',
    active: true,
    workshopCode: 'WS-2',
    lineCode: 'LINE-B',
  },
]

export const session = {
  accessToken: 'access-token',
  refreshToken: 'refresh-token',
  sessionId: 'session-1',
  expiresAtUtc: '2099-01-01T00:00:00.000Z',
  principal,
}

export function envelope<T>(data: T) {
  return { success: true, message: null, data }
}

async function fulfillJson(route: Route, body: unknown, status = 200) {
  await route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) })
}

/** Mock the console auth endpoints the PDA app depends on (login + refresh + me). */
export async function routeConsoleApi(route: Route) {
  const { pathname } = new URL(route.request().url())
  if (pathname === '/api/console/v1/auth/login' || pathname === '/api/console/v1/auth/refresh') {
    return fulfillJson(route, envelope(session))
  }
  if (pathname === '/api/console/v1/auth/me') {
    return fulfillJson(route, envelope(principal))
  }
  if (pathname === '/api/console/v1/auth/logout') {
    return fulfillJson(route, envelope({}))
  }
  // Don't fake-succeed unmatched paths — fall back so a future un-mocked endpoint
  // surfaces loudly instead of being silently swallowed (aligns with console e2e).
  return route.fallback()
}

/** A `{ items, total }` list payload wrapped in the standard success envelope. */
function listEnvelope<T>(items: T[]) {
  return envelope({ items, total: items.length })
}

const nowUtc = '2026-06-11T00:00:00.000Z'

// Realistic WMS row shapes mirroring the real api-client item types — just enough
// fields for the PDA pages to render business codes + Chinese status (no raw codes/GUIDs).
const inboundOrders = [
  { inboundOrderId: 'in-1', inboundOrderNo: 'IN-1', status: 'pending', createdAtUtc: nowUtc },
  { inboundOrderId: 'in-2', inboundOrderNo: 'IN-2', status: 'pending', createdAtUtc: nowUtc },
]

const outboundOrders = [
  { outboundOrderId: 'out-1', outboundOrderNo: 'OUT-1', status: 'pending', createdAtUtc: nowUtc },
  { outboundOrderId: 'out-2', outboundOrderNo: 'OUT-2', status: 'pending', createdAtUtc: nowUtc },
]

const pickingTasks = [
  {
    warehouseTaskId: 'wt-pk-1',
    taskNo: 'PK-1',
    taskType: 'picking',
    sourceOrderNo: 'OUT-1',
    skuCode: 'SKU-1',
    fromLocationCode: 'A1',
    toLocationCode: 'B2',
    plannedQuantity: 10,
    status: 'pending',
    createdAtUtc: nowUtc,
  },
]

const putawayTasks = [
  {
    warehouseTaskId: 'wt-pa-1',
    taskNo: 'PA-1',
    taskType: 'putaway',
    sourceOrderNo: 'IN-1',
    skuCode: 'SKU-1',
    fromLocationCode: 'A1',
    toLocationCode: 'B2',
    plannedQuantity: 10,
    status: 'pending',
    createdAtUtc: nowUtc,
  },
]

// 收货单完整行投影（收货完成的 fail-closed 门禁依赖它：行数 0 → 判「不完整」禁止提交）。
const receivingQualityGates = [
  {
    inboundOrderId: 'in-1',
    inboundOrderLineId: 'in-1-l1',
    inboundOrderNo: 'IN-1',
    lineNo: '1',
    skuCode: 'SKU-1',
    uomCode: 'EA',
    receivedQuantity: 10,
    stagingLocationCode: 'STG-01',
    lotNo: null,
    qualityStatus: 'passed',
    qualityGateStatus: 'not-required',
  },
]

const countExecutions = [
  {
    countExecutionId: 'ce-1',
    countNo: 'CN-1',
    skuCode: 'SKU-1',
    locationCode: 'A1',
    expectedQuantity: 100,
    status: 'pending',
    createdAtUtc: nowUtc,
  },
]

/**
 * Realistic MES list rows used by the operation/report flows. Shapes mirror the
 * generated `BusinessConsoleMesOperationTaskRow` / `BusinessConsoleMesWorkOrderItem`
 * so the pages render real titles/subtitles instead of empty states.
 */
export const mesOperationTasks = [
  {
    operationTaskId: 'OP-1',
    workOrderId: 'WO-1',
    status: 'Running',
    operationSequence: 10,
    workCenterId: 'WC-A',
  },
  {
    operationTaskId: 'OP-2',
    workOrderId: 'WO-1',
    status: 'Ready',
    operationSequence: 20,
    workCenterId: 'WC-B',
  },
  {
    operationTaskId: 'OP-3',
    workOrderId: 'WO-2',
    status: 'Ready',
    operationSequence: 10,
    workCenterId: 'WC-C',
  },
]

const mesManyOperationTasks = Array.from({ length: 501 }, (_, index) => ({
  operationTaskId: `OP-${index + 1}`,
  workOrderId: 'WO-501',
  status: 'Ready',
  operationSequence: index + 1,
  workCenterId: 'WC-MANY',
}))

/**
 * Dispatch-task rows（首页「我的任务」）— shape mirrors `BusinessConsoleMesDispatchTaskRow`：
 * 一条进行中 + 一条待开工，服务端按 assignedUserId 过滤后返回本人任务。
 */
export const mesDispatchTasks = [
  {
    operationTaskId: 'OT-1',
    workOrderId: 'WO-1',
    workOrderNo: 'WO-2026-00001',
    status: 'InProgress',
    operationCode: 'OP-30',
    workCenterId: 'WC-A',
    workCenterName: '装配一线',
    assignedUserId: principal.principalId,
    assignedUserName: '李秀英',
  },
  {
    operationTaskId: 'OT-2',
    workOrderId: 'WO-2',
    workOrderNo: 'WO-2026-00002',
    status: 'Queued',
    operationCode: 'OP-10',
    workCenterId: 'WC-B',
    assignedUserId: principal.principalId,
    assignedUserName: '李秀英',
  },
]

export const mesWorkOrders = [
  {
    workOrderId: 'WO-1',
    skuId: 'SKU-1',
    quantity: 100,
    status: 'Released',
  },
  {
    workOrderId: 'WO-2',
    skuId: 'SKU-2',
    quantity: 50,
    status: 'Released',
  },
  {
    workOrderId: 'WO-501',
    skuId: 'SKU-501',
    quantity: 1,
    status: 'Released',
  },
]

/**
 * Material-issue request rows — shape mirrors `BusinessConsoleMesMaterialIssueRequestRow`
 * so `/mes/issue` renders real titles/subtitles instead of the empty state.
 */
export const mesMaterialIssueRequests = [
  {
    requestId: 'ISS-1',
    workOrderId: 'WO-1',
    materialId: 'MAT-1',
    requestedQuantity: 100,
    receivedQuantity: 0,
    status: 'Requested',
  },
  {
    requestId: 'ISS-2',
    workOrderId: 'WO-1',
    materialId: 'MAT-2',
    requestedQuantity: 50,
    receivedQuantity: 50,
    status: 'Received',
  },
]

/**
 * Finished-goods receipt request rows — shape mirrors `BusinessConsoleMesReceiptRequestRow`
 * so `/mes/receipt` renders real titles/subtitles instead of the empty state.
 */
export const mesReceiptRequests = [
  {
    receiptRequestId: 'RCPT-1',
    requestNo: 'FGR-2026-0001',
    workOrderId: 'WO-1',
    skuId: 'SKU-1',
    quantity: 100,
    receiptStatus: 'Requested',
  },
  {
    receiptRequestId: 'RCPT-2',
    requestNo: 'FGR-2026-0002',
    workOrderId: 'WO-1',
    skuId: 'SKU-2',
    quantity: 50,
    receiptStatus: 'Received',
  },
]

/**
 * Mock the business-console gateway. MES list/action/create endpoints the
 * operation + report flows hit get realistic envelopes, and the equipment
 * maintenance/alarms endpoints the repair/inspect/alarms pages hit get realistic
 * envelopes (item shapes mirror BusinessConsoleMaintenanceWorkOrderItem,
 * BusinessConsoleMaintenancePlan*, BusinessConsoleMaintenanceInspectionItem and
 * EquipmentRuntimeAlarmSummary so the pages render real Chinese labels). WMS
 * lists return `{ items, total }`; completes return a bare success.
 *
 * Any unmatched path falls back (does NOT fake-succeed) so a future un-mocked /
 * mistyped endpoint surfaces loudly instead of being silently swallowed (aligns
 * with routeConsoleApi). Every endpoint a spec hits must be explicitly mocked here.
 */
export async function routeBusinessConsoleApi(route: Route) {
  const requestUrl = new URL(route.request().url())
  const { pathname } = requestUrl
  const method = route.request().method()
  const isPost = method === 'POST'

  // ---- WMS（收货/复核/盘点 + 拣货/上架） ----
  // complete endpoints (POST .../{id}/complete) — match before the list paths.
  if (isPost && /\/wms\/inbound-orders\/[^/]+\/complete$/.test(pathname)) {
    return fulfillJson(route, envelope({}))
  }
  if (isPost && /\/wms\/outbound-orders\/[^/]+\/complete$/.test(pathname)) {
    return fulfillJson(route, envelope({}))
  }
  if (isPost && /\/wms\/count-executions\/[^/]+\/complete$/.test(pathname)) {
    return fulfillJson(route, envelope({}))
  }

  // list endpoints (GET).
  if (pathname.endsWith('/wms/inbound-orders')) {
    return fulfillJson(route, listEnvelope(inboundOrders))
  }
  if (pathname.endsWith('/wms/outbound-orders')) {
    return fulfillJson(route, listEnvelope(outboundOrders))
  }
  if (pathname.endsWith('/wms/picking-tasks')) {
    return fulfillJson(route, listEnvelope(pickingTasks))
  }
  if (pathname.endsWith('/wms/putaway-tasks')) {
    return fulfillJson(route, listEnvelope(putawayTasks))
  }
  if (pathname.endsWith('/wms/count-executions')) {
    return fulfillJson(route, listEnvelope(countExecutions))
  }
  if (pathname.endsWith('/wms/receiving-quality-gates')) {
    return fulfillJson(route, listEnvelope(receivingQualityGates))
  }

  // ---- 设备运维（报修/点检/报警查看） ----
  // 报修设备选择器：principal scope + 服务端 keyword/skip/take，有界返回。
  if (pathname === '/api/business-console/v1/master-data/device-assets') {
    const keyword = (requestUrl.searchParams.get('keyword') ?? '').trim().toLowerCase()
    const skip = Math.max(0, Number(requestUrl.searchParams.get('skip') ?? 0))
    const take = Math.max(1, Number(requestUrl.searchParams.get('take') ?? 20))
    const matched = keyword
      ? deviceAssets.filter(
          (item) =>
            item.displayName.toLowerCase().includes(keyword) ||
            item.code.toLowerCase().includes(keyword),
        )
      : deviceAssets
    return fulfillJson(
      route,
      envelope({
        resources: matched.slice(skip, skip + take),
        total: matched.length,
        truncated: skip + take < matched.length,
        limit: take,
      }),
    )
  }

  // 报修：维修工单 list / create
  if (pathname === '/api/business-console/v1/maintenance/work-orders') {
    if (method === 'POST') {
      return fulfillJson(route, envelope({ workOrderId: 'WO-M-new' }))
    }
    return fulfillJson(
      route,
      envelope({
        items: [
          {
            workOrderId: 'WO-M1',
            deviceAssetId: 'DEV-A',
            priority: 'high',
            status: 'open',
            openedBy: principal.loginName,
            openedAtUtc: '2026-06-10T01:00:00.000Z',
          },
        ],
        total: 1,
      }),
    )
  }

  // 点检：保养计划 list（点检页先选计划）
  if (pathname === '/api/business-console/v1/maintenance/plans') {
    return fulfillJson(
      route,
      envelope({
        items: [
          {
            planId: 'PLAN-1',
            planCode: 'PM-001',
            deviceAssetId: 'DEV-A',
            interval: 'P7D',
          },
        ],
        total: 1,
      }),
    )
  }

  // 点检：记录 list（空）/ record
  if (pathname === '/api/business-console/v1/maintenance/inspections') {
    if (method === 'POST') {
      return fulfillJson(route, envelope({ inspectionId: 'INS-new' }))
    }
    return fulfillJson(route, envelope({ items: [], total: 0 }))
  }

  // 报警查看（只读）
  if (pathname === '/api/business-console/v1/equipment/alarms') {
    return fulfillJson(
      route,
      envelope({
        items: [
          {
            alarmEventId: 'ALM-1',
            deviceAssetId: 'DEV-A',
            alarmCode: 'E-101',
            severity: 'critical',
            raisedAtUtc: '2026-06-10T02:30:00.000Z',
          },
        ],
        total: 1,
      }),
    )
  }

  // ---- 首页（员工档案 / 我的任务 / 待检任务） ----
  if (pathname === '/api/business-console/v1/master-data/workers') {
    return fulfillJson(
      route,
      envelope({ pageIndex: 1, pageSize: 1, totalCount: 1, items: [workerProfile] }),
    )
  }
  if (pathname === '/api/business-console/v1/quality/inspection-tasks') {
    return fulfillJson(route, envelope({ items: [], total: 0 }))
  }

  // ---- MES（工序执行/报工/领料/完工入库） ----
  const base = '/api/business-console/v1/mes'

  if (pathname === `${base}/dispatch-tasks`) {
    return fulfillJson(route, envelope({ items: mesDispatchTasks, total: mesDispatchTasks.length }))
  }

  // Operation-task actions: start/pause/resume/complete → success envelope.
  if (
    method === 'POST' &&
    /\/mes\/operation-tasks\/[^/]+\/(start|pause|resume|complete)$/.test(pathname)
  ) {
    return fulfillJson(route, envelope({}))
  }
  if (pathname === `${base}/operation-tasks`) {
    const workOrderId = requestUrl.searchParams.get('workOrderId')
    const scopedItems =
      workOrderId === 'WO-501'
        ? mesManyOperationTasks
        : workOrderId
          ? mesOperationTasks.filter((task) => task.workOrderId === workOrderId)
          : mesOperationTasks
    const skip = Number(requestUrl.searchParams.get('skip') ?? 0)
    const take = Number(requestUrl.searchParams.get('take') ?? 100)
    const items = scopedItems.slice(skip, skip + take)
    return fulfillJson(route, envelope({ items, total: scopedItems.length }))
  }
  if (pathname === `${base}/work-orders`) {
    return fulfillJson(route, envelope({ items: mesWorkOrders, total: mesWorkOrders.length }))
  }
  const workOrderDetailMatch = pathname.match(
    /^\/api\/business-console\/v1\/mes\/work-orders\/([^/]+)$/,
  )
  if (method === 'GET' && workOrderDetailMatch) {
    const workOrderId = decodeURIComponent(workOrderDetailMatch[1])
    const workOrder = mesWorkOrders.find((candidate) => candidate.workOrderId === workOrderId)
    if (!workOrder) {
      return fulfillJson(route, { success: false, message: '工单不存在', data: null })
    }
    return fulfillJson(
      route,
      envelope({
        ...workOrder,
        readinessStatus: 'ready',
        blockingReasons: [],
        operationTasks: (workOrderId === 'WO-501'
          ? mesManyOperationTasks
          : mesOperationTasks.filter((task) => task.workOrderId === workOrderId)
        ).slice(0, 500),
      }),
    )
  }
  if (pathname === `${base}/production-reports`) {
    if (method === 'POST') {
      return fulfillJson(
        route,
        envelope({
          productionReportId: '019f-e2e-production-report',
          reportNo: 'RPT-E2E-0001',
        }),
      )
    }
    return fulfillJson(route, envelope({ items: [], total: 0 }))
  }
  if (pathname === `${base}/telemetry-production-report-candidates`) {
    return fulfillJson(route, envelope({ items: [], total: 0 }))
  }
  if (pathname === `${base}/material-issue-requests`) {
    return fulfillJson(
      route,
      envelope({ items: mesMaterialIssueRequests, total: mesMaterialIssueRequests.length }),
    )
  }
  if (pathname === `${base}/finished-goods-receipt-requests`) {
    if (method === 'POST') return fulfillJson(route, envelope({}))
    return fulfillJson(
      route,
      envelope({ items: mesReceiptRequests, total: mesReceiptRequests.length }),
    )
  }

  // Don't fake-succeed unmatched paths — fall back so a future un-mocked / mistyped
  // endpoint surfaces loudly instead of being silently swallowed (aligns with routeConsoleApi).
  return route.fallback()
}

/** Seed a stored session so guarded routes load without going through the login form. */
export async function seedStoredSession(page: Page, colorMode?: 'light' | 'dark') {
  await page.addInitScript(
    ({ key, stored, mode }) => {
      localStorage.setItem(key, JSON.stringify(stored))
      if (mode) localStorage.setItem('nerv-iip-color-mode', mode)
    },
    {
      key: STORAGE_KEY,
      stored: { principal, refreshToken: session.refreshToken, sessionId: session.sessionId },
      mode: colorMode,
    },
  )
}

export async function expectNoHorizontalOverflow(page: Page) {
  const overflow = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
  )
  expect(overflow).toBeLessThanOrEqual(1)
}

/**
 * Every enabled interactive control must meet the 44px touch-target floor.
 *
 * Query set covers real-world interactive shapes — including `<div role="button">`
 * (e.g. ListRow) and `role="link"`, which are the forms most likely to render too
 * small. Disabled controls (native `:disabled` or `aria-disabled="true"`) are excluded.
 *
 * Pass rule — deliberately strict so a large layout container can't mask a genuinely
 * small control:
 *  - The control's OWN box meets the floor (>=44 in both dimensions) → pass.
 *  - ONLY for a bare `<input>` declared full-width (CSS `width:100%`, i.e. a flex
 *    full-row input) whose parent row is >=44px tall does the row supply the tap
 *    surface (e.g. ScanBar, where the input shares a 48px `min-h-touch` row with a
 *    decorative icon). The width-intent check is read from the declared style, not the
 *    rendered box, so an icon/padding sibling can't fail it — yet a small `role=button`
 *    tucked inside a tall wrapper is NOT excused.
 *  - Everything else (including role=button / div) is judged by its own box.
 */
export async function expectTouchTargets(page: Page) {
  const tooSmall = await page.evaluate(() => {
    const FLOOR = 44
    const els = [
      ...document.querySelectorAll<HTMLElement>(
        'button:not([disabled]), a[href], input, [role="button"]:not([aria-disabled="true"]), [role="link"]',
      ),
    ]
    return els
      .map((el) => {
        const own = el.getBoundingClientRect()
        let effW = own.width
        let effH = own.height
        // Legal full-row input: a full-width (`width:100%`) <input> whose >=44px-tall
        // parent row carries the tap surface. Restricted to <input> + declared
        // full-width intent so it cannot excuse a small control inside a tall wrapper.
        if (el.tagName === 'INPUT' && el.parentElement) {
          const row = el.parentElement.getBoundingClientRect()
          const declaredFullWidth =
            getComputedStyle(el).width === '100%' || el.classList.contains('w-full')
          if (row.height >= FLOOR && own.width > 0 && declaredFullWidth) {
            effW = Math.max(own.width, row.width)
            effH = row.height
          }
        }
        return { tag: el.tagName, role: el.getAttribute('role'), w: effW, h: effH }
      })
      .filter((m) => m.w > 0 && m.h > 0 && (m.w < FLOOR || m.h < FLOOR))
  })
  expect(tooSmall).toEqual([])
}
