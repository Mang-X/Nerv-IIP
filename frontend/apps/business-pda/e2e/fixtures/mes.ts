import { confirmedOperationReceipt, fulfillJson, envelope, nowUtc } from './http'
import { principal, workerProfile } from './auth'
import { type Route } from '@playwright/test'

/** 工序动作成功后的 canonical 落点状态（与 statusActionGate 的状态机一致）。 */
const operationActionResultStatus: Record<string, string> = {
  start: 'InProgress',
  pause: 'Paused',
  resume: 'InProgress',
  complete: 'Completed',
}

/** 报工写操作的权威回执（fixtures 与 spec 内联覆盖共用同一份语义）。 */
export function productionReportReceipt(productionReportId: string, idempotencyKey: string) {
  return confirmedOperationReceipt({
    operationType: 'mes.production-report.record',
    resourceType: 'mes.production-report',
    resourceId: productionReportId,
    idempotencyKey,
    resourceStatus: 'Recorded',
  })
}

/**
 * 该主体在 MES 侧的已授权作业范围清单（`/me/work-context` 的 authorizedScopes）。
 * 用工序任务里真实出现的工作中心，保证「选了范围 → 看到的任务属于该范围」讲得通。
 */
export const authorizedWorkScopes = [
  { kind: 'work-center', id: 'WC-A', displayName: '精加工一线' },
  { kind: 'work-center', id: 'WC-B', displayName: '精加工二线' },
  { kind: 'workshop', id: 'WS-A', displayName: '机加工车间' },
]

/**
 * Realistic MES list rows used by the operation/report flows. Shapes mirror the
 * generated `BusinessConsoleMesOperationTaskRow` / `BusinessConsoleMesWorkOrderItem`
 * so the pages render real titles/subtitles instead of empty states — 含前端
 * fail-closed 校验实际要求的 canonical status 与 qualityStatus。
 */
export const mesOperationTasks = [
  {
    operationTaskId: 'OP-1',
    workOrderId: 'WO-1',
    status: 'InProgress',
    operationSequence: 10,
    workCenterId: 'WC-A',
    // MES currently has no separate business work-order/task number or device display snapshot;
    // public optional display fields stay null instead of echoing strong IDs.
    workOrderNo: null,
    operationTaskNo: null,
    operationCode: 'OP-CUT',
    deviceAssetId: 'device-asset-cnc-01',
    deviceAssetCode: null,
    deviceAssetName: null,
    qualityStatus: 'Pending',
    allowedActions: ['pause', 'complete', 'report'],
    blockReasons: [],
    evaluatedAtUtc: '2026-08-02T08:30:00.000Z',
  },
  {
    operationTaskId: 'OP-2',
    workOrderId: 'WO-1',
    status: 'Queued',
    operationSequence: 20,
    workCenterId: 'WC-B',
    workOrderNo: null,
    operationTaskNo: null,
    operationCode: 'OP-ASSEMBLE',
    deviceAssetId: 'device-asset-lathe-02',
    deviceAssetCode: null,
    deviceAssetName: null,
    qualityStatus: 'Pending',
    allowedActions: [],
    blockReasons: [
      'PREVIOUS_OPERATION_INCOMPLETE: 前序工序尚未完成（工序 10）',
      'MATERIAL_SHORTAGE: 物料 MAT-STEEL 缺口 2',
      'equipment.activeAlarm: 工业遥测存在未解除报警，设备不可用于当前工序。',
      'QUALITY_HOLD_ACTIVE: 工单存在有效质量保留，无法开工',
    ],
    evaluatedAtUtc: '2026-08-02T08:31:00.000Z',
  },
  {
    operationTaskId: 'OP-3',
    workOrderId: 'WO-2',
    workOrderNo: null,
    operationTaskNo: null,
    status: 'Queued',
    operationSequence: 10,
    workCenterId: 'WC-C',
    qualityStatus: 'Pending',
    allowedActions: ['start'],
    blockReasons: [],
    evaluatedAtUtc: '2026-08-02T08:32:00.000Z',
  },
]

const mesManyOperationTasks = Array.from({ length: 501 }, (_, index) => ({
  operationTaskId: `OP-${index + 1}`,
  workOrderId: 'WO-501',
  workOrderNo: null,
  operationTaskNo: null,
  status: 'Queued',
  operationSequence: index + 1,
  workCenterId: 'WC-A',
  qualityStatus: 'Pending',
  allowedActions: ['start'],
  blockReasons: [],
  evaluatedAtUtc: '2026-08-02T08:33:00.000Z',
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
export function routeMes(route: Route, requestUrl: URL, pathname: string, method: string) {
  // 与 BusinessGateway 契约同构：不带 scopeKind/scopeId 只回授权清单、selectedScope 为 null；
  // 带参且命中清单才回填「服务端已核验」的选择；越权选择 403。前端只有走完
  // 「清单 → 选择 → 带参重核验」才拿得到 selectedScope，工序/报工读查询才会发出。
  if (pathname === '/api/business-console/v1/me/work-context') {
    const scopeKind = (requestUrl.searchParams.get('scopeKind') ?? '').trim()
    const scopeId = (requestUrl.searchParams.get('scopeId') ?? '').trim()
    const selectedScope =
      scopeKind && scopeId
        ? authorizedWorkScopes.find((scope) => scope.kind === scopeKind && scope.id === scopeId)
        : undefined
    if (scopeKind && scopeId && !selectedScope) {
      return fulfillJson(route, { success: false, message: '所选作业范围未授权', data: null }, 403)
    }
    return fulfillJson(
      route,
      envelope({
        organizationId: principal.organizationId,
        environmentId: principal.environmentId,
        applicablePermissionCode: requestUrl.searchParams.get('permissionCode') ?? '',
        resolvedAtUtc: '2026-07-31T12:00:00.000Z',
        principal: {
          id: principal.principalId,
          principalType: principal.principalType,
          loginName: principal.loginName,
          roles: [{ id: 'role-pda-operator', displayName: 'PDA 操作员' }],
        },
        resolutionStatus: 'resolved',
        worker: {
          id: 'worker-012',
          userId: principal.principalId,
          employeeNo: workerProfile.employeeNo,
          name: workerProfile.displayName,
          departmentName: '生产部',
          jobTitle: workerProfile.jobTitle,
          employmentStatus: 'active',
        },
        teams: [{ id: 'team-a', name: workerProfile.teams[0]?.teamName ?? '', isLeader: false }],
        authorizedScopes: authorizedWorkScopes,
        availableScopeKinds: ['work-center'],
        selectedScope: selectedScope ?? null,
        issues: [],
      }),
    )
  }

  // ---- MES（工序执行/报工/领料/完工入库） ----
  const base = '/api/business-console/v1/mes'

  if (pathname === `${base}/dispatch-tasks`) {
    return fulfillJson(route, envelope({ items: mesDispatchTasks, total: mesDispatchTasks.length }))
  }

  if (pathname === `${base}/operation-sops/current`) {
    return fulfillJson(route, envelope({ items: [] }))
  }

  // Operation-task actions: start/pause/resume/complete → 成功信封 + 权威回执。
  const operationActionMatch = pathname.match(
    /\/mes\/operation-tasks\/([^/]+)\/(start|pause|resume|complete)$/,
  )
  if (method === 'POST' && operationActionMatch) {
    const operationTaskId = decodeURIComponent(operationActionMatch[1])
    const action = operationActionMatch[2]
    const body = (route.request().postDataJSON() ?? {}) as { idempotencyKey?: string }
    return fulfillJson(
      route,
      envelope({
        operationReceipt: confirmedOperationReceipt({
          operationType: `mes.operation-task.${action}`,
          resourceType: 'mes.operation-task',
          resourceId: operationTaskId,
          idempotencyKey: body.idempotencyKey ?? '',
          resourceStatus: operationActionResultStatus[action],
        }),
      }),
    )
  }
  // 可报工工序精确回读（报工写前的权威事实来源）——与工序任务列表同一份数据、同样的分页。
  if (pathname === `${base}/reportable-operation-tasks` || pathname === `${base}/operation-tasks`) {
    const workOrderId = requestUrl.searchParams.get('workOrderId')
    const operationTaskId = requestUrl.searchParams.get('operationTaskId')?.trim()
    const workOrderScopedItems =
      workOrderId === 'WO-501'
        ? mesManyOperationTasks
        : workOrderId
          ? mesOperationTasks.filter((task) => task.workOrderId === workOrderId)
          : mesOperationTasks
    const scopedItems = operationTaskId
      ? workOrderScopedItems.filter((task) => task.operationTaskId === operationTaskId)
      : workOrderScopedItems
    const skip = Number(requestUrl.searchParams.get('skip') ?? 0)
    const take = Number(requestUrl.searchParams.get('take') ?? 100)
    const items = scopedItems
      .slice(skip, skip + take)
      .map((task) =>
        pathname === `${base}/reportable-operation-tasks`
          ? { ...task, allowedActions: ['report'] }
          : task,
      )
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
      const body = (route.request().postDataJSON() ?? {}) as { idempotencyKey?: string }
      const productionReportId = '019f-e2e-production-report'
      return fulfillJson(
        route,
        envelope({
          productionReportId,
          reportNo: 'RPT-E2E-0001',
          operationReceipt: productionReportReceipt(productionReportId, body.idempotencyKey ?? ''),
        }),
      )
    }
    return fulfillJson(route, envelope({ items: [], total: 0 }))
  }
  const productionReportDetailMatch = pathname.match(
    /^\/api\/business-console\/v1\/mes\/production-reports\/([^/]+)$/,
  )
  if (method === 'GET' && productionReportDetailMatch) {
    const reportNo = decodeURIComponent(productionReportDetailMatch[1])
    return fulfillJson(
      route,
      envelope({
        report: {
          productionReportId: '019f-e2e-production-report',
          reportNo,
          workOrderId: 'WO-1',
          operationTaskId: 'OP-1',
          goodQuantity: 5,
          scrapQuantity: 0,
          reworkQuantity: 0,
          reportedAtUtc: nowUtc,
        },
        consumedMaterialLots: [],
        laborAllocations: [],
      }),
    )
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
  if (pathname === `${base}/line-side-inventory-balances`) {
    const requestedPage = Number(requestUrl.searchParams.get('page') ?? 1)
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
  if (pathname === `${base}/finished-goods-receipt-requests`) {
    if (method === 'POST') return fulfillJson(route, envelope({}))
    return fulfillJson(
      route,
      envelope({ items: mesReceiptRequests, total: mesReceiptRequests.length }),
    )
  }
}
