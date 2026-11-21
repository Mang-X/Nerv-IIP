import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, vi } from 'vitest'
import { reactive, shallowRef, type ShallowRef } from 'vue'
import {
  getBusinessConsoleMesWorkOrderDetail,
  listBusinessConsoleMesMaterialIssueRequests,
  listBusinessConsoleMesOperationTasks,
  listBusinessConsoleMesReportableOperationTasks,
} from '@nerv-iip/api-client'
import { clearPendingBusinessIntent } from '@nerv-iip/business-core'
import { useMesOperationTasks } from './useBusinessMes'

const coladaState = vi.hoisted(() => ({
  queryDataById: new Map<string, unknown>(),
  queryDataRefById: new Map<string, ShallowRef<unknown>>(),
  loadingById: new Map<string, ShallowRef<boolean>>(),
  refetchById: new Map<string, ReturnType<typeof vi.fn>>(),
  queryOptionsById: new Map<
    string,
    {
      enabled?: boolean
      query?: (context: { signal: AbortSignal }) => unknown
    }
  >(),
  queryFactoriesById: new Map<
    string,
    () => {
      enabled?: boolean
      key?: unknown
      query?: (context: { signal: AbortSignal }) => unknown
    }
  >(),
  queryErrorRefById: new Map<string, ShallowRef<unknown>>(),
  mutateById: new Map<string, ReturnType<typeof vi.fn>>(),
  cancelQueries: vi.fn().mockResolvedValue(undefined),
  invalidateQueries: vi.fn().mockResolvedValue(undefined),
}))

const receiptState = vi.hoisted(() => ({
  confirm: vi.fn(),
}))

const lineSideInventoryFetch = vi.hoisted(() => vi.fn())

const productionReportFetch = vi.hoisted(() => vi.fn())

const authState = vi.hoisted(() => ({
  principal: undefined as
    | {
        principalId?: string
        organizationId?: string
        environmentId?: string
        permissionCodes?: string[]
      }
    | undefined,
  sessionId: 'session-001',
}))

const reactiveAuthState = reactive(authState)

function mockQueryOptions(id: string) {
  return vi.fn(() => ({
    key: [{ _id: id }],
    query: vi.fn(),
  }))
}

function mockMutationOptions(id: string) {
  return vi.fn(() => ({
    key: [{ _id: id }],
    mutation: vi.fn(),
  }))
}

vi.mock('@nerv-iip/api-client', () => ({
  confirmBusinessConsoleOperation: (...args: unknown[]) => receiptState.confirm(...args),
  listBusinessConsoleMesWorkOrdersQueryOptions: mockQueryOptions(
    'listBusinessConsoleMesWorkOrders',
  ),
  getBusinessConsoleMesCurrentOperationSopsQueryOptions: mockQueryOptions(
    'getBusinessConsoleMesCurrentOperationSops',
  ),
  getBusinessConsolePrincipalWorkContextQueryOptions: vi.fn(({ query }) => ({
    key: [{ _id: `getBusinessConsolePrincipalWorkContext:${query.permissionCode}` }],
    query: vi.fn(),
  })),
  getBusinessConsoleMesWorkOrderDetailQueryOptions: mockQueryOptions(
    'getBusinessConsoleMesWorkOrderDetail',
  ),
  getBusinessConsoleMesWorkOrderDetail: vi.fn(),
  getBusinessConsoleMesWorkOrderDetailQueryKey: vi.fn(({ path, query }) => [
    {
      _id: 'getBusinessConsoleMesWorkOrderDetail',
      workOrderId: path.workOrderId,
      organizationId: query.organizationId,
      environmentId: query.environmentId,
    },
  ]),
  listBusinessConsoleMesMaterialIssueRequests: vi.fn(),
  listBusinessConsoleMesOperationTasks: vi.fn(),
  listBusinessConsoleMesReportableOperationTasks: vi.fn(),
  getBusinessConsoleMesProductionReport: (...args: unknown[]) => productionReportFetch(...args),
  listBusinessConsoleMesOperationTasksQueryOptions: mockQueryOptions(
    'listBusinessConsoleMesOperationTasks',
  ),
  listBusinessConsoleMesProductionReportsQueryOptions: mockQueryOptions(
    'listBusinessConsoleMesProductionReports',
  ),
  listBusinessConsoleMesMaterialIssueRequestsQueryOptions: mockQueryOptions(
    'listBusinessConsoleMesMaterialIssueRequests',
  ),
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
  listBusinessConsoleMesFinishedGoodsReceiptRequestsQueryOptions: mockQueryOptions(
    'listBusinessConsoleMesFinishedGoodsReceiptRequests',
  ),
  startBusinessConsoleMesOperationTaskMutationOptions: mockMutationOptions(
    'startBusinessConsoleMesOperationTask',
  ),
  pauseBusinessConsoleMesOperationTaskMutationOptions: mockMutationOptions(
    'pauseBusinessConsoleMesOperationTask',
  ),
  resumeBusinessConsoleMesOperationTaskMutationOptions: mockMutationOptions(
    'resumeBusinessConsoleMesOperationTask',
  ),
  completeBusinessConsoleMesOperationTaskMutationOptions: mockMutationOptions(
    'completeBusinessConsoleMesOperationTask',
  ),
  claimBusinessConsoleMesOperationTaskMutationOptions: mockMutationOptions(
    'claimBusinessConsoleMesOperationTask',
  ),
  recordBusinessConsoleMesProductionReportMutationOptions: mockMutationOptions(
    'recordBusinessConsoleMesProductionReport',
  ),
  listBusinessConsoleMesTelemetryProductionReportCandidatesQueryOptions: mockQueryOptions(
    'listBusinessConsoleMesTelemetryProductionReportCandidates',
  ),
  promoteBusinessConsoleMesTelemetryProductionReportCandidateMutationOptions: mockMutationOptions(
    'promoteBusinessConsoleMesTelemetryProductionReportCandidate',
  ),
  dismissBusinessConsoleMesTelemetryProductionReportCandidateMutationOptions: mockMutationOptions(
    'dismissBusinessConsoleMesTelemetryProductionReportCandidate',
  ),
  createBusinessConsoleMesMaterialIssueRequestMutationOptions: mockMutationOptions(
    'createBusinessConsoleMesMaterialIssueRequest',
  ),
  confirmBusinessConsoleMesLineSideMaterialReceiptMutationOptions: mockMutationOptions(
    'confirmBusinessConsoleMesLineSideMaterialReceipt',
  ),
  returnBusinessConsoleMesLineSideMaterialMutationOptions: mockMutationOptions(
    'returnBusinessConsoleMesLineSideMaterial',
  ),
  createBusinessConsoleMesFinishedGoodsReceiptRequestMutationOptions: mockMutationOptions(
    'createBusinessConsoleMesFinishedGoodsReceiptRequest',
  ),
}))

vi.mock('@pinia/colada', () => ({
  useQuery: vi.fn((optionsFactory) => {
    const options = optionsFactory()
    const key = Array.isArray(options.key) ? options.key[0] : undefined
    const id =
      key && typeof key === 'object' && '_id' in key
        ? String(key._id)
        : typeof key === 'string'
          ? key
          : ''
    coladaState.queryOptionsById.set(id, options)
    coladaState.queryFactoriesById.set(id, optionsFactory)

    const initialData = coladaState.queryDataById.get(id)
    const data = shallowRef(
      id === 'listBusinessConsoleMesLineSideInventoryBalances' && initialData !== undefined
        ? { identity: 'org-001:env-dev:page:1', response: initialData }
        : initialData,
    )
    const isLoading = shallowRef(false)
    const error = shallowRef()
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
    coladaState.refetchById.set(id, refetch)
    coladaState.queryDataRefById.set(id, data)
    coladaState.queryErrorRefById.set(id, error)
    coladaState.loadingById.set(id, isLoading)
    return {
      data,
      error,
      isLoading,
      refetch,
    }
  }),
  useMutation: vi.fn((options) => {
    const key = Array.isArray(options.key) ? options.key[0] : undefined
    const id = key && typeof key === 'object' && '_id' in key ? String(key._id) : ''
    const mutateAsync = vi.fn().mockResolvedValue(undefined)
    coladaState.mutateById.set(id, mutateAsync)

    return {
      error: shallowRef(),
      isLoading: shallowRef(false),
      mutateAsync,
    }
  }),
  useQueryCache: vi.fn(() => ({
    invalidateQueries: coladaState.invalidateQueries,
    cancelQueries: coladaState.cancelQueries,
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

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((resolver) => {
    resolve = resolver
  })
  return { promise, resolve }
}

function operationActionOptions(
  mes: ReturnType<typeof useMesOperationTasks>,
  action: 'start' | 'pause' | 'resume' | 'complete',
  workOrderId: string,
  operationTaskId: string,
  idempotencyKey: string,
  reasonCode?: string,
) {
  return {
    ...(reasonCode ? { reasonCode } : {}),
    idempotencyKey,
    context: mes.captureOperationActionContext(action, workOrderId, operationTaskId),
  }
}
export function setupBusinessMesTests() {
  afterEach(() => {
    vi.useRealTimers()
  })

  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    for (const intent of JSON.parse(
      sessionStorage.getItem('nerv-iip.pending-business-intents.v1') ?? '[]',
    ))
      clearPendingBusinessIntent(intent)
    sessionStorage.clear()
    receiptState.confirm.mockImplementation(async (value) => value)
    lineSideInventoryFetch.mockReset().mockResolvedValue({
      data: { success: true, data: { items: [], totalCount: 0, page: 1, pageSize: 200 } },
    })
    productionReportFetch.mockReset()
    coladaState.queryDataById.clear()
    coladaState.queryDataRefById.clear()
    coladaState.queryErrorRefById.clear()
    coladaState.loadingById.clear()
    coladaState.refetchById.clear()
    coladaState.queryOptionsById.clear()
    coladaState.queryFactoriesById.clear()
    coladaState.mutateById.clear()
    coladaState.cancelQueries.mockClear()
    coladaState.invalidateQueries.mockClear()
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
    reactiveAuthState.principal = {
      principalId: 'user-001',
      organizationId: 'org-001',
      environmentId: 'env-dev',
    }
    authState.sessionId = 'session-001'
    vi.mocked(getBusinessConsoleMesWorkOrderDetail)
      .mockReset()
      .mockImplementation(async ({ path }: { path: { workOrderId: string } }) => {
        const previous = coladaState.mutateById.get('recordBusinessConsoleMesProductionReport')
          ?.mock.lastCall?.[0].body
        return {
          data: {
            success: true,
            data: {
              workOrderId: path.workOrderId,
              operationTasks: [
                {
                  workOrderId: path.workOrderId,
                  operationTaskId:
                    previous?.operationTaskId ?? path.workOrderId.replace(/^wo-/, 'ot-'),
                  status: previous?.completesOperation ? 'Completed' : 'InProgress',
                  allowedActions: previous?.completesOperation ? [] : ['report'],
                },
              ],
            },
          },
        } as never
      })
    vi.mocked(listBusinessConsoleMesOperationTasks)
      .mockReset()
      .mockImplementation(
        async ({
          query,
        }: {
          query: { operationTaskId?: string | null; workOrderId?: string | null }
        }) =>
          ({
            data: {
              success: true,
              data: {
                items: [
                  {
                    operationTaskId: query.operationTaskId,
                    workOrderId: query.workOrderId ?? 'wo-1',
                    status:
                      query.operationTaskId === 'ot-3' || query.operationTaskId === 'ot-reentry'
                        ? 'Queued'
                        : 'InProgress',
                    allowedActions:
                      query.operationTaskId === 'ot-3' || query.operationTaskId === 'ot-reentry'
                        ? ['start']
                        : ['pause', 'complete'],
                    blockReasons: [],
                    evaluatedAtUtc: '2026-08-02T08:30:00.000Z',
                  },
                ],
                total: 1,
              },
            },
          }) as never,
      )
    vi.mocked(listBusinessConsoleMesReportableOperationTasks)
      .mockReset()
      .mockImplementation(
        async ({ query }: { query: { keyword?: string | null; workOrderId?: string | null } }) =>
          ({
            data: {
              success: true,
              data: {
                items: [
                  {
                    operationTaskId: query.keyword,
                    workOrderId: query.workOrderId ?? 'wo-1',
                    status: 'InProgress',
                    allowedActions: ['report'],
                  },
                ],
                total: 1,
              },
            },
          }) as never,
      )
    vi.mocked(listBusinessConsoleMesMaterialIssueRequests)
      .mockReset()
      .mockImplementation(
        async ({ query }: { query: { workOrderId?: string | null } }) =>
          ({
            data: {
              success: true,
              data: {
                items: [
                  {
                    requestId: 'req-2',
                    workOrderId: query.workOrderId ?? 'wo-2',
                    status: 'Requested',
                  },
                ],
                total: 1,
              },
            },
          }) as never,
      )
  })
}
export {
  coladaState,
  receiptState,
  lineSideInventoryFetch,
  productionReportFetch,
  authState,
  reactiveAuthState,
  deferred,
  operationActionOptions,
}
