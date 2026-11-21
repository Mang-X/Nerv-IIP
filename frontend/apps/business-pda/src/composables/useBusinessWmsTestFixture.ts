import { beforeEach, vi } from 'vitest'
import { computed, reactive, shallowRef, type ShallowRef } from 'vue'
import {
  useWmsCount,
  useWmsInbound,
  useWmsOutbound,
  useWmsPicking,
  useWmsPutaway,
} from './useBusinessWms'

const coladaState = vi.hoisted(() => ({
  queryDataById: new Map<string, unknown>(),
  queryDataRefById: new Map<string, ShallowRef<unknown>>(),
  queryOptionsById: new Map<string, { enabled?: boolean }>(),
  refetchById: new Map<string, ReturnType<typeof vi.fn>>(),
  lastMutationVars: new Map<string, unknown>(),
  mutationResultById: new Map<string, unknown>(),
  mutationFailureById: new Map<string, unknown>(),
  confirmationFailure: undefined as unknown,
  listInbound: vi.fn(),
  listOutbound: vi.fn(),
  listCount: vi.fn(),
  listPicking: vi.fn(),
  listPutaway: vi.fn(),
  startPicking: vi.fn(),
  progressPicking: vi.fn(),
  exceptionPicking: vi.fn(),
  completePicking: vi.fn(),
  startPutaway: vi.fn(),
  progressPutaway: vi.fn(),
  exceptionPutaway: vi.fn(),
  completePutaway: vi.fn(),
}))

const authState = vi.hoisted(() => ({
  principal: undefined as
    | { principalId?: string; organizationId?: string; environmentId?: string }
    | undefined,
}))

const reactiveAuthState = reactive(authState)

// 真实 Pinia store 会解包 principal ref；mock 直接返回解包后的值以贴合运行时。
vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({
    get principal() {
      return reactiveAuthState.principal
    },
  }),
}))

vi.mock('./makeIdempotencyKey', () => ({
  makeIdempotencyKey: vi.fn(() => 'TASK-KEY'),
}))

vi.mock('./useWmsWorkScope', async (importOriginal) => ({
  ...(await importOriginal<typeof import('./useWmsWorkScope')>()),
  useWmsWorkScope: vi.fn(() => {
    const scopeKey = shallowRef<string | undefined>('self:emp049')
    const parsedScope = computed(() => {
      const [kind, id] = scopeKey.value?.split(':') ?? []
      return { kind, id }
    })
    const hasTenant = computed(
      () =>
        Boolean(reactiveAuthState.principal?.organizationId) &&
        Boolean(reactiveAuthState.principal?.environmentId),
    )
    return {
      organizationId: computed(() => reactiveAuthState.principal?.organizationId ?? ''),
      environmentId: computed(() => reactiveAuthState.principal?.environmentId ?? ''),
      principalId: computed(() => reactiveAuthState.principal?.principalId ?? 'emp049'),
      sessionKey: computed(() =>
        JSON.stringify([
          reactiveAuthState.principal?.principalId,
          reactiveAuthState.principal?.organizationId,
          reactiveAuthState.principal?.environmentId,
        ]),
      ),
      hasTenant,
      hasSelection: computed(
        () =>
          hasTenant.value &&
          ['self', 'work-pool', 'site'].includes(parsedScope.value.kind ?? '') &&
          Boolean(parsedScope.value.id),
      ),
      scopeKey,
      scopeKind: computed(() => parsedScope.value.kind),
      scopeId: computed(() => parsedScope.value.id),
      scopeOptions: computed(() => [
        { label: '我的任务', value: 'self:emp049' },
        { label: '一号仓作业池', value: 'work-pool:WMS-SITE-001' },
        { label: '一号仓库', value: 'site:SITE-001' },
      ]),
      selectedScopeLabel: computed(() => '我的任务'),
      pending: shallowRef(false),
      error: shallowRef(),
      refresh: vi.fn(),
    }
  }),
}))

vi.mock('@nerv-iip/api-client', () => ({
  confirmBusinessConsoleOperation: vi.fn(async (value) => {
    if (coladaState.confirmationFailure !== undefined) {
      throw coladaState.confirmationFailure
    }
    return value
  }),
  listBusinessConsoleWmsInboundOrders: coladaState.listInbound,
  listBusinessConsoleWmsOutboundOrders: coladaState.listOutbound,
  listBusinessConsoleWmsCountExecutions: coladaState.listCount,
  listBusinessConsoleWmsPickingTasks: coladaState.listPicking,
  listBusinessConsoleWmsPutawayTasks: coladaState.listPutaway,
  startBusinessConsoleWmsPickingTask: coladaState.startPicking,
  recordBusinessConsoleWmsPickingTaskProgress: coladaState.progressPicking,
  reportBusinessConsoleWmsPickingTaskException: coladaState.exceptionPicking,
  completeBusinessConsoleWmsPickingTask: coladaState.completePicking,
  startBusinessConsoleWmsPutawayTask: coladaState.startPutaway,
  recordBusinessConsoleWmsPutawayTaskProgress: coladaState.progressPutaway,
  reportBusinessConsoleWmsPutawayTaskException: coladaState.exceptionPutaway,
  completeBusinessConsoleWmsPutawayTask: coladaState.completePutaway,
  listBusinessConsoleWmsInboundOrdersQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleWmsInboundOrders' }],
    query: vi.fn(),
  })),
  listBusinessConsoleWmsOutboundOrdersQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleWmsOutboundOrders' }],
    query: vi.fn(),
  })),
  listBusinessConsoleWmsPickingTasksQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleWmsPickingTasks' }],
    query: vi.fn(),
  })),
  listBusinessConsoleWmsPutawayTasksQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleWmsPutawayTasks' }],
    query: vi.fn(),
  })),
  listBusinessConsoleWmsCountExecutionsQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleWmsCountExecutions' }],
    query: vi.fn(),
  })),
  listBusinessConsoleWmsReceivingQualityGatesQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleWmsReceivingQualityGates' }],
    query: vi.fn(),
  })),
  completeBusinessConsoleWmsInboundOrderMutationOptions: vi.fn(() => ({
    _mutationId: 'completeInbound',
  })),
  completeBusinessConsoleWmsOutboundOrderMutationOptions: vi.fn(() => ({
    _mutationId: 'completeOutbound',
  })),
  completeBusinessConsoleWmsCountExecutionMutationOptions: vi.fn(() => ({
    _mutationId: 'completeCount',
  })),
}))

vi.mock('@pinia/colada', () => ({
  useQuery: vi.fn((optionsFactory) => {
    const options = optionsFactory()
    const key = Array.isArray(options.key) ? options.key[0] : undefined
    const id = key && typeof key === 'object' && '_id' in key ? String(key._id) : ''
    coladaState.queryOptionsById.set(id, options)

    const refetch = vi.fn()
    coladaState.refetchById.set(id, refetch)
    const data = shallowRef(coladaState.queryDataById.get(id))
    coladaState.queryDataRefById.set(id, data)
    return {
      data,
      error: shallowRef(),
      isLoading: shallowRef(false),
      refetch,
    }
  }),
  useMutation: vi.fn((mutationOptions: { _mutationId?: string }) => ({
    mutateAsync: vi.fn((vars: unknown) => {
      const mutationId = mutationOptions._mutationId ?? ''
      coladaState.lastMutationVars.set(mutationId, vars)
      if (coladaState.mutationFailureById.has(mutationId)) {
        return Promise.reject(coladaState.mutationFailureById.get(mutationId))
      }
      return Promise.resolve(coladaState.mutationResultById.get(mutationId))
    }),
    isLoading: shallowRef(false),
    error: shallowRef(),
  })),
}))

const SCOPE = { organizationId: 'org-001', environmentId: 'env-dev' }

const pagingErrorCases = [
  {
    name: 'inbound',
    id: 'listBusinessConsoleWmsInboundOrders',
    list: coladaState.listInbound,
    create: () => {
      const result = useWmsInbound()
      return { rows: result.orders, ...result }
    },
    makeItem: (index: number) => ({ inboundOrderId: `inbound-${index}`, status: 'Open' }),
  },
  {
    name: 'outbound',
    id: 'listBusinessConsoleWmsOutboundOrders',
    list: coladaState.listOutbound,
    create: () => {
      const result = useWmsOutbound()
      return { rows: result.orders, ...result }
    },
    makeItem: (index: number) => ({ outboundOrderId: `outbound-${index}`, status: 'Open' }),
  },
  {
    name: 'picking',
    id: 'listBusinessConsoleWmsPickingTasks',
    list: coladaState.listPicking,
    create: () => {
      const result = useWmsPicking()
      return { rows: result.tasks, ...result }
    },
    makeItem: (index: number) => ({
      warehouseTaskId: `picking-${index}`,
      status: 'Open',
      allowedActions: [],
    }),
  },
  {
    name: 'putaway',
    id: 'listBusinessConsoleWmsPutawayTasks',
    list: coladaState.listPutaway,
    create: () => {
      const result = useWmsPutaway()
      return { rows: result.tasks, ...result }
    },
    makeItem: (index: number) => ({
      warehouseTaskId: `putaway-${index}`,
      status: 'Open',
      allowedActions: [],
    }),
  },
  {
    name: 'count',
    id: 'listBusinessConsoleWmsCountExecutions',
    list: coladaState.listCount,
    create: () => {
      const result = useWmsCount()
      return { rows: result.executions, ...result }
    },
    makeItem: (index: number) => ({ countExecutionId: `count-${index}`, status: 'Open' }),
  },
] as const

const pagingIdentityResetCases = pagingErrorCases.flatMap((testCase) => [
  { ...testCase, resetKind: 'filter' as const },
  { ...testCase, resetKind: 'scope' as const },
])

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise
    reject = rejectPromise
  })
  return { promise, resolve, reject }
}
export function setupBusinessWmsTests() {
  beforeEach(() => {
    vi.clearAllMocks()
    sessionStorage.clear()
    coladaState.queryDataById.clear()
    coladaState.queryDataRefById.clear()
    coladaState.queryOptionsById.clear()
    coladaState.refetchById.clear()
    coladaState.lastMutationVars.clear()
    coladaState.mutationResultById.clear()
    coladaState.mutationFailureById.clear()
    coladaState.confirmationFailure = undefined
    reactiveAuthState.principal = { principalId: 'emp049', ...SCOPE }
    coladaState.listInbound.mockResolvedValue({
      data: {
        success: true,
        data: { items: [{ inboundOrderId: 'inbound-1', status: 'Open', version: 5 }], total: 1 },
      },
    })
    coladaState.listOutbound.mockResolvedValue({
      data: {
        success: true,
        data: { items: [{ outboundOrderId: 'outbound-1', status: 'Open', version: 6 }], total: 1 },
      },
    })
    coladaState.listCount.mockResolvedValue({
      data: {
        success: true,
        data: { items: [{ countExecutionId: 'count-1', status: 'Open', version: 7 }], total: 1 },
      },
    })
    coladaState.listPicking.mockResolvedValue({
      data: {
        success: true,
        data: {
          items: [
            {
              warehouseTaskId: 'pick-1',
              taskNo: 'PICK-001',
              status: 'Open',
              version: 2,
              allowedActions: ['start', 'exception'],
            },
          ],
          total: 1,
        },
      },
    })
    coladaState.listPutaway.mockResolvedValue({
      data: {
        success: true,
        data: {
          items: [
            {
              warehouseTaskId: 'putaway-1',
              taskNo: 'PUT-001',
              status: 'Open',
              version: 3,
              allowedActions: ['start', 'exception'],
            },
          ],
          total: 1,
        },
      },
    })
    for (const action of [
      coladaState.startPicking,
      coladaState.progressPicking,
      coladaState.exceptionPicking,
      coladaState.completePicking,
      coladaState.startPutaway,
      coladaState.progressPutaway,
      coladaState.exceptionPutaway,
      coladaState.completePutaway,
    ]) {
      action.mockResolvedValue({
        data: {
          success: true,
          data: { warehouseTaskId: 'pick-1', status: 'InProgress', version: 3 },
        },
      })
    }
  })
}
export {
  coladaState,
  reactiveAuthState,
  SCOPE,
  pagingErrorCases,
  pagingIdentityResetCases,
  deferred,
}
