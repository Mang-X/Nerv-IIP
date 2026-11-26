import { beforeEach, vi } from 'vitest'
import { reactive, shallowRef, type ShallowRef } from 'vue'
import { createPinia, setActivePinia } from 'pinia'
import {
  listBusinessConsoleMesOperationTasks,
  listBusinessConsoleMesReportableOperationTasks,
} from '@nerv-iip/api-client'
import { useBusinessContextStore } from '@/stores/businessContext'

type TestQueryCacheEntry = { key: Array<{ _id: string }> }

type TestInvalidateQueriesOptions = {
  predicate: (entry: TestQueryCacheEntry) => boolean
}

const coladaState = vi.hoisted(() => ({
  invalidateQueries: vi.fn(
    async (_options: TestInvalidateQueriesOptions, _refetchActive?: boolean) => undefined,
  ),
  queryFactoriesById: new Map<
    string,
    () => {
      enabled?: boolean
      autoRefetch?: (state: { status: string }) => number | false
      key?: unknown
      query?: (context: { signal: AbortSignal }) => unknown
    }
  >(),
  queryDataById: new Map<string, unknown>(),
  queryErrorById: new Map<string, unknown>(),
  queryErrorRefById: new Map<string, ShallowRef<unknown>>(),
  queryDataRefById: new Map<string, ShallowRef<unknown>>(),
  queryRefetchById: new Map<string, ReturnType<typeof vi.fn>>(),
}))

const receiptState = vi.hoisted(() => ({
  confirm: vi.fn(),
}))

const workOrderDetailQuery = vi.hoisted(() => vi.fn())

const lineSideInventoryFetch = vi.hoisted(() => vi.fn())

const materialReadinessFetch = vi.hoisted(() => vi.fn())

const authState = vi.hoisted(() => ({
  principal: {
    principalId: 'user-001',
    organizationId: 'org-001',
    environmentId: 'env-dev',
    permissionCodes: [] as string[],
  },
  sessionId: 'session-001',
}))

const reactiveAuthState = reactive(authState)

vi.mock('@nerv-iip/api-client', async (importOriginal) => ({
  BusinessOperationUnconfirmedError: (await importOriginal<typeof import('@nerv-iip/api-client')>())
    .BusinessOperationUnconfirmedError,
  listBusinessConsoleMesReceivableProducedLotsQueryOptions: vi.fn((options) => ({
    key: [{ _id: 'listBusinessConsoleMesReceivableProducedLots', ...options }],
    query: vi.fn(),
  })),
  listBusinessConsoleMesHistoricalProducedLotsQueryOptions: vi.fn((options) => ({
    key: [{ _id: 'listBusinessConsoleMesHistoricalProducedLots', ...options }],
    query: vi.fn(),
  })),
  getBusinessConsoleMesFinishedGoodsReceiptInventoryLinkQueryOptions: vi.fn(({ path, query }) => ({
    key: [{ _id: 'getBusinessConsoleMesFinishedGoodsReceiptInventoryLink', path, query }],
    query: vi.fn(),
  })),
  cancelBusinessConsoleMesWorkOrder: vi.fn(async () => ({
    data: { success: true },
    response: { status: 200 },
  })),
  confirmBusinessConsoleOperation: (...args: unknown[]) => receiptState.confirm(...args),
  acceptBusinessConsoleMesShiftHandoverMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
    })),
  })),
  assignBusinessConsoleMesDispatchTaskMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
    })),
  })),
  cancelBusinessConsoleMesWorkOrderMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
    })),
  })),
  closeBusinessConsoleMesWorkOrderMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
    })),
  })),
  completeBusinessConsoleMesOperationTaskMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
    })),
  })),
  confirmBusinessConsoleMesDowntimeRecoveryMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
    })),
  })),
  convertBusinessConsoleMesPlanToWorkOrderMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
    })),
  })),
  createBusinessConsoleMesFinishedGoodsReceiptRequestMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
    })),
  })),
  retryBusinessConsoleMesFinishedGoodsReceiptInventoryPostingMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
    })),
  })),
  forceReleaseBusinessConsoleMesQualityHoldMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
    })),
  })),
  getBusinessConsoleMesQualityHoldTimelineQueryOptions: vi.fn(() => ({
    key: [{ _id: 'getBusinessConsoleMesQualityHoldTimeline' }],
    query: vi.fn(),
  })),
  createBusinessConsoleMesMaterialIssueRequestMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
    })),
  })),
  confirmBusinessConsoleMesLineSideMaterialReceiptMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async () => ({
      success: true,
      data: { accepted: true },
    })),
  })),
  returnBusinessConsoleMesLineSideMaterialMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
    })),
  })),
  createBusinessConsoleMesRushWorkOrderMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
    })),
  })),
  createBusinessConsoleMesShiftHandoverMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
    })),
  })),
  getBusinessConsoleMesBatchTraceabilityQueryOptions: vi.fn(() => ({
    key: [{ _id: 'getBusinessConsoleMesBatchTraceability' }],
    query: vi.fn(),
  })),
  getBusinessConsoleMesCurrentOperationSopsQueryOptions: vi.fn(() => ({
    key: [{ _id: 'getBusinessConsoleMesCurrentOperationSops' }],
    query: vi.fn(),
  })),
  getBusinessConsoleMesCapacityImpactsQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleMesCapacityImpacts' }],
    query: vi.fn(),
  })),
  getBusinessConsoleMesFoundationReadinessQueryOptions: vi.fn(() => ({
    key: [{ _id: 'getBusinessConsoleMesFoundationReadiness' }],
    query: vi.fn(),
  })),
  getBusinessConsolePrincipalWorkContextQueryOptions: vi.fn(({ query }) => ({
    key: [{ _id: `getBusinessConsolePrincipalWorkContext:${query.permissionCode}` }],
    query: vi.fn(),
  })),
  getBusinessConsoleMesMaterialReadinessQueryOptions: vi.fn(() => ({
    key: [{ _id: 'getBusinessConsoleMesMaterialReadiness' }],
    query: materialReadinessFetch,
  })),
  getBusinessConsoleMesMaterialLotTraceabilityQueryOptions: vi.fn(() => ({
    key: [{ _id: 'getBusinessConsoleMesMaterialLotTraceability' }],
    query: vi.fn(),
  })),
  getBusinessConsoleMesOverviewQueryOptions: vi.fn(() => ({
    key: [{ _id: 'getBusinessConsoleMesOverview' }],
    query: vi.fn(),
  })),
  getBusinessConsoleMesProductionReportQueryOptions: vi.fn(({ path, query }) => ({
    key: [{ _id: 'getBusinessConsoleMesProductionReport', path, query }],
    query: vi.fn(),
  })),
  getBusinessConsoleMesProductionPlanReadinessQueryOptions: vi.fn(() => ({
    key: [{ _id: 'getBusinessConsoleMesProductionPlanReadiness' }],
    query: vi.fn(),
  })),
  getBusinessConsoleMesWipSummaryQueryOptions: vi.fn(() => ({
    key: [{ _id: 'getBusinessConsoleMesWipSummary' }],
    query: vi.fn(),
  })),
  getBusinessConsoleMesWipSummary: vi.fn(),
  getBusinessConsoleMesWorkOrderDetail: vi.fn(),
  listBusinessConsoleMesReceivableProducedLots: vi.fn(),
  getBusinessConsoleMesWorkOrderDetailQueryOptions: vi.fn(() => ({
    key: [{ _id: 'getBusinessConsoleMesWorkOrderDetail' }],
    query: workOrderDetailQuery,
  })),
  getBusinessConsoleMesWorkOrderTraceabilityQueryOptions: vi.fn(() => ({
    key: [{ _id: 'getBusinessConsoleMesWorkOrderTraceability' }],
    query: vi.fn(),
  })),
  holdBusinessConsoleMesWorkOrder: vi.fn(async () => ({
    data: { success: true },
    response: { status: 200 },
  })),
  listBusinessConsoleMesDispatchTasksQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleMesDispatchTasks' }],
    query: vi.fn(),
  })),
  listBusinessConsoleMesDowntimeEventsQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleMesDowntimeEvents' }],
    query: vi.fn(),
  })),
  listBusinessConsoleMesCapacityImpactsQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleMesCapacityImpacts' }],
    query: vi.fn(),
  })),
  listBusinessConsoleMesFinishedGoodsReceiptRequestsQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleMesFinishedGoodsReceiptRequests' }],
    query: vi.fn(),
  })),
  listBusinessConsoleMesMaterialIssueRequestsQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleMesMaterialIssueRequests' }],
    query: vi.fn(),
  })),
  listBusinessConsoleMesLineSideInventoryBalances: (...args: unknown[]) =>
    lineSideInventoryFetch(...args),
  listBusinessConsoleMesLineSideInventoryBalancesQueryOptions: vi.fn((options) => ({
    key: [
      {
        _id: 'listBusinessConsoleMesLineSideInventoryBalances',
        page: options.query.page,
      },
    ],
    query: async (context: { signal?: AbortSignal }) => {
      const response = await lineSideInventoryFetch({
        ...options,
        ...context,
        throwOnError: true,
      })
      return response.data
    },
  })),
  // 原始 fetch fn（供完整分页），默认返回空页；分页测试用 mockImplementation 覆盖成多页。
  listBusinessConsoleMesFinishedGoodsReceiptRequests: vi.fn(async () => ({
    data: { success: true, data: { items: [], total: 0 } },
  })),
  listBusinessConsoleMesMaterialIssueRequests: vi.fn(async () => ({
    data: { success: true, data: { items: [], total: 0 } },
  })),
  listBusinessConsoleMesOperationTasksQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleMesOperationTasks' }],
    query: vi.fn(async () => coladaState.queryDataById.get('listBusinessConsoleMesOperationTasks')),
  })),
  listBusinessConsoleMesOperationTasks: vi.fn(async () => ({
    data: {
      success: true,
      data: {
        items: [{ operationTaskId: 'op-1', workOrderId: 'wo-rush', status: 'InProgress' }],
        total: 1,
      },
    },
  })),
  listBusinessConsoleMesReportableOperationTasks: vi.fn(async () => ({
    data: {
      success: true,
      data: {
        items: [{ operationTaskId: 'op-1', workOrderId: 'wo-rush', status: 'InProgress' }],
        total: 1,
      },
    },
  })),
  recordBusinessConsoleMesProductionReport: vi.fn(async () => ({
    data: { success: true },
    response: { status: 200 },
  })),
  listBusinessConsoleMesProductionPlansQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleMesProductionPlans' }],
    query: vi.fn(),
  })),
  listBusinessConsoleMesProductionReportsQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleMesProductionReports' }],
    query: vi.fn(),
  })),
  listBusinessConsoleMesRelatedQualityItemsQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleMesRelatedQualityItems' }],
    query: vi.fn(),
  })),
  listBusinessConsoleMesShiftHandoversQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleMesShiftHandovers' }],
    query: vi.fn(),
  })),
  getBusinessConsoleMesShiftHandoverQueryOptions: vi.fn(() => ({
    key: [{ _id: 'getBusinessConsoleMesShiftHandover' }],
    query: vi.fn(),
  })),
  listBusinessConsoleMesWorkOrdersQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleMesWorkOrders' }],
    query: vi.fn(),
  })),
  listBusinessConsoleSearchableDirectoryQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleSearchableDirectory' }],
    query: vi.fn(),
  })),
  pauseBusinessConsoleMesOperationTaskMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
    })),
  })),
  recordBusinessConsoleMesDefectV2MutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
    })),
  })),
  recordBusinessConsoleMesEngineeringChangeDecisionMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
    })),
  })),
  recordBusinessConsoleMesDowntimeEventV2MutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
    })),
  })),
  recordBusinessConsoleMesProductionReportMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
    })),
  })),
  releaseBusinessConsoleMesWorkOrderMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
    })),
  })),
  resumeBusinessConsoleMesOperationTaskMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
    })),
  })),
  reverseBusinessConsoleMesProductionReportMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
    })),
  })),
  startBusinessConsoleMesOperationTaskMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
    })),
  })),
}))

vi.mock('@pinia/colada', () => ({
  defineQueryOptions: (options: unknown) => options,
  useMutation: vi.fn((options) => ({
    error: shallowRef(),
    isLoading: shallowRef(false),
    mutateAsync: vi.fn(async (vars) => {
      const result = await options.mutation(vars)
      await options.onSuccess?.(result)
      return result
    }),
  })),
  useQuery: vi.fn((optionsFactory) => {
    const options = optionsFactory()
    const key = Array.isArray(options.key) ? options.key[0] : undefined
    const id = key && typeof key === 'object' && '_id' in key ? String(key._id) : ''
    coladaState.queryFactoriesById.set(id, optionsFactory)

    const initialData = coladaState.queryDataById.get(id)
    const data = shallowRef(
      id === 'listBusinessConsoleMesLineSideInventoryBalances' && initialData !== undefined
        ? { identity: 'org-001:env-dev:page:1', response: initialData }
        : initialData,
    )
    const error = shallowRef(coladaState.queryErrorById.get(id))
    const isLoading = shallowRef(false)
    const refetch = vi.fn(async () => {
      if (id !== 'listBusinessConsoleMesLineSideInventoryBalances') return
      const latest = optionsFactory()
      isLoading.value = true
      try {
        const result = await latest.query?.({ signal: new AbortController().signal })
        data.value = result
        error.value = undefined
        return result
      } catch (queryError) {
        error.value = queryError
        throw queryError
      } finally {
        isLoading.value = false
      }
    })
    coladaState.queryRefetchById.set(id, refetch)
    coladaState.queryDataRefById.set(id, data)
    coladaState.queryErrorRefById.set(id, error)
    return {
      data,
      error,
      isLoading,
      refetch,
    }
  }),
  useQueryCache: vi.fn(() => ({
    invalidateQueries: coladaState.invalidateQueries,
  })),
}))

vi.mock('@/stores/auth', () => ({
  useAuthStore: vi.fn(() => ({
    get principal() {
      return reactiveAuthState.principal
    },
    get sessionId() {
      return reactiveAuthState.sessionId
    },
  })),
}))
export function setupBusinessMesTests() {
  beforeEach(() => {
    setActivePinia(createPinia())
    useBusinessContextStore().patchContext({ organizationId: 'org-001', environmentId: 'env-dev' })
    vi.clearAllMocks()
    coladaState.invalidateQueries.mockImplementation(async () => undefined)
    sessionStorage.clear()
    receiptState.confirm.mockImplementation(async (value) => value)
    workOrderDetailQuery.mockReset().mockResolvedValue({
      success: true,
      data: {
        workOrderId: 'WO-RELEASE',
        status: 'Created',
        productionVersionId: 'PV-1',
        operationTasks: [
          {
            status: 'queued',
            workCenterId: 'WC-READ',
            blockReasons: [],
            evaluatedAtUtc: '2026-08-25T00:00:00.000Z',
          },
        ],
        qualityHolds: [],
      },
    })
    lineSideInventoryFetch.mockReset().mockResolvedValue({
      data: { success: true, data: { items: [], totalCount: 0, page: 1, pageSize: 200 } },
    })
    vi.mocked(listBusinessConsoleMesOperationTasks).mockResolvedValue({
      data: {
        success: true,
        data: {
          items: [{ operationTaskId: 'op-1', workOrderId: 'wo-rush', status: 'InProgress' }],
          total: 1,
        },
      },
    } as never)
    vi.mocked(listBusinessConsoleMesReportableOperationTasks).mockResolvedValue({
      data: {
        success: true,
        data: {
          items: [{ operationTaskId: 'op-1', workOrderId: 'wo-rush', status: 'InProgress' }],
          total: 1,
        },
      },
    } as never)
    coladaState.invalidateQueries.mockClear()
    coladaState.queryFactoriesById.clear()
    coladaState.queryDataById.clear()
    coladaState.queryErrorById.clear()
    coladaState.queryErrorRefById.clear()
    coladaState.queryDataRefById.clear()
    coladaState.queryRefetchById.clear()
    reactiveAuthState.principal = {
      principalId: 'user-001',
      organizationId: 'org-001',
      environmentId: 'env-dev',
      permissionCodes: [],
    }
    reactiveAuthState.sessionId = 'session-001'
    coladaState.queryDataById.set(
      'getBusinessConsolePrincipalWorkContext:business.mes.operations.read',
      {
        success: true,
        data: {
          selectedScope: { kind: 'work-center', id: 'WC-A', displayName: '精加工一线' },
        },
      },
    )
    coladaState.queryDataById.set(
      'getBusinessConsolePrincipalWorkContext:business.mes.operations.manage',
      {
        success: true,
        data: {
          selectedScope: { kind: 'work-center', id: 'WC-A', displayName: '精加工一线' },
        },
      },
    )
    coladaState.queryDataById.set(
      'getBusinessConsolePrincipalWorkContext:business.mes.work-orders.read',
      {
        success: true,
        data: {
          selectedScope: { kind: 'work-center', id: 'WC-A', displayName: '精加工一线' },
        },
      },
    )
    coladaState.queryDataById.set(
      'getBusinessConsolePrincipalWorkContext:business.mes.work-orders.manage',
      {
        success: true,
        data: {
          selectedScope: { kind: 'work-center', id: 'WC-A', displayName: '精加工一线' },
        },
      },
    )
    coladaState.queryDataById.set(
      'getBusinessConsolePrincipalWorkContext:business.mes.reporting.read',
      {
        success: true,
        data: {
          selectedScope: { kind: 'work-center', id: 'WC-A', displayName: '精加工一线' },
        },
      },
    )
    coladaState.queryDataById.set(
      'getBusinessConsolePrincipalWorkContext:business.mes.reporting.write',
      {
        success: true,
        data: { selectedScope: { kind: 'self', id: 'user-001', displayName: '我的任务' } },
      },
    )
    coladaState.queryDataById.set(
      'getBusinessConsolePrincipalWorkContext:business.mes.downtime.manage',
      {
        success: true,
        data: {
          selectedScope: { kind: 'work-center', id: 'WC-A', displayName: '精加工一线' },
        },
      },
    )
  })
}
export {
  coladaState,
  receiptState,
  workOrderDetailQuery,
  lineSideInventoryFetch,
  materialReadinessFetch,
  reactiveAuthState,
}
