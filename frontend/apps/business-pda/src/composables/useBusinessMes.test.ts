import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { nextTick, reactive, shallowRef, type ShallowRef } from 'vue'

import {
  completeBusinessConsoleMesOperationTaskMutationOptions,
  confirmBusinessConsoleMesLineSideMaterialReceiptMutationOptions,
  createBusinessConsoleMesFinishedGoodsReceiptRequestMutationOptions,
  createBusinessConsoleMesMaterialIssueRequestMutationOptions,
  createBusinessConsoleSopFileDownloadGrantMutationOptions,
  getBusinessConsolePrincipalWorkContextQueryOptions,
  getBusinessConsoleMesCurrentOperationSopsQueryOptions,
  getBusinessConsoleMesWorkOrderDetailQueryKey,
  getBusinessConsoleMesWorkOrderDetailQueryOptions,
  listBusinessConsoleMesMaterialIssueRequests,
  listBusinessConsoleMesOperationTasks,
  listBusinessConsoleMesOperationTasksQueryOptions,
  listBusinessConsoleMesReportableOperationTasks,
  listBusinessConsoleMesWorkOrdersQueryOptions,
  recordBusinessConsoleMesProductionReportMutationOptions,
  startBusinessConsoleMesOperationTaskMutationOptions,
} from '@nerv-iip/api-client'
import { acquirePendingBusinessIntent } from '@nerv-iip/business-core'

import {
  MES_WORK_SCOPE_UNAVAILABLE_MESSAGE,
  useMesMaterialIssue,
  useMesCurrentOperationSops,
  useMesExactOperationTask,
  useMesOperationTasks,
  useMesProductionReports,
  useMesReceipts,
  useMesWorkOrderDetail,
  useMesWorkOrders,
} from './useBusinessMes'

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
      query?: (context: { signal: AbortSignal }) => unknown
    }
  >(),
  mutateById: new Map<string, ReturnType<typeof vi.fn>>(),
  cancelQueries: vi.fn().mockResolvedValue(undefined),
}))
const receiptState = vi.hoisted(() => ({
  confirm: vi.fn(),
}))

const authState = vi.hoisted(() => ({
  principal: undefined as
    | { principalId?: string; organizationId?: string; environmentId?: string }
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
  listBusinessConsoleMesOperationTasksQueryOptions: mockQueryOptions(
    'listBusinessConsoleMesOperationTasks',
  ),
  listBusinessConsoleMesProductionReportsQueryOptions: mockQueryOptions(
    'listBusinessConsoleMesProductionReports',
  ),
  listBusinessConsoleMesMaterialIssueRequestsQueryOptions: mockQueryOptions(
    'listBusinessConsoleMesMaterialIssueRequests',
  ),
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
  recordBusinessConsoleMesProductionReportMutationOptions: mockMutationOptions(
    'recordBusinessConsoleMesProductionReport',
  ),
  createBusinessConsoleMesMaterialIssueRequestMutationOptions: mockMutationOptions(
    'createBusinessConsoleMesMaterialIssueRequest',
  ),
  confirmBusinessConsoleMesLineSideMaterialReceiptMutationOptions: mockMutationOptions(
    'confirmBusinessConsoleMesLineSideMaterialReceipt',
  ),
  createBusinessConsoleMesFinishedGoodsReceiptRequestMutationOptions: mockMutationOptions(
    'createBusinessConsoleMesFinishedGoodsReceiptRequest',
  ),
  createBusinessConsoleSopFileDownloadGrantMutationOptions: mockMutationOptions(
    'createBusinessConsoleSopFileDownloadGrant',
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

    const refetch = vi.fn()
    coladaState.refetchById.set(id, refetch)
    const data = shallowRef(coladaState.queryDataById.get(id))
    const isLoading = shallowRef(false)
    coladaState.queryDataRefById.set(id, data)
    coladaState.loadingById.set(id, isLoading)
    return {
      data,
      error: shallowRef(),
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
    invalidateQueries: vi.fn().mockResolvedValue(undefined),
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

describe('pda useBusinessMes composables', () => {
  afterEach(() => {
    vi.useRealTimers()
  })

  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    sessionStorage.clear()
    receiptState.confirm.mockImplementation(async (value) => value)
    coladaState.queryDataById.clear()
    coladaState.queryDataRefById.clear()
    coladaState.loadingById.clear()
    coladaState.refetchById.clear()
    coladaState.queryOptionsById.clear()
    coladaState.queryFactoriesById.clear()
    coladaState.mutateById.clear()
    coladaState.cancelQueries.mockClear()
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
    authState.principal = {
      principalId: 'user-001',
      organizationId: 'org-001',
      environmentId: 'env-dev',
    }
    authState.sessionId = 'session-001'
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

  it('keeps the original operation key after accepted-readback failure and PDA page re-entry', async () => {
    receiptState.confirm
      .mockRejectedValueOnce(
        Object.assign(new Error('请求已受理，但权威状态尚未确认'), {
          code: 'business-operation-unconfirmed',
        }),
      )
      .mockImplementation(async (value) => value)

    const firstPage = useMesOperationTasks()
    const firstMutation = coladaState.mutateById.get('startBusinessConsoleMesOperationTask')!
    await expect(
      firstPage.startTask('wo-1', 'ot-reentry', {
        reasonCode: 'OPERATOR_READY',
        idempotencyKey: 'pda-page-key-1',
      }),
    ).rejects.toThrow('权威状态尚未确认')

    const returnedPage = useMesOperationTasks()
    const retryMutation = coladaState.mutateById.get('startBusinessConsoleMesOperationTask')!
    await returnedPage.startTask('wo-1', 'ot-reentry', {
      reasonCode: 'OPERATOR_READY',
      idempotencyKey: 'pda-page-key-2',
    })

    const newIntentPage = useMesOperationTasks()
    const newIntentMutation = coladaState.mutateById.get('startBusinessConsoleMesOperationTask')!
    await newIntentPage.startTask('wo-1', 'ot-reentry', {
      reasonCode: 'OPERATOR_READY',
      idempotencyKey: 'pda-page-key-3',
    })

    expect(firstMutation.mock.calls[0]?.[0].body.idempotencyKey).toBe('pda-page-key-1')
    expect(retryMutation.mock.calls[0]?.[0].body.idempotencyKey).toBe('pda-page-key-1')
    expect(newIntentMutation.mock.calls[0]?.[0].body.idempotencyKey).toBe('pda-page-key-3')
  })

  it('keeps list queries disabled when the principal has no org/env scope', () => {
    reactiveAuthState.principal = undefined

    useMesWorkOrders()
    useMesOperationTasks()
    useMesCurrentOperationSops()

    expect(listBusinessConsoleMesWorkOrdersQueryOptions).toHaveBeenCalledWith({
      query: expect.objectContaining({ organizationId: '', environmentId: '' }),
    })
    expect(coladaState.queryOptionsById.get('listBusinessConsoleMesWorkOrders')?.enabled).toBe(
      false,
    )
    expect(coladaState.queryOptionsById.get('listBusinessConsoleMesOperationTasks')?.enabled).toBe(
      false,
    )
    expect(
      coladaState.queryOptionsById.get('getBusinessConsoleMesCurrentOperationSops')?.enabled,
    ).toBe(false)
  })

  it('loads MES operation tasks in bounded pages and sends filters to the server', async () => {
    coladaState.queryDataById.set('listBusinessConsoleMesOperationTasks', {
      success: true,
      data: { items: [{ operationTaskId: 'ot-1' }], total: 2 },
    })
    vi.mocked(listBusinessConsoleMesOperationTasks).mockResolvedValueOnce({
      data: {
        success: true,
        data: { items: [{ operationTaskId: 'ot-2' }], total: 2 },
      },
    } as never)

    const result = useMesOperationTasks()
    ;(
      result.filters as typeof result.filters & {
        operationTaskId?: string
      }
    ).operationTaskId = 'ot-exact-beyond-first-page'
    result.filters.keyword = undefined
    coladaState.queryFactoriesById.get('listBusinessConsoleMesOperationTasks')?.()
    await nextTick()
    coladaState.queryDataRefById.get('listBusinessConsoleMesOperationTasks')!.value = {
      success: true,
      data: { items: [{ operationTaskId: 'ot-1' }], total: 2 },
    }
    await nextTick()
    await result.loadMore()

    expect(listBusinessConsoleMesOperationTasksQueryOptions).toHaveBeenCalledWith({
      query: expect.objectContaining({
        operationTaskId: 'ot-exact-beyond-first-page',
        skip: 0,
        take: 20,
      }),
    })
    expect(listBusinessConsoleMesOperationTasks).toHaveBeenCalledWith({
      query: expect.objectContaining({
        skip: 1,
        take: 20,
        operationTaskId: 'ot-exact-beyond-first-page',
      }),
      throwOnError: true,
    })
    expect(result.operationTasks.value.map((task) => task.operationTaskId)).toEqual([
      'ot-1',
      'ot-2',
    ])
  })

  it('does not manually refresh work-order, operation, or report lists without org/env scope', async () => {
    authState.principal = undefined

    const workOrders = useMesWorkOrders()
    const operationTasks = useMesOperationTasks()
    const reports = useMesProductionReports()
    await Promise.all([workOrders.refresh(), operationTasks.refresh(), reports.refresh()])

    expect(coladaState.refetchById.get('listBusinessConsoleMesWorkOrders')).not.toHaveBeenCalled()
    expect(
      coladaState.refetchById.get('listBusinessConsoleMesOperationTasks'),
    ).not.toHaveBeenCalled()
    expect(
      coladaState.refetchById.get('listBusinessConsoleMesProductionReports'),
    ).not.toHaveBeenCalled()
  })

  it('does not manually refresh work-order or operation lists before read scopes are ready', async () => {
    coladaState.queryDataById.delete(
      'getBusinessConsolePrincipalWorkContext:business.mes.work-orders.read',
    )
    coladaState.queryDataById.delete(
      'getBusinessConsolePrincipalWorkContext:business.mes.operations.read',
    )

    const workOrders = useMesWorkOrders()
    const operationTasks = useMesOperationTasks()
    await Promise.all([workOrders.refresh(), operationTasks.refresh()])

    expect(coladaState.refetchById.get('listBusinessConsoleMesWorkOrders')).not.toHaveBeenCalled()
    expect(
      coladaState.refetchById.get('listBusinessConsoleMesOperationTasks'),
    ).not.toHaveBeenCalled()
    expect(workOrders.workOrderReadScopeReady.value).toBe(false)
    expect(workOrders.workOrderReadScopeMessage.value).toContain('尚未选择已授权作业范围')
    expect(operationTasks.operationListScopeReady.value).toBe(false)
    expect(operationTasks.operationListScopeMessage.value).toContain('尚未选择已授权作业范围')
  })

  it('does not manually refresh material-issue or receipt lists without org/env scope', async () => {
    authState.principal = undefined

    const materialIssue = useMesMaterialIssue()
    const receipts = useMesReceipts()
    await Promise.all([materialIssue.refresh(), receipts.refresh()])

    expect(
      coladaState.refetchById.get('listBusinessConsoleMesMaterialIssueRequests'),
    ).not.toHaveBeenCalled()
    expect(
      coladaState.refetchById.get('listBusinessConsoleMesFinishedGoodsReceiptRequests'),
    ).not.toHaveBeenCalled()
  })

  it('records freshness only after successful material-issue and receipt responses', () => {
    coladaState.queryDataById.set('listBusinessConsoleMesMaterialIssueRequests', {
      success: true,
      data: { items: [], total: 0 },
    })
    coladaState.queryDataById.set('listBusinessConsoleMesFinishedGoodsReceiptRequests', {
      success: true,
      data: { items: [], total: 0 },
    })

    const materialIssue = useMesMaterialIssue()
    const receipts = useMesReceipts()

    expect(materialIssue.lastUpdatedAt.value).not.toBeNull()
    expect(receipts.lastUpdatedAt.value).not.toBeNull()
  })

  it('exposes failed material-issue and receipt envelopes instead of treating them as successful empty lists', () => {
    coladaState.queryDataById.set('listBusinessConsoleMesMaterialIssueRequests', {
      success: false,
      message: '领料申请查询失败',
    })
    coladaState.queryDataById.set('listBusinessConsoleMesFinishedGoodsReceiptRequests', {
      success: false,
      message: '完工入库申请查询失败',
    })

    const materialIssue = useMesMaterialIssue()
    const receipts = useMesReceipts()

    expect(materialIssue.hasSuccessfulResponse.value).toBe(false)
    expect(materialIssue.hasFailedResponse.value).toBe(true)
    expect(receipts.hasSuccessfulResponse.value).toBe(false)
    expect(receipts.hasFailedResponse.value).toBe(true)
  })

  it('does not report stale successful envelopes as current success while refreshing', async () => {
    coladaState.queryDataById.set('listBusinessConsoleMesMaterialIssueRequests', {
      success: true,
      data: { items: [], total: 0 },
    })
    coladaState.queryDataById.set('listBusinessConsoleMesFinishedGoodsReceiptRequests', {
      success: true,
      data: { items: [], total: 0 },
    })

    const materialIssue = useMesMaterialIssue()
    const receipts = useMesReceipts()
    expect(materialIssue.hasSuccessfulResponse.value).toBe(true)
    expect(receipts.hasSuccessfulResponse.value).toBe(true)

    coladaState.loadingById.get('listBusinessConsoleMesMaterialIssueRequests')!.value = true
    coladaState.loadingById.get('listBusinessConsoleMesFinishedGoodsReceiptRequests')!.value = true
    await nextTick()

    expect(materialIssue.hasSuccessfulResponse.value).toBe(false)
    expect(materialIssue.hasFailedResponse.value).toBe(false)
    expect(receipts.hasSuccessfulResponse.value).toBe(false)
    expect(receipts.hasFailedResponse.value).toBe(false)
  })

  it('removes cached issue and receipt rows outside scope and waits for the restored scope response', async () => {
    vi.useFakeTimers()
    vi.setSystemTime('2026-07-28T01:00:00.000Z')
    coladaState.queryDataById.set('listBusinessConsoleMesMaterialIssueRequests', {
      success: true,
      data: {
        items: [{ requestId: 'OLD-ISSUE', workOrderId: 'OLD-WO' }],
        total: 9,
      },
    })
    coladaState.queryDataById.set('listBusinessConsoleMesFinishedGoodsReceiptRequests', {
      success: true,
      data: {
        items: [{ receiptRequestId: 'OLD-RECEIPT', workOrderId: 'OLD-WO' }],
        total: 8,
      },
    })

    const materialIssue = useMesMaterialIssue()
    const receipts = useMesReceipts()
    expect(materialIssue.requests.value).toHaveLength(1)
    expect(materialIssue.total.value).toBe(9)
    expect(receipts.receipts.value).toHaveLength(1)
    expect(receipts.total.value).toBe(8)
    expect(materialIssue.lastUpdatedAt.value).toBe('2026-07-28T01:00:00.000Z')
    expect(receipts.lastUpdatedAt.value).toBe('2026-07-28T01:00:00.000Z')

    reactiveAuthState.principal = undefined
    await nextTick()
    await Promise.all([materialIssue.refresh(), receipts.refresh()])

    expect(materialIssue.requests.value).toEqual([])
    expect(materialIssue.total.value).toBe(0)
    expect(receipts.receipts.value).toEqual([])
    expect(receipts.total.value).toBe(0)
    expect(materialIssue.lastUpdatedAt.value).toBeNull()
    expect(receipts.lastUpdatedAt.value).toBeNull()
    expect(
      coladaState.refetchById.get('listBusinessConsoleMesMaterialIssueRequests'),
    ).not.toHaveBeenCalled()
    expect(
      coladaState.refetchById.get('listBusinessConsoleMesFinishedGoodsReceiptRequests'),
    ).not.toHaveBeenCalled()

    reactiveAuthState.principal = { organizationId: 'org-002', environmentId: 'env-prod' }
    await nextTick()

    expect(materialIssue.requests.value).toEqual([])
    expect(materialIssue.total.value).toBe(0)
    expect(materialIssue.hasSuccessfulResponse.value).toBe(false)
    expect(receipts.receipts.value).toEqual([])
    expect(receipts.total.value).toBe(0)
    expect(receipts.hasSuccessfulResponse.value).toBe(false)
    expect(materialIssue.lastUpdatedAt.value).toBeNull()
    expect(receipts.lastUpdatedAt.value).toBeNull()

    coladaState.queryDataRefById.get('listBusinessConsoleMesMaterialIssueRequests')!.value = {
      success: false,
    }
    coladaState.queryDataRefById.get('listBusinessConsoleMesFinishedGoodsReceiptRequests')!.value =
      { success: false }
    await nextTick()
    expect(materialIssue.lastUpdatedAt.value).toBeNull()
    expect(receipts.lastUpdatedAt.value).toBeNull()

    vi.setSystemTime('2026-07-28T02:00:00.000Z')
    coladaState.queryDataRefById.get('listBusinessConsoleMesMaterialIssueRequests')!.value = {
      success: true,
      data: {
        items: [{ requestId: 'NEW-ISSUE', workOrderId: 'NEW-WO' }],
        total: 1,
      },
    }
    coladaState.queryDataRefById.get('listBusinessConsoleMesFinishedGoodsReceiptRequests')!.value =
      {
        success: true,
        data: {
          items: [{ receiptRequestId: 'NEW-RECEIPT', workOrderId: 'NEW-WO' }],
          total: 1,
        },
      }
    await nextTick()

    expect(materialIssue.requests.value).toEqual([
      expect.objectContaining({ requestId: 'NEW-ISSUE' }),
    ])
    expect(materialIssue.total.value).toBe(1)
    expect(receipts.receipts.value).toEqual([
      expect.objectContaining({ receiptRequestId: 'NEW-RECEIPT' }),
    ])
    expect(receipts.total.value).toBe(1)
    expect(materialIssue.lastUpdatedAt.value).toBe('2026-07-28T02:00:00.000Z')
    expect(receipts.lastUpdatedAt.value).toBe('2026-07-28T02:00:00.000Z')

    coladaState.loadingById.get('listBusinessConsoleMesMaterialIssueRequests')!.value = true
    coladaState.loadingById.get('listBusinessConsoleMesFinishedGoodsReceiptRequests')!.value = true
    await nextTick()

    expect(materialIssue.requests.value).toEqual([
      expect.objectContaining({ requestId: 'NEW-ISSUE' }),
    ])
    expect(receipts.receipts.value).toEqual([
      expect.objectContaining({ receiptRequestId: 'NEW-RECEIPT' }),
    ])
    expect(materialIssue.lastUpdatedAt.value).toBe('2026-07-28T02:00:00.000Z')
    expect(receipts.lastUpdatedAt.value).toBe('2026-07-28T02:00:00.000Z')
  })

  it('enables list queries once a principal scope is present', () => {
    useMesWorkOrders()

    expect(listBusinessConsoleMesWorkOrdersQueryOptions).toHaveBeenCalledWith({
      query: expect.objectContaining({ organizationId: 'org-001', environmentId: 'env-dev' }),
    })
    expect(coladaState.queryOptionsById.get('listBusinessConsoleMesWorkOrders')?.enabled).toBe(true)
  })

  it('exposes failed or malformed work-order, operation, and report responses', () => {
    coladaState.queryDataById.set('listBusinessConsoleMesWorkOrders', {
      success: false,
      message: '工单查询失败',
    })
    coladaState.queryDataById.set('listBusinessConsoleMesOperationTasks', [])
    coladaState.queryDataById.set('listBusinessConsoleMesProductionReports', {
      data: { items: [], total: 0 },
    })

    const workOrders = useMesWorkOrders()
    const operationTasks = useMesOperationTasks()
    const reports = useMesProductionReports()

    expect(workOrders.workOrders.value).toEqual([])
    expect(workOrders.total.value).toBe(0)
    expect(workOrders.hasSuccessfulResponse.value).toBe(false)
    expect(workOrders.hasFailedResponse.value).toBe(true)
    expect(operationTasks.operationTasks.value).toEqual([])
    expect(operationTasks.total.value).toBe(0)
    expect(operationTasks.hasSuccessfulResponse.value).toBe(false)
    expect(operationTasks.hasFailedResponse.value).toBe(true)
    expect(reports.productionReports.value).toEqual([])
    expect(reports.total.value).toBe(0)
    expect(reports.hasSuccessfulResponse.value).toBe(false)
    expect(reports.hasFailedResponse.value).toBe(true)
  })

  it('unbinds work-order, operation, and report projections on an org/env scope switch', async () => {
    for (const id of [
      'listBusinessConsoleMesWorkOrders',
      'listBusinessConsoleMesOperationTasks',
      'listBusinessConsoleMesProductionReports',
    ]) {
      coladaState.queryDataById.set(id, {
        success: true,
        data: { items: [{ id: `old-${id}` }], total: 4 },
      })
    }

    const workOrders = useMesWorkOrders()
    const operationTasks = useMesOperationTasks()
    const reports = useMesProductionReports()
    expect(workOrders.workOrders.value).toHaveLength(1)
    expect(operationTasks.operationTasks.value).toHaveLength(1)
    expect(reports.productionReports.value).toHaveLength(1)
    expect(workOrders.lastUpdatedAt.value).not.toBeNull()
    expect(operationTasks.lastUpdatedAt.value).not.toBeNull()
    expect(reports.lastUpdatedAt.value).not.toBeNull()

    reactiveAuthState.principal = { organizationId: 'org-002', environmentId: 'env-prod' }
    await nextTick()

    expect(workOrders.workOrders.value).toEqual([])
    expect(workOrders.total.value).toBe(0)
    expect(workOrders.hasSuccessfulResponse.value).toBe(false)
    expect(operationTasks.operationTasks.value).toEqual([])
    expect(operationTasks.total.value).toBe(0)
    expect(operationTasks.hasSuccessfulResponse.value).toBe(false)
    expect(reports.productionReports.value).toEqual([])
    expect(reports.total.value).toBe(0)
    expect(reports.hasSuccessfulResponse.value).toBe(false)
    expect(workOrders.lastUpdatedAt.value).toBeNull()
    expect(operationTasks.lastUpdatedAt.value).toBeNull()
    expect(reports.lastUpdatedAt.value).toBeNull()
  })

  it('uses the exact strong-ID work-order detail query for report route identity', () => {
    const workOrderId = shallowRef('WO-OUTSIDE-101')
    useMesWorkOrderDetail(workOrderId)

    expect(getBusinessConsoleMesWorkOrderDetailQueryOptions).toHaveBeenCalledWith({
      path: { workOrderId: 'WO-OUTSIDE-101' },
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        scopeKind: 'work-center',
        scopeId: 'WC-A',
      },
    })
    expect(coladaState.queryOptionsById.get('getBusinessConsoleMesWorkOrderDetail')?.enabled).toBe(
      true,
    )
  })

  it('exposes success:false and malformed work-order detail responses as retryable failures', async () => {
    coladaState.queryDataById.set('getBusinessConsoleMesWorkOrderDetail', {
      success: false,
      message: '工单详情查询失败',
    })
    const result = useMesWorkOrderDetail(shallowRef('WO-501'))

    expect(result.workOrder.value).toBeUndefined()
    expect(result.hasSuccessfulResponse.value).toBe(false)
    expect(result.hasFailedResponse.value).toBe(true)
    expect(result.error.value).toBeInstanceOf(Error)
    expect((result.error.value as Error).message).toBe('工单详情查询失败')
    expect(result.lastUpdatedAt.value).toBeNull()

    coladaState.queryDataRefById.get('getBusinessConsoleMesWorkOrderDetail')!.value = {
      success: true,
      data: null,
    }
    await nextTick()

    expect(result.workOrder.value).toBeUndefined()
    expect(result.hasSuccessfulResponse.value).toBe(false)
    expect(result.hasFailedResponse.value).toBe(true)
    expect((result.error.value as Error).message).toBe('工单详情响应无效，请重试。')
    expect(result.lastUpdatedAt.value).toBeNull()

    coladaState.queryDataRefById.get('getBusinessConsoleMesWorkOrderDetail')!.value = [
      { workOrderId: 'WO-501' },
    ]
    await nextTick()

    expect(result.workOrder.value).toBeUndefined()
    expect(result.hasSuccessfulResponse.value).toBe(false)
    expect(result.hasFailedResponse.value).toBe(true)
    expect((result.error.value as Error).message).toBe('工单详情响应无效，请重试。')
    expect(result.lastUpdatedAt.value).toBeNull()
  })

  it.each([
    ['missing required fields', { workOrderId: 'WO-501' }],
    [
      'non-array operation tasks',
      {
        workOrderId: 'WO-501',
        skuId: 'SKU-501',
        quantity: 1,
        status: 'Released',
        readinessStatus: 'ready',
        blockingReasons: [],
        operationTasks: {},
      },
    ],
    [
      'non-canonical work-order identity',
      {
        workOrderId: ' WO-501 ',
        skuId: 'SKU-501',
        quantity: 1,
        status: 'Released',
        readinessStatus: 'ready',
        blockingReasons: [],
        operationTasks: [],
      },
    ],
  ])('rejects a success:true detail payload with %s', (_name, data) => {
    coladaState.queryDataById.set('getBusinessConsoleMesWorkOrderDetail', {
      success: true,
      data,
    })

    const result = useMesWorkOrderDetail(shallowRef('WO-501'))

    expect(result.workOrder.value).toBeUndefined()
    expect(result.hasSuccessfulResponse.value).toBe(false)
    expect(result.hasFailedResponse.value).toBe(true)
    expect((result.error.value as Error).message).toBe('工单详情响应无效，请重试。')
    expect(result.lastUpdatedAt.value).toBeNull()
  })

  it('unbinds work-order detail and freshness when the work-order or org/env identity changes', async () => {
    coladaState.queryDataById.set('getBusinessConsoleMesWorkOrderDetail', {
      success: true,
      data: {
        workOrderId: 'WO-A',
        skuId: 'SKU-A',
        quantity: 1,
        status: 'Released',
        readinessStatus: 'ready',
        blockingReasons: [],
        operationTasks: [],
      },
    })
    const workOrderId = shallowRef('WO-A')
    const result = useMesWorkOrderDetail(workOrderId)

    expect(result.workOrder.value?.workOrderId).toBe('WO-A')
    expect(result.hasSuccessfulResponse.value).toBe(true)
    expect(result.lastUpdatedAt.value).not.toBeNull()

    workOrderId.value = 'WO-B'
    await nextTick()
    expect(result.workOrder.value).toBeUndefined()
    expect(result.hasSuccessfulResponse.value).toBe(false)
    expect(result.hasFailedResponse.value).toBe(false)
    expect(result.lastUpdatedAt.value).toBeNull()

    coladaState.queryDataRefById.get('getBusinessConsoleMesWorkOrderDetail')!.value = {
      success: true,
      data: {
        workOrderId: 'WO-A',
        skuId: 'SKU-A',
        quantity: 1,
        status: 'Released',
        readinessStatus: 'ready',
        blockingReasons: [],
        operationTasks: [],
      },
    }
    await nextTick()
    expect(result.workOrder.value).toBeUndefined()
    expect(result.hasSuccessfulResponse.value).toBe(false)
    expect(result.hasFailedResponse.value).toBe(false)
    expect(result.lastUpdatedAt.value).toBeNull()

    coladaState.queryDataRefById.get('getBusinessConsoleMesWorkOrderDetail')!.value = {
      success: true,
      data: {
        workOrderId: 'WO-B',
        skuId: 'SKU-B',
        quantity: 2,
        status: 'Released',
        readinessStatus: 'ready',
        blockingReasons: [],
        operationTasks: [],
      },
    }
    await nextTick()
    expect(result.workOrder.value?.workOrderId).toBe('WO-B')
    expect(result.lastUpdatedAt.value).not.toBeNull()

    reactiveAuthState.principal = { organizationId: 'org-002', environmentId: 'env-prod' }
    await nextTick()
    expect(result.workOrder.value).toBeUndefined()
    expect(result.hasSuccessfulResponse.value).toBe(false)
    expect(result.hasFailedResponse.value).toBe(false)
    expect(result.lastUpdatedAt.value).toBeNull()
  })

  it('continues exact task pagination across a full page when total is omitted', async () => {
    vi.mocked(listBusinessConsoleMesReportableOperationTasks)
      .mockResolvedValueOnce({
        data: {
          success: true,
          data: {
            items: Array.from({ length: 100 }, (_, index) => ({
              operationTaskId: `OP-${index + 1}`,
              workOrderId: 'WO-501',
            })),
          },
        },
      } as never)
      .mockResolvedValueOnce({
        data: {
          success: true,
          data: {
            items: [{ operationTaskId: 'OP-101', workOrderId: 'WO-501' }],
          },
        },
      } as never)
    const detail = shallowRef({
      workOrderId: 'WO-501',
      operationTasks: [],
    })

    useMesExactOperationTask(shallowRef('WO-501'), shallowRef('OP-101'), detail as never)
    const options = coladaState.queryFactoriesById.get('mes-report-exact-operation-task')?.()
    const result = await options?.query?.({ signal: new AbortController().signal })

    expect(result).toMatchObject({ operationTaskId: 'OP-101', workOrderId: 'WO-501' })
    expect(listBusinessConsoleMesReportableOperationTasks).toHaveBeenNthCalledWith(
      2,
      expect.objectContaining({
        query: expect.objectContaining({
          skip: 100,
          take: 100,
          workOrderId: 'WO-501',
          scopeKind: 'work-center',
          scopeId: 'WC-A',
        }),
      }),
    )
  })

  it('does not query or manually refresh an exact report task before reporting-read scope is ready', async () => {
    coladaState.queryDataById.delete(
      'getBusinessConsolePrincipalWorkContext:business.mes.reporting.read',
    )
    const detail = shallowRef({
      workOrderId: 'WO-501',
      operationTasks: [],
    })

    const exactTask = useMesExactOperationTask(
      shallowRef('WO-501'),
      shallowRef('OP-101'),
      detail as never,
    )
    const options = coladaState.queryFactoriesById.get('mes-report-exact-operation-task')?.()
    await exactTask.refresh()

    expect(options?.enabled).toBe(false)
    expect(coladaState.refetchById.get('mes-report-exact-operation-task')).not.toHaveBeenCalled()
    expect(listBusinessConsoleMesReportableOperationTasks).not.toHaveBeenCalled()
    expect(exactTask.reportingReadScopeReady.value).toBe(false)
    expect(exactTask.reportingReadScopeMessage.value).toContain('尚未选择已授权作业范围')
  })

  it('freezes exact task scope for every page of one query execution', async () => {
    let resolveFirstPage!: (value: unknown) => void
    vi.mocked(listBusinessConsoleMesReportableOperationTasks)
      .mockReturnValueOnce(
        new Promise((resolve) => {
          resolveFirstPage = resolve
        }) as never,
      )
      .mockResolvedValueOnce({
        data: {
          success: true,
          data: {
            items: [{ operationTaskId: 'OP-101', workOrderId: 'WO-501' }],
          },
        },
      } as never)
    const detail = shallowRef({
      workOrderId: 'WO-501',
      operationTasks: [],
    })

    useMesExactOperationTask(shallowRef('WO-501'), shallowRef('OP-101'), detail as never)
    const options = coladaState.queryFactoriesById.get('mes-report-exact-operation-task')?.()
    const resultPromise = options?.query?.({ signal: new AbortController().signal })
    reactiveAuthState.principal = {
      organizationId: 'org-002',
      environmentId: 'env-prod',
    }
    await nextTick()
    resolveFirstPage({
      data: {
        success: true,
        data: {
          items: Array.from({ length: 100 }, (_, index) => ({
            operationTaskId: `OP-${index + 1}`,
            workOrderId: 'WO-501',
          })),
        },
      },
    })
    await resultPromise

    expect(listBusinessConsoleMesReportableOperationTasks).toHaveBeenNthCalledWith(
      2,
      expect.objectContaining({
        query: expect.objectContaining({
          organizationId: 'org-001',
          environmentId: 'env-dev',
          skip: 100,
        }),
      }),
    )
  })

  it('queries current operation SOPs only after operation code is selected', () => {
    coladaState.queryDataById.set('getBusinessConsoleMesCurrentOperationSops', {
      success: true,
      data: {
        items: [
          { documentNumber: 'SOP-10', revision: 'A', operationCode: 'OP-10', fileId: 'file-10' },
        ],
      },
    })
    const sops = useMesCurrentOperationSops()

    expect(
      coladaState.queryOptionsById.get('getBusinessConsoleMesCurrentOperationSops')?.enabled,
    ).toBe(false)

    sops.filters.operationCode = ' OP-10 '
    sops.filters.workCenterCode = ' WC-10 '
    const options = coladaState.queryFactoriesById.get(
      'getBusinessConsoleMesCurrentOperationSops',
    )?.()

    expect(getBusinessConsoleMesCurrentOperationSopsQueryOptions).toHaveBeenLastCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        operationCode: 'OP-10',
        workCenterCode: 'WC-10',
      },
    })
    expect(options).toMatchObject({ enabled: true })
    expect(sops.currentSops.value[0]).toMatchObject({ fileId: 'file-10' })
  })

  it('exposes a generated SDK mutation path for SOP file download grants', () => {
    useMesCurrentOperationSops()

    expect(createBusinessConsoleSopFileDownloadGrantMutationOptions).toHaveBeenCalled()
  })

  it('records a production report forwarding the caller-supplied idempotency key + business fields', async () => {
    const { recordReport } = useMesProductionReports()

    await recordReport({
      workOrderId: 'wo-1',
      operationTaskId: 'ot-1',
      goodQuantity: 9,
      scrapQuantity: 1,
      completesOperation: true,
      idempotencyKey: 'op-report-1',
    })

    expect(recordBusinessConsoleMesProductionReportMutationOptions).toHaveBeenCalled()
    const mutateAsync = coladaState.mutateById.get('recordBusinessConsoleMesProductionReport')
    expect(mutateAsync).toHaveBeenCalledTimes(1)
    const payload = mutateAsync!.mock.calls[0][0]
    expect(payload.body).toMatchObject({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      workOrderId: 'wo-1',
      operationTaskId: 'ot-1',
      goodQuantity: 9,
      scrapQuantity: 1,
      completesOperation: true,
      scopeKind: 'self',
      scopeId: 'user-001',
    })
    // caller-supplied key passes through verbatim
    expect(payload.body.idempotencyKey).toBe('op-report-1')
    expect(payload.body.reportedAtUtc).toBeTruthy()
    expect(listBusinessConsoleMesReportableOperationTasks).toHaveBeenCalledWith(
      expect.objectContaining({
        query: expect.objectContaining({
          scopeKind: 'self',
          scopeId: 'user-001',
        }),
      }),
    )
  })

  it('does not send a production report when no reporting scope is selected', async () => {
    coladaState.queryDataById.set(
      'getBusinessConsolePrincipalWorkContext:business.mes.reporting.write',
      { success: true, data: { selectedScope: null } },
    )
    const { recordReport, reportScopeReady } = useMesProductionReports()
    const mutateAsync = coladaState.mutateById.get('recordBusinessConsoleMesProductionReport')!

    expect(reportScopeReady.value).toBe(false)
    await expect(
      recordReport({
        workOrderId: 'wo-no-scope',
        operationTaskId: 'ot-no-scope',
        goodQuantity: 1,
        scrapQuantity: 0,
        completesOperation: false,
        idempotencyKey: 'report-no-scope',
      }),
      // 响应成功但授权清单为空 = 「一个授权范围都没有」，与「还没选」是两回事（#1297）。
    ).rejects.toThrow(MES_WORK_SCOPE_UNAVAILABLE_MESSAGE)
    expect(mutateAsync).not.toHaveBeenCalled()
  })

  it('fails closed before report mutation when frozen write scope cannot pass reportable readback', async () => {
    vi.mocked(listBusinessConsoleMesReportableOperationTasks).mockRejectedValueOnce({
      status: 403,
      message: 'scope not readable',
    })
    const { recordReport } = useMesProductionReports()
    const mutateAsync = coladaState.mutateById.get('recordBusinessConsoleMesProductionReport')!

    await expect(
      recordReport({
        workOrderId: 'wo-write-only',
        operationTaskId: 'ot-write-only',
        goodQuantity: 1,
        scrapQuantity: 0,
        completesOperation: true,
        idempotencyKey: 'report-write-only',
      }),
    ).rejects.toMatchObject({ status: 403 })

    expect(listBusinessConsoleMesReportableOperationTasks).toHaveBeenCalledWith(
      expect.objectContaining({
        query: expect.objectContaining({
          scopeKind: 'self',
          scopeId: 'user-001',
        }),
      }),
    )
    expect(mutateAsync).not.toHaveBeenCalled()
  })

  it('completes an operation task forwarding the caller-supplied idempotency key', async () => {
    const { completeTask } = useMesOperationTasks()

    await completeTask('wo-1', 'ot-9', { idempotencyKey: 'op-complete-1' })

    expect(completeBusinessConsoleMesOperationTaskMutationOptions).toHaveBeenCalled()
    const mutateAsync = coladaState.mutateById.get('completeBusinessConsoleMesOperationTask')
    expect(mutateAsync).toHaveBeenCalledTimes(1)
    const payload = mutateAsync!.mock.calls[0][0]
    expect(payload.path).toEqual({ operationTaskId: 'ot-9' })
    expect(payload.query).toEqual({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      scopeKind: 'work-center',
      scopeId: 'WC-A',
    })
    expect(payload.body.idempotencyKey).toBe('op-complete-1')
    expect(listBusinessConsoleMesOperationTasks).toHaveBeenCalledWith(
      expect.objectContaining({
        query: expect.objectContaining({
          scopeKind: 'work-center',
          scopeId: 'WC-A',
        }),
      }),
    )
  })

  it('fails closed before task readback and mutation when no operation scope is selected', async () => {
    coladaState.queryDataById.set(
      'getBusinessConsolePrincipalWorkContext:business.mes.operations.manage',
      { success: true, data: { selectedScope: null } },
    )
    vi.mocked(listBusinessConsoleMesOperationTasks).mockClear()
    const { startTask, operationScopeReady } = useMesOperationTasks()
    const mutateAsync = coladaState.mutateById.get('startBusinessConsoleMesOperationTask')!

    expect(operationScopeReady.value).toBe(false)
    await expect(
      startTask('wo-1', 'ot-no-scope', { idempotencyKey: 'operation-no-scope' }),
      // 同上：授权清单为空的真话是「没有已授权范围」，不能说成「还没选」（#1297）。
    ).rejects.toThrow(MES_WORK_SCOPE_UNAVAILABLE_MESSAGE)
    expect(listBusinessConsoleMesOperationTasks).not.toHaveBeenCalled()
    expect(mutateAsync).not.toHaveBeenCalled()
  })

  it('clears an operation intent after a determinate 422 so the next attempt uses a new key', async () => {
    const { pauseTask } = useMesOperationTasks()
    const mutateAsync = coladaState.mutateById.get('pauseBusinessConsoleMesOperationTask')!
    mutateAsync
      .mockRejectedValueOnce({ status: 422, message: 'invalid transition' })
      .mockResolvedValueOnce({ success: true, data: {} })

    await expect(
      pauseTask('wo-1', 'ot-determinate', { idempotencyKey: 'operation-key-1' }),
    ).rejects.toMatchObject({ status: 422 })
    await pauseTask('wo-1', 'ot-determinate', { idempotencyKey: 'operation-key-2' })

    expect(mutateAsync.mock.calls[0][0].body.idempotencyKey).toBe('operation-key-1')
    expect(mutateAsync.mock.calls[1][0].body.idempotencyKey).toBe('operation-key-2')
  })

  it('re-reads the exact operation task and blocks a completed task before mutation', async () => {
    vi.mocked(listBusinessConsoleMesOperationTasks).mockResolvedValue({
      data: {
        success: true,
        data: {
          items: [{ operationTaskId: 'ot-9', workOrderId: 'wo-1', status: 'Completed' }],
          total: 1,
        },
      },
    } as never)
    const { completeTask } = useMesOperationTasks()

    await expect(completeTask('wo-1', 'ot-9', { idempotencyKey: 'op-complete-1' })).rejects.toThrow(
      '状态已被其他操作更新',
    )

    expect(
      coladaState.mutateById.get('completeBusinessConsoleMesOperationTask'),
    ).not.toHaveBeenCalled()
  })

  it('re-reads the exact strong-ID pair and rejects an action omitted by server allowedActions', async () => {
    vi.mocked(listBusinessConsoleMesOperationTasks).mockResolvedValue({
      data: {
        success: true,
        data: {
          items: [
            {
              operationTaskId: 'ot-blocked',
              workOrderId: 'wo-blocked',
              status: 'Queued',
              allowedActions: [],
              blockReasons: ['MATERIAL_SHORTAGE: 物料 MAT-1 缺口 2'],
              evaluatedAtUtc: '2026-08-02T08:30:00.000Z',
            },
          ],
          total: 1,
        },
      },
    } as never)
    const { startTask } = useMesOperationTasks()

    await expect(
      startTask('wo-blocked', 'ot-blocked', { idempotencyKey: 'op-blocked-1' }),
    ).rejects.toThrow('状态已被其他操作更新')

    expect(listBusinessConsoleMesOperationTasks).toHaveBeenCalledWith(
      expect.objectContaining({
        query: expect.objectContaining({
          workOrderId: 'wo-blocked',
          operationTaskId: 'ot-blocked',
          scopeKind: 'work-center',
          scopeId: 'WC-A',
        }),
      }),
    )
    expect(
      coladaState.mutateById.get('startBusinessConsoleMesOperationTask'),
    ).not.toHaveBeenCalled()
  })

  it('fails closed when a retained start intent still reads Queued without a server action', async () => {
    vi.mocked(listBusinessConsoleMesOperationTasks)
      .mockResolvedValueOnce({
        data: {
          success: true,
          data: {
            items: [
              {
                operationTaskId: 'ot-replay-blocked',
                workOrderId: 'wo-replay-blocked',
                status: 'Queued',
                allowedActions: ['start'],
              },
            ],
            total: 1,
          },
        },
      } as never)
      .mockResolvedValueOnce({
        data: {
          success: true,
          data: {
            items: [
              {
                operationTaskId: 'ot-replay-blocked',
                workOrderId: 'wo-replay-blocked',
                status: 'Queued',
                allowedActions: [],
              },
            ],
            total: 1,
          },
        },
      } as never)
    receiptState.confirm.mockRejectedValueOnce(new TypeError('response lost'))
    const { startTask } = useMesOperationTasks()
    const mutateAsync = coladaState.mutateById.get('startBusinessConsoleMesOperationTask')!
    const request = {
      reasonCode: 'OPERATOR_READY',
      idempotencyKey: 'op-replay-blocked-1',
    }

    await expect(startTask('wo-replay-blocked', 'ot-replay-blocked', request)).rejects.toThrow(
      'response lost',
    )
    await expect(startTask('wo-replay-blocked', 'ot-replay-blocked', request)).rejects.toThrow(
      '状态已被其他操作更新',
    )

    expect(mutateAsync).toHaveBeenCalledTimes(1)
  })

  it('allows the same retained start intent to replay when the authoritative status is its legal result', async () => {
    vi.mocked(listBusinessConsoleMesOperationTasks)
      .mockResolvedValueOnce({
        data: {
          success: true,
          data: {
            items: [
              {
                operationTaskId: 'ot-replay-applied',
                workOrderId: 'wo-replay-applied',
                status: 'Queued',
                allowedActions: ['start'],
              },
            ],
            total: 1,
          },
        },
      } as never)
      .mockResolvedValueOnce({
        data: {
          success: true,
          data: {
            items: [
              {
                operationTaskId: 'ot-replay-applied',
                workOrderId: 'wo-replay-applied',
                status: 'InProgress',
                allowedActions: [],
              },
            ],
            total: 1,
          },
        },
      } as never)
    receiptState.confirm.mockRejectedValueOnce(new TypeError('response lost'))
    const { startTask } = useMesOperationTasks()
    const mutateAsync = coladaState.mutateById.get('startBusinessConsoleMesOperationTask')!
    const request = {
      reasonCode: 'OPERATOR_READY',
      idempotencyKey: 'op-replay-applied-1',
    }

    await expect(startTask('wo-replay-applied', 'ot-replay-applied', request)).rejects.toThrow(
      'response lost',
    )
    await expect(
      startTask('wo-replay-applied', 'ot-replay-applied', request),
    ).resolves.toBeUndefined()

    expect(mutateAsync).toHaveBeenCalledTimes(2)
    expect(mutateAsync.mock.calls[1]?.[0].body.idempotencyKey).toBe('op-replay-applied-1')
  })

  it('fails closed before replay when the principal changes after an unconfirmed result', async () => {
    receiptState.confirm.mockRejectedValueOnce(new TypeError('response lost'))
    const mes = useMesOperationTasks() as ReturnType<typeof useMesOperationTasks> & {
      captureOperationActionContextIdentity: (
        action: 'start',
        workOrderId: string,
        operationTaskId: string,
      ) => string
    }
    const mutateAsync = coladaState.mutateById.get('startBusinessConsoleMesOperationTask')!
    const contextIdentity = mes.captureOperationActionContextIdentity('start', 'wo-1', 'ot-reentry')
    const request = { idempotencyKey: 'principal-drift-key', contextIdentity }

    await expect(mes.startTask('wo-1', 'ot-reentry', request)).rejects.toThrow('response lost')
    reactiveAuthState.principal = {
      principalId: 'user-002',
      organizationId: 'org-001',
      environmentId: 'env-dev',
    }
    await nextTick()

    await expect(mes.startTask('wo-1', 'ot-reentry', request)).rejects.toThrow(
      '账号、组织、环境或作业范围已变化',
    )
    expect(mutateAsync).toHaveBeenCalledTimes(1)
  })

  it('fails closed before replay when the manage scope changes after an unconfirmed result', async () => {
    receiptState.confirm.mockRejectedValueOnce(new TypeError('response lost'))
    const mes = useMesOperationTasks() as ReturnType<typeof useMesOperationTasks> & {
      captureOperationActionContextIdentity: (
        action: 'start',
        workOrderId: string,
        operationTaskId: string,
      ) => string
    }
    const mutateAsync = coladaState.mutateById.get('startBusinessConsoleMesOperationTask')!
    const contextIdentity = mes.captureOperationActionContextIdentity('start', 'wo-1', 'ot-reentry')
    const request = { idempotencyKey: 'scope-drift-key', contextIdentity }

    await expect(mes.startTask('wo-1', 'ot-reentry', request)).rejects.toThrow('response lost')
    coladaState.queryDataRefById.get(
      'getBusinessConsolePrincipalWorkContext:business.mes.operations.manage',
    )!.value = {
      success: true,
      data: { selectedScope: { kind: 'work-center', id: 'WC-B', displayName: '精加工二线' } },
    }
    await nextTick()

    await expect(mes.startTask('wo-1', 'ot-reentry', request)).rejects.toThrow(
      '账号、组织、环境或作业范围已变化',
    )
    expect(mutateAsync).toHaveBeenCalledTimes(1)
  })

  it('reports context drift before scope validation when the manage scope disappears', async () => {
    receiptState.confirm.mockRejectedValueOnce(new TypeError('response lost'))
    const mes = useMesOperationTasks()
    const mutateAsync = coladaState.mutateById.get('startBusinessConsoleMesOperationTask')!
    const contextIdentity = mes.captureOperationActionContextIdentity('start', 'wo-1', 'ot-reentry')
    const request = { idempotencyKey: 'scope-removed-key', contextIdentity }

    await expect(mes.startTask('wo-1', 'ot-reentry', request)).rejects.toThrow('response lost')
    coladaState.queryDataRefById.get(
      'getBusinessConsolePrincipalWorkContext:business.mes.operations.manage',
    )!.value = { success: true, data: { selectedScope: null } }
    await nextTick()

    await expect(mes.startTask('wo-1', 'ot-reentry', request)).rejects.toThrow(
      '账号、组织、环境或作业范围已变化',
    )
    expect(mutateAsync).toHaveBeenCalledTimes(1)
  })

  it('starts an operation task forwarding an optional reason code with the caller-supplied key', async () => {
    const { startTask } = useMesOperationTasks()

    await startTask('wo-1', 'ot-3', {
      reasonCode: 'OPERATOR_READY',
      idempotencyKey: 'op-start-1',
    })

    const mutateAsync = coladaState.mutateById.get('startBusinessConsoleMesOperationTask')
    expect(startBusinessConsoleMesOperationTaskMutationOptions).toHaveBeenCalled()
    const payload = mutateAsync!.mock.calls[0][0]
    expect(payload.path).toEqual({ operationTaskId: 'ot-3' })
    expect(payload.body).toMatchObject({ reasonCode: 'OPERATOR_READY' })
    expect(payload.body.idempotencyKey).toBe('op-start-1')
  })

  it('forwards the caller-supplied key when creating a material issue request', async () => {
    const { createIssue } = useMesMaterialIssue()

    await createIssue('wo-7', { materialId: 'mat-1', quantity: 5, idempotencyKey: 'op-issue-1' })

    expect(createBusinessConsoleMesMaterialIssueRequestMutationOptions).toHaveBeenCalled()
    const mutateAsync = coladaState.mutateById.get('createBusinessConsoleMesMaterialIssueRequest')
    const payload = mutateAsync!.mock.calls[0][0]
    expect(payload.path).toEqual({ workOrderId: 'wo-7' })
    expect(payload.query).toMatchObject({ organizationId: 'org-001', environmentId: 'env-dev' })
    expect(payload.body).toMatchObject({ materialId: 'mat-1', quantity: 5 })
    expect(payload.body.idempotencyKey).toBe('op-issue-1')
  })

  it('forwards the caller-supplied key when confirming a line-side material receipt', async () => {
    const { confirmLineSideReceipt } = useMesMaterialIssue()

    await confirmLineSideReceipt(
      'req-2',
      { receivedQuantity: 4, idempotencyKey: 'op-confirm-1' },
      { workOrderId: 'wo-2' },
    )

    expect(confirmBusinessConsoleMesLineSideMaterialReceiptMutationOptions).toHaveBeenCalled()
    expect(listBusinessConsoleMesMaterialIssueRequests).toHaveBeenCalledWith({
      query: expect.objectContaining({ workOrderId: 'wo-2', skip: 0, take: 100 }),
      throwOnError: true,
    })
    expect(
      vi.mocked(listBusinessConsoleMesMaterialIssueRequests).mock.calls[0]?.[0].query,
    ).not.toHaveProperty('keyword')
    const mutateAsync = coladaState.mutateById.get(
      'confirmBusinessConsoleMesLineSideMaterialReceipt',
    )
    const payload = mutateAsync!.mock.calls[0][0]
    expect(payload.path).toEqual({ requestId: 'req-2' })
    expect(payload.body).toMatchObject({ receivedQuantity: 4 })
    expect(payload.body.idempotencyKey).toBe('op-confirm-1')
  })

  it('forwards the caller-supplied key + injects business fields when creating a finished-goods receipt', async () => {
    const { createReceipt } = useMesReceipts()

    await createReceipt({
      workOrderId: 'wo-5',
      skuId: 'sku-1',
      quantity: 12,
      unitCost: 12.34,
      uomCode: 'EA',
      idempotencyKey: 'op-receipt-1',
    })

    expect(createBusinessConsoleMesFinishedGoodsReceiptRequestMutationOptions).toHaveBeenCalled()
    const mutateAsync = coladaState.mutateById.get(
      'createBusinessConsoleMesFinishedGoodsReceiptRequest',
    )
    const payload = mutateAsync!.mock.calls[0][0]
    expect(payload.body).toMatchObject({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      workOrderId: 'wo-5',
      skuId: 'sku-1',
      quantity: 12,
      unitCost: 12.34,
      uomCode: 'EA',
    })
    expect(payload.body.idempotencyKey).toBe('op-receipt-1')
    expect(payload.body.requestedAtUtc).toBeTruthy()
  })

  it('keeps injected scope/timestamp override-proof on recordReport (hostile org/env lose; caller key wins)', async () => {
    const { recordReport } = useMesProductionReports()

    await recordReport({
      workOrderId: 'wo-1',
      operationTaskId: 'ot-1',
      goodQuantity: 1,
      scrapQuantity: 0,
      completesOperation: false,
      idempotencyKey: 'op-report-stable',
      organizationId: 'EVIL',
      environmentId: 'EVIL',
      scopeKind: 'organization',
      scopeId: 'EVIL',
      reportedAtUtc: '1999-01-01T00:00:00.000Z',
    } as never)

    const mutateAsync = coladaState.mutateById.get('recordBusinessConsoleMesProductionReport')
    const payload = mutateAsync!.mock.calls[0][0]
    // org/env + timestamp injected LAST from principal scope — hostile caller values lose
    expect(payload.body.organizationId).toBe('org-001')
    expect(payload.body.environmentId).toBe('env-dev')
    expect(payload.body.scopeKind).toBe('self')
    expect(payload.body.scopeId).toBe('user-001')
    expect(payload.body.reportedAtUtc).not.toBe('1999-01-01T00:00:00.000Z')
    // the idempotency key is now the caller's responsibility — it passes through verbatim
    expect(payload.body.idempotencyKey).toBe('op-report-stable')
  })

  it('clears a report intent after a determinate 422 so the next attempt uses a new key', async () => {
    const { recordReport } = useMesProductionReports()
    const mutateAsync = coladaState.mutateById.get('recordBusinessConsoleMesProductionReport')!
    mutateAsync
      .mockRejectedValueOnce({ status: 422, message: 'invalid report' })
      .mockResolvedValueOnce({ success: true, data: {} })
    const intent = {
      workOrderId: 'wo-report-determinate',
      operationTaskId: 'ot-report-determinate',
      goodQuantity: 4,
      scrapQuantity: 0,
      completesOperation: false,
    } as const

    await expect(recordReport({ ...intent, idempotencyKey: 'report-key-1' })).rejects.toMatchObject(
      { status: 422 },
    )
    await recordReport({ ...intent, idempotencyKey: 'report-key-2' })

    expect(mutateAsync.mock.calls[0][0].body.idempotencyKey).toBe('report-key-1')
    expect(mutateAsync.mock.calls[1][0].body.idempotencyKey).toBe('report-key-2')
  })

  it('restores all required report fields when a pending intent has no payload snapshot', async () => {
    const input = {
      workOrderId: 'wo-report-missing-snapshot',
      operationTaskId: 'ot-report-missing-snapshot',
      goodQuantity: 7,
      scrapQuantity: 1,
      completesOperation: false,
      idempotencyKey: 'report-missing-snapshot-key',
    }
    const { idempotencyKey, ...payload } = input
    acquirePendingBusinessIntent(
      {
        principalId: 'unrestored-session',
        organizationId: 'org-001',
        environmentId: 'env-dev',
        operationType: 'mes.production-report.record',
        payloadFingerprint: JSON.stringify({
          ...payload,
          scopeKind: 'self',
          scopeId: 'user-001',
        }),
      },
      () => idempotencyKey,
    )
    const { recordReport } = useMesProductionReports()

    await recordReport(input)

    const mutateAsync = coladaState.mutateById.get('recordBusinessConsoleMesProductionReport')!
    expect(mutateAsync.mock.calls[0][0].body).toMatchObject({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      workOrderId: input.workOrderId,
      operationTaskId: input.operationTaskId,
      goodQuantity: input.goodQuantity,
      scrapQuantity: input.scrapQuantity,
      completesOperation: false,
      idempotencyKey,
      reportedAtUtc: expect.any(String),
      scopeKind: 'self',
      scopeId: 'user-001',
    })
  })

  it('replays only the same issued report-complete key after the task became completed', async () => {
    const { recordReport } = useMesProductionReports()
    const mutateAsync = coladaState.mutateById.get('recordBusinessConsoleMesProductionReport')!
    mutateAsync
      .mockRejectedValueOnce(new TypeError('response lost'))
      .mockResolvedValueOnce(undefined)
    const originalIntent = {
      workOrderId: 'wo-1',
      operationTaskId: 'ot-1',
      goodQuantity: 4,
      scrapQuantity: 0,
      completesOperation: true,
      idempotencyKey: 'report-complete-replay',
    }

    await expect(recordReport(originalIntent)).rejects.toThrow('response lost')
    vi.mocked(listBusinessConsoleMesReportableOperationTasks).mockResolvedValue({
      data: {
        success: true,
        data: {
          items: [{ operationTaskId: 'ot-1', workOrderId: 'wo-1', status: 'Completed' }],
          total: 1,
        },
      },
    } as never)

    await expect(recordReport(originalIntent)).resolves.toBeUndefined()
    await expect(
      recordReport({ ...originalIntent, idempotencyKey: 'report-complete-new-intent' }),
    ).rejects.toThrow('状态已被其他操作更新')

    expect(mutateAsync).toHaveBeenCalledTimes(2)
  })

  it('keeps injected scope/timestamp override-proof on createReceipt (hostile org/env lose; caller key wins)', async () => {
    const { createReceipt } = useMesReceipts()

    await createReceipt({
      workOrderId: 'wo-5',
      skuId: 'sku-1',
      quantity: 12,
      unitCost: 12.34,
      uomCode: 'EA',
      idempotencyKey: 'op-receipt-stable',
      organizationId: 'EVIL',
      environmentId: 'EVIL',
      requestedAtUtc: '1999-01-01T00:00:00.000Z',
    } as never)

    const mutateAsync = coladaState.mutateById.get(
      'createBusinessConsoleMesFinishedGoodsReceiptRequest',
    )
    const payload = mutateAsync!.mock.calls[0][0]
    expect(payload.body.organizationId).toBe('org-001')
    expect(payload.body.environmentId).toBe('env-dev')
    expect(payload.body.requestedAtUtc).not.toBe('1999-01-01T00:00:00.000Z')
    expect(payload.body.idempotencyKey).toBe('op-receipt-stable')
  })
})
