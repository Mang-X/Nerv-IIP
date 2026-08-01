import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, nextTick, reactive, shallowRef, type ShallowRef } from 'vue'

import {
  confirmBusinessConsoleOperation,
  listBusinessConsoleWmsPickingTasks,
  listBusinessConsoleWmsCountExecutionsQueryOptions,
  listBusinessConsoleWmsInboundOrdersQueryOptions,
  listBusinessConsoleWmsOutboundOrdersQueryOptions,
  listBusinessConsoleWmsPickingTasksQueryOptions,
  listBusinessConsoleWmsPutawayTasksQueryOptions,
  listBusinessConsoleWmsReceivingQualityGatesQueryOptions,
  startBusinessConsoleWmsPickingTask,
} from '@nerv-iip/api-client'
import {
  acquirePendingBusinessIntent,
  clearPendingBusinessIntent,
  peekPendingBusinessIntent,
} from '@nerv-iip/business-core'
import {
  useWmsCount,
  useWmsInbound,
  useWmsOutbound,
  useWmsPicking,
  useWmsPutaway,
  useWmsReceivingLines,
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

vi.mock('./useWmsWorkScope', () => ({
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
    create: () => useWmsInbound(),
    makeItem: (index: number) => ({ inboundOrderId: `inbound-${index}`, status: 'Open' }),
  },
  {
    name: 'outbound',
    id: 'listBusinessConsoleWmsOutboundOrders',
    list: coladaState.listOutbound,
    create: () => useWmsOutbound(),
    makeItem: (index: number) => ({ outboundOrderId: `outbound-${index}`, status: 'Open' }),
  },
  {
    name: 'picking',
    id: 'listBusinessConsoleWmsPickingTasks',
    list: coladaState.listPicking,
    create: () => useWmsPicking(),
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
    create: () => useWmsPutaway(),
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
    create: () => useWmsCount(),
    makeItem: (index: number) => ({ countExecutionId: `count-${index}`, status: 'Open' }),
  },
] as const

describe('PDA WMS composables', () => {
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

  it('disables every list query when the principal has no org/env scope', () => {
    reactiveAuthState.principal = undefined

    useWmsInbound()
    useWmsOutbound()
    useWmsPicking()
    useWmsPutaway()
    useWmsCount()

    for (const id of [
      'listBusinessConsoleWmsInboundOrders',
      'listBusinessConsoleWmsOutboundOrders',
      'listBusinessConsoleWmsPickingTasks',
      'listBusinessConsoleWmsPutawayTasks',
      'listBusinessConsoleWmsCountExecutions',
    ]) {
      expect(coladaState.queryOptionsById.get(id)?.enabled).toBe(false)
    }
  })

  it('does not manually refresh any list when the principal has no org/env scope', async () => {
    reactiveAuthState.principal = undefined

    const results = [
      useWmsInbound(),
      useWmsOutbound(),
      useWmsPicking(),
      useWmsPutaway(),
      useWmsCount(),
    ]
    await Promise.all(results.map((result) => result.refresh()))

    for (const id of [
      'listBusinessConsoleWmsInboundOrders',
      'listBusinessConsoleWmsOutboundOrders',
      'listBusinessConsoleWmsPickingTasks',
      'listBusinessConsoleWmsPutawayTasks',
      'listBusinessConsoleWmsCountExecutions',
    ]) {
      expect(coladaState.refetchById.get(id)).not.toHaveBeenCalled()
    }
  })

  it('enables lists and threads the principal scope into inbound query', () => {
    coladaState.queryDataById.set('listBusinessConsoleWmsInboundOrders', {
      success: true,
      data: { items: [{ inboundOrderId: 'in-1' }], total: 3 },
    })
    const result = useWmsInbound()

    expect(listBusinessConsoleWmsInboundOrdersQueryOptions).toHaveBeenCalledWith({
      query: expect.objectContaining({
        organizationId: 'org-001',
        environmentId: 'env-dev',
        skip: 0,
      }),
    })
    expect(coladaState.queryOptionsById.get('listBusinessConsoleWmsInboundOrders')?.enabled).toBe(
      true,
    )
    expect(result.orders.value).toHaveLength(1)
    expect(result.organizationId.value).toBe('org-001')
    expect(result.environmentId.value).toBe('env-dev')
    expect(result.scopeReady.value).toBe(true)
    expect(result.total.value).toBe(3)
  })

  it('uses the page-supplied idempotency key for the inbound complete body and targets the path', async () => {
    const { completeInbound } = useWmsInbound()
    await completeInbound('inbound-1', 'KEY-1')

    const vars = coladaState.lastMutationVars.get('completeInbound') as {
      path: { inboundOrderId: string }
      query: { organizationId: string; environmentId: string }
      body: {
        idempotencyKey: string
        scopeKind: string
        scopeId: string
        expectedVersion: number
      }
    }
    expect(vars.path).toEqual({ inboundOrderId: 'inbound-1' })
    expect(vars.query).toEqual(SCOPE)
    // 页面提供的稳定键原样透传，封装不再生成。
    expect(vars.body.idempotencyKey).toBe('KEY-1')
    expect(vars.body).toMatchObject({
      scopeKind: 'self',
      scopeId: 'emp049',
      expectedVersion: 5,
    })
  })

  it('passes the supplied idempotencyKey through outbound and keeps org/env override-proof', async () => {
    const { completeOutbound } = useWmsOutbound()
    await completeOutbound('outbound-1', {
      packReviewNo: 'PR',
      passed: true,
      idempotencyKey: 'KEY-OUT',
      // 调用方试图注入敌意 org/env——必须永远落空（query 恒取登录主体）。
      organizationId: 'evil-org',
      environmentId: 'evil-env',
    } as never)

    const vars = coladaState.lastMutationVars.get('completeOutbound') as {
      path: { outboundOrderId: string }
      query: { organizationId: string; environmentId: string }
      body: {
        packReviewNo: string
        passed?: boolean
        idempotencyKey: string
        scopeKind: string
        scopeId: string
        expectedVersion: number
      }
    }
    expect(vars.path).toEqual({ outboundOrderId: 'outbound-1' })
    expect(vars.body.packReviewNo).toBe('PR')
    expect(vars.body.passed).toBe(true)
    // 页面提供的稳定键原样透传。
    expect(vars.body.idempotencyKey).toBe('KEY-OUT')
    expect(vars.body).toMatchObject({
      scopeKind: 'self',
      scopeId: 'emp049',
      expectedVersion: 6,
    })
    // org/env 取自登录主体，敌意值永远不进 query。
    expect(vars.query).toEqual(SCOPE)
  })

  it.each([
    {
      name: 'inbound',
      operationType: 'wms.inbound-order.complete',
      resourceId: 'inbound-1',
      payloadFingerprint: 'inbound-1:[]',
      payloadSnapshot: {
        lines: [],
        scopeKind: 'self',
        scopeId: 'emp049',
        expectedVersion: 5,
      },
      mutationId: 'completeInbound',
      expectedBody: {
        idempotencyKey: 'KEY-OLD',
        scopeKind: 'self',
        scopeId: 'emp049',
        expectedVersion: 5,
      },
      arrangeTerminal: () =>
        coladaState.listInbound.mockResolvedValue({
          data: {
            success: true,
            data: {
              items: [{ inboundOrderId: 'inbound-1', status: 'Completed', version: 6 }],
              total: 1,
            },
          },
        }),
      execute: () =>
        useWmsInbound().completeInbound('inbound-1', 'KEY-NEW', undefined, { attempt: 'retry' }),
    },
    {
      name: 'outbound',
      operationType: 'wms.outbound-order.complete',
      resourceId: 'outbound-1',
      payloadFingerprint: 'outbound-1:{"packReviewNo":"PR","passed":true}',
      payloadSnapshot: {
        packReviewNo: 'PR',
        passed: true,
        scopeKind: 'self',
        scopeId: 'emp049',
        expectedVersion: 6,
      },
      mutationId: 'completeOutbound',
      expectedBody: {
        packReviewNo: 'PR',
        passed: true,
        idempotencyKey: 'KEY-OLD',
        scopeKind: 'self',
        scopeId: 'emp049',
        expectedVersion: 6,
      },
      arrangeTerminal: () =>
        coladaState.listOutbound.mockResolvedValue({
          data: {
            success: true,
            data: {
              items: [{ outboundOrderId: 'outbound-1', status: 'Completed', version: 7 }],
              total: 1,
            },
          },
        }),
      execute: () =>
        useWmsOutbound().completeOutbound(
          'outbound-1',
          { packReviewNo: 'PR', passed: true, idempotencyKey: 'KEY-NEW' },
          { attempt: 'retry' },
        ),
    },
    {
      name: 'count',
      operationType: 'wms.count-execution.complete',
      resourceId: 'count-1',
      payloadFingerprint: 'count-1:{"countedQuantity":5}',
      payloadSnapshot: {
        countedQuantity: 5,
        scopeKind: 'self',
        scopeId: 'emp049',
        expectedVersion: 7,
      },
      mutationId: 'completeCount',
      expectedBody: {
        countedQuantity: 5,
        idempotencyKey: 'KEY-OLD',
        scopeKind: 'self',
        scopeId: 'emp049',
        expectedVersion: 7,
      },
      arrangeTerminal: () =>
        coladaState.listCount.mockResolvedValue({
          data: {
            success: true,
            data: {
              items: [{ countExecutionId: 'count-1', status: 'Completed', version: 8 }],
              total: 1,
            },
          },
        }),
      execute: () =>
        useWmsCount().completeCount(
          'count-1',
          { countedQuantity: 5, idempotencyKey: 'KEY-NEW' },
          { attempt: 'retry' },
        ),
    },
  ])(
    'replays the restored OLD key after a refresh supplies a NEW key for $name',
    async ({
      operationType,
      resourceId,
      payloadFingerprint,
      payloadSnapshot,
      mutationId,
      expectedBody,
      arrangeTerminal,
      execute,
    }) => {
      arrangeTerminal()
      const intentScope = {
        principalId: 'emp049',
        ...SCOPE,
        operationType,
        payloadFingerprint,
      }
      acquirePendingBusinessIntent(intentScope, () => 'KEY-OLD', payloadSnapshot)
      const receipt = { operationType, resourceId }
      const unconfirmed = Object.assign(new Error('unconfirmed'), {
        code: 'business-operation-unconfirmed',
      })
      coladaState.mutationResultById.set(mutationId, receipt)
      coladaState.confirmationFailure = unconfirmed

      try {
        await expect(execute()).rejects.toBe(unconfirmed)
        expect(coladaState.lastMutationVars.get(mutationId)).toMatchObject({
          body: expectedBody,
        })
        expect(confirmBusinessConsoleOperation).toHaveBeenCalledWith(receipt, {
          expectedOperationType: operationType,
          expectedIdempotencyKey: 'KEY-OLD',
          expectedResourceId: resourceId,
        })
        expect(peekPendingBusinessIntent(intentScope)?.idempotencyKey).toBe('KEY-OLD')
      } finally {
        clearPendingBusinessIntent(intentScope)
      }
    },
  )

  it.each([
    {
      name: 'inbound',
      list: coladaState.listInbound,
      mutationId: 'completeInbound',
      openItem: { inboundOrderId: 'inbound-1', status: 'Open', version: 5 },
      terminalItem: () => ({
        inboundOrderId: 'inbound-1',
        status: 'Completed',
        get version(): never {
          throw new Error('restored replay read the current inbound version')
        },
      }),
      create: () => useWmsInbound(),
      execute: (
        result: ReturnType<typeof useWmsInbound>,
        key: string,
        attempt: 'initial' | 'retry',
      ) => result.completeInbound('inbound-1', key, [{ lineNo: '1', lotNo: 'LOT-A' }], { attempt }),
      expectedBody: {
        lines: [{ lineNo: '1', lotNo: 'LOT-A' }],
        scopeKind: 'self',
        scopeId: 'emp049',
        expectedVersion: 5,
        idempotencyKey: 'KEY-SCOPE-A',
      },
    },
    {
      name: 'outbound',
      list: coladaState.listOutbound,
      mutationId: 'completeOutbound',
      openItem: { outboundOrderId: 'outbound-1', status: 'Open', version: 6 },
      terminalItem: () => ({
        outboundOrderId: 'outbound-1',
        status: 'InventoryPostingPending',
        get version(): never {
          throw new Error('restored replay read the current outbound version')
        },
      }),
      create: () => useWmsOutbound(),
      execute: (
        result: ReturnType<typeof useWmsOutbound>,
        key: string,
        attempt: 'initial' | 'retry',
      ) =>
        result.completeOutbound(
          'outbound-1',
          { packReviewNo: 'PR-A', passed: true, idempotencyKey: key },
          { attempt },
        ),
      expectedBody: {
        packReviewNo: 'PR-A',
        passed: true,
        scopeKind: 'self',
        scopeId: 'emp049',
        expectedVersion: 6,
        idempotencyKey: 'KEY-SCOPE-A',
      },
    },
    {
      name: 'count',
      list: coladaState.listCount,
      mutationId: 'completeCount',
      openItem: { countExecutionId: 'count-1', status: 'Open', version: 7 },
      terminalItem: () => ({
        countExecutionId: 'count-1',
        status: 'Completed',
        get version(): never {
          throw new Error('restored replay read the current count version')
        },
      }),
      create: () => useWmsCount(),
      execute: (
        result: ReturnType<typeof useWmsCount>,
        key: string,
        attempt: 'initial' | 'retry',
      ) =>
        result.completeCount('count-1', { countedQuantity: 5, idempotencyKey: key }, { attempt }),
      expectedBody: {
        countedQuantity: 5,
        scopeKind: 'self',
        scopeId: 'emp049',
        expectedVersion: 7,
        idempotencyKey: 'KEY-SCOPE-A',
      },
    },
  ])(
    'restores $name completion entirely from scope A after the user switches to scope B',
    async ({ list, mutationId, openItem, terminalItem, create, execute, expectedBody }) => {
      list.mockResolvedValueOnce({
        data: { success: true, data: { items: [openItem], total: 1 } },
      })
      coladaState.mutationResultById.set(mutationId, { operationType: mutationId })
      const unconfirmed = Object.assign(new Error('first response was not confirmed'), {
        code: 'business-operation-unconfirmed',
      })
      coladaState.confirmationFailure = unconfirmed
      const result = create()

      await expect(execute(result as never, 'KEY-SCOPE-A', 'initial')).rejects.toBe(unconfirmed)

      result.scopeKey.value = 'work-pool:WMS-SITE-001'
      await nextTick()
      list.mockResolvedValueOnce({
        data: { success: true, data: { items: [terminalItem()], total: 1 } },
      })
      coladaState.confirmationFailure = undefined

      await expect(execute(result as never, 'KEY-SCOPE-B', 'retry')).resolves.toBeDefined()

      expect(list.mock.calls.at(-1)?.[0]).toMatchObject({
        query: {
          ...SCOPE,
          scopeKind: 'self',
          scopeId: 'emp049',
          skip: 0,
          take: 2,
        },
        throwOnError: true,
      })
      expect(coladaState.lastMutationVars.get(mutationId)).toMatchObject({
        query: SCOPE,
        body: expectedBody,
      })
    },
  )

  it('allows only a persisted same-key inbound retry after completion', async () => {
    coladaState.listInbound.mockResolvedValue({
      data: {
        success: true,
        data: {
          items: [{ inboundOrderId: 'inbound-1', status: 'Completed', version: 6 }],
          total: 1,
        },
      },
    })
    const { completeInbound } = useWmsInbound()

    await expect(
      completeInbound('inbound-1', 'KEY-1', undefined, { attempt: 'initial' }),
    ).rejects.toThrow('状态已被其他操作更新')
    expect(coladaState.lastMutationVars.has('completeInbound')).toBe(false)

    await expect(
      completeInbound('inbound-1', 'KEY-1', undefined, { attempt: 'retry' }),
    ).rejects.toThrow('状态已被其他操作更新')
    expect(coladaState.lastMutationVars.has('completeInbound')).toBe(false)

    acquirePendingBusinessIntent(
      {
        principalId: 'emp049',
        ...SCOPE,
        operationType: 'wms.inbound-order.complete',
        payloadFingerprint: 'inbound-1:[]',
      },
      () => 'KEY-1',
      { lines: [], scopeKind: 'self', scopeId: 'emp049', expectedVersion: 5 },
    )
    await expect(
      completeInbound('inbound-1', 'KEY-1', undefined, { attempt: 'retry' }),
    ).resolves.toBeUndefined()
    expect(coladaState.lastMutationVars.has('completeInbound')).toBe(true)
  })

  it('allows only a persisted same-key outbound retry while inventory posting is pending', async () => {
    coladaState.listOutbound.mockResolvedValue({
      data: {
        success: true,
        data: {
          items: [{ outboundOrderId: 'outbound-1', status: 'InventoryPostingPending', version: 7 }],
          total: 1,
        },
      },
    })
    const { completeOutbound } = useWmsOutbound()
    const input = { packReviewNo: 'PR', passed: true, idempotencyKey: 'KEY-OUT' }

    await expect(completeOutbound('outbound-1', input, { attempt: 'initial' })).rejects.toThrow(
      '状态已被其他操作更新',
    )
    expect(coladaState.lastMutationVars.has('completeOutbound')).toBe(false)

    await expect(completeOutbound('outbound-1', input, { attempt: 'retry' })).rejects.toThrow(
      '状态已被其他操作更新',
    )
    expect(coladaState.lastMutationVars.has('completeOutbound')).toBe(false)

    acquirePendingBusinessIntent(
      {
        principalId: 'emp049',
        ...SCOPE,
        operationType: 'wms.outbound-order.complete',
        payloadFingerprint: 'outbound-1:{"packReviewNo":"PR","passed":true}',
      },
      () => 'KEY-OUT',
      {
        packReviewNo: 'PR',
        passed: true,
        scopeKind: 'self',
        scopeId: 'emp049',
        expectedVersion: 6,
      },
    )
    await expect(
      completeOutbound('outbound-1', input, { attempt: 'retry' }),
    ).resolves.toBeUndefined()
    expect(coladaState.lastMutationVars.has('completeOutbound')).toBe(true)
  })

  it('passes the supplied idempotencyKey through count and keeps org/env override-proof', async () => {
    const { completeCount } = useWmsCount()
    await completeCount('count-1', {
      countedQuantity: 5,
      idempotencyKey: 'KEY-CNT',
      // 调用方试图注入敌意 org/env——必须永远落空。
      organizationId: 'evil-org',
      environmentId: 'evil-env',
    } as never)

    const vars = coladaState.lastMutationVars.get('completeCount') as {
      path: { countExecutionId: string }
      query: { organizationId: string; environmentId: string }
      body: { countedQuantity?: number; idempotencyKey: string }
    }
    expect(vars.path).toEqual({ countExecutionId: 'count-1' })
    expect(vars.body.countedQuantity).toBe(5)
    expect(vars.body.idempotencyKey).toBe('KEY-CNT')
    expect(vars.body).toMatchObject({
      scopeKind: 'self',
      scopeId: 'emp049',
      expectedVersion: 7,
    })
    expect(vars.query).toEqual(SCOPE)
  })

  it('re-reads the exact count execution and does not mutate after it became completed', async () => {
    coladaState.listCount.mockResolvedValue({
      data: {
        success: true,
        data: {
          items: [{ countExecutionId: 'count-1', status: 'Completed', version: 8 }],
          total: 1,
        },
      },
    })
    const { completeCount } = useWmsCount()

    await expect(
      completeCount('count-1', { countedQuantity: 5, idempotencyKey: 'KEY-CNT' }),
    ).rejects.toThrow('状态已被其他操作更新')

    expect(coladaState.listCount).toHaveBeenCalledWith({
      query: {
        ...SCOPE,
        scopeKind: 'self',
        scopeId: 'emp049',
        countExecutionId: 'count-1',
        skip: 0,
        take: 2,
      },
      throwOnError: true,
    })
    expect(coladaState.lastMutationVars.has('completeCount')).toBe(false)
  })

  it('allows a completed count retry with the frozen key and returns the authoritative receipt', async () => {
    const receipt = { countExecutionId: 'count-1', status: 'Completed', countedQuantity: 5 }
    coladaState.listCount.mockResolvedValue({
      data: {
        success: true,
        data: {
          items: [{ countExecutionId: 'count-1', status: 'Completed', version: 8 }],
          total: 1,
        },
      },
    })
    coladaState.mutationResultById.set('completeCount', receipt)
    const { completeCount } = useWmsCount()
    acquirePendingBusinessIntent(
      {
        principalId: 'emp049',
        ...SCOPE,
        operationType: 'wms.count-execution.complete',
        payloadFingerprint: 'count-1:{"countedQuantity":5}',
      },
      () => 'KEY-CNT-FROZEN',
      {
        countedQuantity: 5,
        scopeKind: 'self',
        scopeId: 'emp049',
        expectedVersion: 7,
      },
    )

    await expect(
      completeCount(
        'count-1',
        { countedQuantity: 5, idempotencyKey: 'KEY-CNT-FROZEN' },
        { attempt: 'retry' },
      ),
    ).resolves.toBe(receipt)

    expect(coladaState.lastMutationVars.get('completeCount')).toMatchObject({
      path: { countExecutionId: 'count-1' },
      body: {
        countedQuantity: 5,
        idempotencyKey: 'KEY-CNT-FROZEN',
        scopeKind: 'self',
        scopeId: 'emp049',
        expectedVersion: 7,
      },
    })
  })

  it('clears a determinate count failure but retains an unconfirmed count for exact replay', async () => {
    const input = { countedQuantity: 5, idempotencyKey: 'KEY-CNT-SETTLEMENT' }
    const { completeCount } = useWmsCount()

    coladaState.mutationFailureById.set('completeCount', { statusCode: 422 })
    await expect(completeCount('count-1', input)).rejects.toEqual({ statusCode: 422 })
    coladaState.mutationFailureById.delete('completeCount')
    coladaState.listCount.mockResolvedValue({
      data: {
        success: true,
        data: {
          items: [{ countExecutionId: 'count-1', status: 'Completed', version: 8 }],
          total: 1,
        },
      },
    })
    await expect(completeCount('count-1', input, { attempt: 'retry' })).rejects.toThrow(
      '状态已被其他操作更新',
    )

    coladaState.listCount.mockResolvedValue({
      data: {
        success: true,
        data: { items: [{ countExecutionId: 'count-2', status: 'Open', version: 2 }], total: 1 },
      },
    })
    coladaState.mutationFailureById.set(
      'completeCount',
      Object.assign(new Error('unconfirmed'), { code: 'business-operation-unconfirmed' }),
    )
    const unconfirmedInput = { countedQuantity: 6, idempotencyKey: 'KEY-CNT-UNCONFIRMED' }
    await expect(completeCount('count-2', unconfirmedInput)).rejects.toThrow('unconfirmed')
    coladaState.mutationFailureById.delete('completeCount')
    coladaState.listCount.mockResolvedValue({
      data: {
        success: true,
        data: {
          items: [{ countExecutionId: 'count-2', status: 'Completed', version: 3 }],
          total: 1,
        },
      },
    })
    await expect(
      completeCount('count-2', unconfirmedInput, { attempt: 'retry' }),
    ).resolves.toBeUndefined()
  })

  it('threads the selected trusted WMS scope into all five list queries', () => {
    useWmsInbound()
    useWmsOutbound()
    useWmsPicking()
    useWmsPutaway()
    useWmsCount()

    for (const fn of [
      listBusinessConsoleWmsInboundOrdersQueryOptions,
      listBusinessConsoleWmsOutboundOrdersQueryOptions,
      listBusinessConsoleWmsPickingTasksQueryOptions,
      listBusinessConsoleWmsPutawayTasksQueryOptions,
      listBusinessConsoleWmsCountExecutionsQueryOptions,
    ]) {
      const call = vi.mocked(fn).mock.calls.at(-1)?.[0] as { query: Record<string, unknown> }
      expect(call.query).toEqual(
        expect.objectContaining({
          organizationId: 'org-001',
          environmentId: 'env-dev',
          scopeKind: 'self',
          scopeId: 'emp049',
        }),
      )
    }
  })

  it('unbinds task rows immediately when the selected WMS scope changes', async () => {
    coladaState.queryDataById.set('listBusinessConsoleWmsPickingTasks', {
      success: true,
      data: {
        items: [
          {
            warehouseTaskId: 'pick-1',
            taskNo: 'PICK-001',
            allowedActions: ['start'],
            blockReasons: null,
          },
        ],
        total: 1,
      },
    })
    const result = useWmsPicking()

    expect(result.tasks.value).toHaveLength(1)
    result.scopeKey.value = 'work-pool:WMS-SITE-001'
    await nextTick()

    expect(result.tasks.value).toEqual([])
    expect(result.total.value).toBe(0)
    expect(result.lastUpdatedAt.value).toBeNull()
  })

  it('normalizes task rows and refreshes the authorized query from page zero', async () => {
    coladaState.queryDataById.set('listBusinessConsoleWmsPickingTasks', {
      success: true,
      data: {
        items: [
          {
            warehouseTaskId: 'pick-1',
            taskNo: 'PICK-001',
            allowedActions: null,
            blockReasons: null,
          },
          { taskNo: '缺少可信标识' },
        ],
        total: 45,
      },
    })
    const result = useWmsPicking()

    expect(result.filters.take).toBe(20)
    expect(result.tasks.value).toEqual([
      expect.objectContaining({
        warehouseTaskId: 'pick-1',
        allowedActions: [],
        blockReasons: [],
      }),
    ])

    await result.refresh()
    expect(coladaState.refetchById.get('listBusinessConsoleWmsPickingTasks')).toHaveBeenCalledTimes(
      1,
    )
  })

  it.each([
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
  ])(
    'loads all 520 $name rows with fixed take and advancing skip, then stops at no-more',
    async ({ id, list, create, makeItem }) => {
      const total = 520
      const page = (skip: number) =>
        Array.from({ length: Math.min(20, total - skip) }, (_, offset) => makeItem(skip + offset))
      coladaState.queryDataById.set(id, {
        success: true,
        data: { items: page(0), total },
      })
      list.mockImplementation(async ({ query }: { query: { skip?: number } }) => ({
        data: {
          success: true,
          data: { items: page(query.skip ?? 0), total },
        },
      }))
      const result = create()

      expect(result.rows.value).toHaveLength(20)
      const first = result.loadMore()
      const duplicate = result.loadMore()
      await Promise.all([first, duplicate])
      expect(list).toHaveBeenCalledTimes(1)

      for (let pageIndex = 2; pageIndex < 26; pageIndex += 1) {
        await result.loadMore()
      }

      expect(list).toHaveBeenCalledTimes(25)
      expect(list.mock.calls.map(([options]) => options.query.skip)).toEqual(
        Array.from({ length: 25 }, (_, index) => (index + 1) * 20),
      )
      expect(list.mock.calls.every(([options]) => options.query.take === 20)).toBe(true)
      expect(result.rows.value).toHaveLength(520)
      expect(new Set(result.rows.value.map((item) => JSON.stringify(item))).size).toBe(520)

      await result.loadMore()
      expect(list).toHaveBeenCalledTimes(25)
    },
  )

  it.each(pagingErrorCases)(
    'does not expose a rejected stale $name page after scope and filter reset',
    async ({ id, list, create, makeItem }) => {
      coladaState.queryDataById.set(id, {
        success: true,
        data: {
          items: Array.from({ length: 20 }, (_, index) => makeItem(index)),
          total: 40,
        },
      })
      let rejectPage!: (reason?: unknown) => void
      list.mockReturnValueOnce(
        new Promise((_resolve, reject) => {
          rejectPage = reject
        }),
      )
      const result = create()
      const staleFailure = new Error(`${id}: stale loadMore failure`)

      const load = result.loadMore()
      result.scopeKey.value = 'work-pool:WMS-SITE-001'
      result.filters.keyword = 'NEW-FILTER'
      await nextTick()
      expect(result.error.value).toBeUndefined()

      rejectPage(staleFailure)

      await expect(load).rejects.toBe(staleFailure)
      expect(result.error.value).toBeUndefined()
    },
  )

  it.each(pagingErrorCases)(
    'records a rejected current-scope $name page in the appropriate error channel',
    async ({ id, list, create, makeItem }) => {
      coladaState.queryDataById.set(id, {
        success: true,
        data: {
          items: Array.from({ length: 20 }, (_, index) => makeItem(index)),
          total: 40,
        },
      })
      const currentFailure = new Error(`${id}: current loadMore failure`)
      list.mockRejectedValueOnce(currentFailure)
      const result = create()

      await expect(result.loadMore()).rejects.toBe(currentFailure)
      expect(result.loadMoreError.value).toBe(currentFailure)
      expect(result.error.value).toBeUndefined()
    },
  )

  it.each(pagingErrorCases.filter(({ name }) => name === 'picking' || name === 'putaway'))(
    'clears the split $name load-more error after a successful retry',
    async ({ id, list, create, makeItem }) => {
      coladaState.queryDataById.set(id, {
        success: true,
        data: {
          items: Array.from({ length: 20 }, (_, index) => makeItem(index)),
          total: 40,
        },
      })
      const currentFailure = new Error(`${id}: current loadMore failure`)
      list.mockRejectedValueOnce(currentFailure).mockResolvedValueOnce({
        data: {
          success: true,
          data: {
            items: Array.from({ length: 20 }, (_, index) => makeItem(index + 20)),
            total: 40,
          },
        },
      })
      const result = create()

      await expect(result.loadMore()).rejects.toBe(currentFailure)
      expect(result.loadMoreError.value).toBe(currentFailure)

      await result.loadMore()

      expect(result.loadMoreError.value).toBeUndefined()
    },
  )

  it('deduplicates a repeated picking page boundary without requesting the terminal page twice', async () => {
    coladaState.queryDataById.set('listBusinessConsoleWmsPickingTasks', {
      success: true,
      data: {
        items: Array.from({ length: 20 }, (_, index) => ({
          warehouseTaskId: `pick-${index}`,
          allowedActions: [],
        })),
        total: 39,
      },
    })
    coladaState.listPicking.mockResolvedValue({
      data: {
        success: true,
        data: {
          items: Array.from({ length: 20 }, (_, index) => ({
            warehouseTaskId: `pick-${index + 19}`,
            allowedActions: [],
          })),
          total: 39,
        },
      },
    })
    const result = useWmsPicking()

    await result.loadMore()
    await result.loadMore()

    expect(result.tasks.value).toHaveLength(39)
    expect(new Set(result.tasks.value.map((task) => task.warehouseTaskId)).size).toBe(39)
    expect(coladaState.listPicking).toHaveBeenCalledTimes(1)
  })

  it('discards an in-flight picking page after the keyword changes', async () => {
    coladaState.queryDataById.set('listBusinessConsoleWmsPickingTasks', {
      success: true,
      data: {
        items: Array.from({ length: 20 }, (_, index) => ({
          warehouseTaskId: `pick-old-${index}`,
          allowedActions: [],
        })),
        total: 40,
      },
    })
    let resolvePage!: (value: unknown) => void
    coladaState.listPicking.mockReturnValue(
      new Promise((resolve) => {
        resolvePage = resolve
      }),
    )
    const result = useWmsPicking()

    const load = result.loadMore()
    result.filters.keyword = 'PICK-NEW'
    await nextTick()
    expect(result.tasks.value).toEqual([])

    resolvePage({
      data: {
        success: true,
        data: {
          items: Array.from({ length: 20 }, (_, index) => ({
            warehouseTaskId: `pick-old-${index + 20}`,
            allowedActions: [],
          })),
          total: 40,
        },
      },
    })
    await load

    expect(result.tasks.value).toEqual([])
  })

  it('clears accumulated rows before refresh and every task scope/filter dimension change', async () => {
    const initialEnvelope = () => ({
      success: true,
      data: {
        items: [{ warehouseTaskId: 'pick-old', allowedActions: [] }],
        total: 31,
      },
    })
    coladaState.queryDataById.set('listBusinessConsoleWmsPickingTasks', initialEnvelope())
    const result = useWmsPicking()

    const refreshPromise = result.refresh()
    expect(result.tasks.value).toEqual([])
    expect(result.filters.skip).toBe(0)
    expect(result.filters.take).toBe(20)
    await refreshPromise

    for (const change of [
      () => (result.scopeKey.value = 'work-pool:WMS-SITE-001'),
      () => (result.filters.status = 'Completed'),
      () => (result.filters.keyword = 'PICK-NEW'),
      () => (result.filters.locationCode = 'A-02'),
      () => (result.filters.lotNo = 'LOT-02'),
    ]) {
      coladaState.queryDataRefById.get('listBusinessConsoleWmsPickingTasks')!.value =
        initialEnvelope()
      await nextTick()
      expect(result.tasks.value).toHaveLength(1)

      change()
      await nextTick()

      expect(result.tasks.value).toEqual([])
      expect(result.filters.skip).toBe(0)
      expect(result.filters.take).toBe(20)
    }
  })

  it('re-reads the exact picking task and starts it with trusted scope, version and stable key', async () => {
    coladaState.queryDataById.set('listBusinessConsoleWmsPickingTasks', {
      success: true,
      data: {
        items: [
          {
            warehouseTaskId: 'pick-1',
            taskNo: 'PICK-001',
            status: 'Open',
            version: 2,
            allowedActions: ['start'],
          },
        ],
        total: 1,
      },
    })
    const result = useWmsPicking()

    await result.executeTask({ action: 'start', task: result.tasks.value[0]! })

    expect(listBusinessConsoleWmsPickingTasks).toHaveBeenCalledWith({
      query: {
        ...SCOPE,
        scopeKind: 'self',
        scopeId: 'emp049',
        keyword: 'PICK-001',
        skip: 0,
        take: 20,
      },
      throwOnError: true,
    })
    expect(startBusinessConsoleWmsPickingTask).toHaveBeenCalledWith({
      path: { warehouseTaskId: 'pick-1' },
      query: {
        ...SCOPE,
        scopeKind: 'self',
        scopeId: 'emp049',
      },
      body: { idempotencyKey: 'TASK-KEY', expectedVersion: 2 },
      throwOnError: true,
    })
  })

  it('blocks a new task action when the authoritative row became terminal or stale', async () => {
    coladaState.queryDataById.set('listBusinessConsoleWmsPickingTasks', {
      success: true,
      data: {
        items: [
          {
            warehouseTaskId: 'pick-1',
            taskNo: 'PICK-001',
            status: 'Open',
            version: 2,
            allowedActions: ['start'],
          },
        ],
        total: 1,
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
              status: 'Completed',
              version: 3,
              allowedActions: [],
            },
          ],
          total: 1,
        },
      },
    })
    const result = useWmsPicking()

    await expect(
      result.executeTask({ action: 'start', task: result.tasks.value[0]! }),
    ).rejects.toThrow('状态已被其他操作更新')
    expect(startBusinessConsoleWmsPickingTask).not.toHaveBeenCalled()
  })

  it('replays an unconfirmed task action with the frozen key, version, and trusted scope', async () => {
    coladaState.queryDataById.set('listBusinessConsoleWmsPickingTasks', {
      success: true,
      data: {
        items: [
          {
            warehouseTaskId: 'pick-1',
            taskNo: 'PICK-001',
            status: 'Open',
            version: 2,
            allowedActions: ['start'],
          },
        ],
        total: 1,
      },
    })
    const unconfirmed = Object.assign(new Error('unconfirmed'), {
      code: 'business-operation-unconfirmed',
    })
    coladaState.startPicking.mockRejectedValueOnce(unconfirmed)
    const result = useWmsPicking()
    const intent = { action: 'start' as const, task: result.tasks.value[0]! }

    await expect(result.executeTask(intent)).rejects.toBe(unconfirmed)
    const frozenPayload = {
      expectedVersion: 2,
      scopeKind: 'self',
      scopeId: 'emp049',
    }
    const selfIntentScope = {
      principalId: 'emp049',
      ...SCOPE,
      operationType: 'wms.picking-task.start',
      payloadFingerprint: `pick-1:${JSON.stringify(frozenPayload)}`,
    }
    expect(peekPendingBusinessIntent(selfIntentScope)).toMatchObject({
      idempotencyKey: 'TASK-KEY',
      payloadSnapshot: frozenPayload,
    })

    result.scopeKey.value = 'work-pool:WMS-SITE-001'
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
              allowedActions: ['start'],
            },
          ],
          total: 1,
        },
      },
    })

    await expect(result.executeTask(intent)).resolves.toEqual(
      expect.objectContaining({ warehouseTaskId: 'pick-1' }),
    )
    expect(startBusinessConsoleWmsPickingTask).toHaveBeenCalledTimes(2)
    expect(vi.mocked(startBusinessConsoleWmsPickingTask).mock.calls[0]?.[0].body).toEqual(
      vi.mocked(startBusinessConsoleWmsPickingTask).mock.calls[1]?.[0].body,
    )
    expect(coladaState.listPicking).toHaveBeenCalledTimes(2)
    expect(coladaState.listPicking.mock.calls.map(([options]) => options.query)).toEqual([
      {
        ...SCOPE,
        scopeKind: 'self',
        scopeId: 'emp049',
        keyword: 'PICK-001',
        skip: 0,
        take: 20,
      },
      {
        ...SCOPE,
        scopeKind: 'self',
        scopeId: 'emp049',
        keyword: 'PICK-001',
        skip: 0,
        take: 20,
      },
    ])
    expect(
      vi.mocked(startBusinessConsoleWmsPickingTask).mock.calls.map(([options]) => options.query),
    ).toEqual([
      { ...SCOPE, scopeKind: 'self', scopeId: 'emp049' },
      { ...SCOPE, scopeKind: 'self', scopeId: 'emp049' },
    ])
  })

  it('authoritative refresh confirms an unconfirmed start and clears the stale action error', async () => {
    coladaState.queryDataById.set('listBusinessConsoleWmsPickingTasks', {
      success: true,
      data: {
        items: [
          {
            warehouseTaskId: 'pick-1',
            taskNo: 'PICK-001',
            status: 'Open',
            version: 2,
            allowedActions: ['start'],
          },
        ],
        total: 1,
      },
    })
    const unconfirmed = Object.assign(new Error('unconfirmed'), {
      code: 'business-operation-unconfirmed',
    })
    coladaState.startPicking.mockRejectedValueOnce(unconfirmed)
    const result = useWmsPicking()
    const intent = { action: 'start' as const, task: result.tasks.value[0]! }

    await expect(result.executeTask(intent)).rejects.toBe(unconfirmed)
    expect(result.actionUnconfirmed.value).toBe(true)
    expect(result.actionError.value).toBe(unconfirmed)
    expect(result.error.value).toBeUndefined()

    coladaState.listPicking.mockResolvedValue({
      data: {
        success: true,
        data: {
          items: [
            {
              warehouseTaskId: 'pick-1',
              taskNo: 'PICK-001',
              status: 'InProgress',
              version: 3,
              executedQuantity: 0,
              allowedActions: ['progress', 'exception', 'complete'],
            },
          ],
          total: 1,
        },
      },
    })

    await expect(result.refresh()).resolves.toMatchObject({ confirmedAction: 'start' })
    expect(result.actionUnconfirmed.value).toBe(false)
    expect(result.actionError.value).toBeUndefined()
    expect(result.error.value).toBeUndefined()
    expect(result.actionConfirmedSequence.value).toBe(1)
    expect(startBusinessConsoleWmsPickingTask).toHaveBeenCalledTimes(1)
  })

  it('clears refreshing when authoritative verification of an unconfirmed action fails', async () => {
    coladaState.queryDataById.set('listBusinessConsoleWmsPickingTasks', {
      success: true,
      data: {
        items: [
          {
            warehouseTaskId: 'pick-1',
            taskNo: 'PICK-001',
            status: 'Open',
            version: 2,
            allowedActions: ['start'],
          },
        ],
        total: 1,
      },
    })
    const unconfirmed = Object.assign(new Error('unconfirmed'), {
      code: 'business-operation-unconfirmed',
    })
    coladaState.startPicking.mockRejectedValueOnce(unconfirmed)
    const result = useWmsPicking()

    await expect(
      result.executeTask({ action: 'start', task: result.tasks.value[0]! }),
    ).rejects.toBe(unconfirmed)
    const verificationFailure = new Error('authoritative verification failed')
    coladaState.listPicking.mockRejectedValueOnce(verificationFailure)
    const listRefetch = coladaState.refetchById.get('listBusinessConsoleWmsPickingTasks')!

    const refreshPromise = result.refresh()
    expect(result.refreshing.value).toBe(true)
    await expect(refreshPromise).rejects.toBe(verificationFailure)
    expect(result.actionError.value).toBe(verificationFailure)
    expect(result.error.value).toBeUndefined()
    expect(result.loadMoreError.value).toBeUndefined()
    expect(listRefetch).not.toHaveBeenCalled()
    expect(result.refreshing.value).toBe(false)
  })

  it('keeps a confirmed start successful when only the follow-up list refresh fails', async () => {
    coladaState.queryDataById.set('listBusinessConsoleWmsPickingTasks', {
      success: true,
      data: {
        items: [
          {
            warehouseTaskId: 'pick-1',
            taskNo: 'PICK-001',
            status: 'Open',
            version: 2,
            allowedActions: ['start'],
          },
        ],
        total: 1,
      },
    })
    const result = useWmsPicking()
    const refreshFailure = new Error('list refresh failed')
    coladaState.refetchById
      .get('listBusinessConsoleWmsPickingTasks')!
      .mockRejectedValueOnce(refreshFailure)

    await expect(
      result.executeTask({ action: 'start', task: result.tasks.value[0]! }),
    ).resolves.toMatchObject({ warehouseTaskId: 'pick-1', status: 'InProgress' })
    expect(result.actionUnconfirmed.value).toBe(false)
    expect(result.actionConfirmedSequence.value).toBe(1)
    expect(result.error.value).toBe(refreshFailure)
  })

  it('exposes success:false and malformed raw responses as failures for every WMS list', async () => {
    const cases = [
      {
        id: 'listBusinessConsoleWmsInboundOrders',
        create: () => {
          const result = useWmsInbound()
          return { rows: result.orders, ...result }
        },
      },
      {
        id: 'listBusinessConsoleWmsOutboundOrders',
        create: () => {
          const result = useWmsOutbound()
          return { rows: result.orders, ...result }
        },
      },
      {
        id: 'listBusinessConsoleWmsPickingTasks',
        create: () => {
          const result = useWmsPicking()
          return { rows: result.tasks, ...result }
        },
      },
      {
        id: 'listBusinessConsoleWmsPutawayTasks',
        create: () => {
          const result = useWmsPutaway()
          return { rows: result.tasks, ...result }
        },
      },
      {
        id: 'listBusinessConsoleWmsCountExecutions',
        create: () => {
          const result = useWmsCount()
          return { rows: result.executions, ...result }
        },
      },
    ]

    for (const candidate of cases) {
      coladaState.queryDataById.set(candidate.id, { success: false, message: '查询失败' })
      const result = candidate.create()

      expect(result.rows.value, candidate.id).toEqual([])
      expect(result.total.value, candidate.id).toBe(0)
      expect(result.hasSuccessfulResponse.value, candidate.id).toBe(false)
      expect(result.hasFailedResponse.value, candidate.id).toBe(true)

      coladaState.queryDataRefById.get(candidate.id)!.value = { data: { items: [], total: 0 } }
      await nextTick()
      expect(result.hasSuccessfulResponse.value, `${candidate.id}:malformed`).toBe(false)
      expect(result.hasFailedResponse.value, `${candidate.id}:malformed`).toBe(true)
    }
  })

  it('unbinds all WMS list projections immediately when the principal scope changes', async () => {
    const ids = [
      'listBusinessConsoleWmsInboundOrders',
      'listBusinessConsoleWmsOutboundOrders',
      'listBusinessConsoleWmsPickingTasks',
      'listBusinessConsoleWmsPutawayTasks',
      'listBusinessConsoleWmsCountExecutions',
    ]
    for (const id of ids) {
      const item =
        id === 'listBusinessConsoleWmsInboundOrders'
          ? { inboundOrderId: 'old-inbound' }
          : id === 'listBusinessConsoleWmsOutboundOrders'
            ? { outboundOrderId: 'old-outbound' }
            : id === 'listBusinessConsoleWmsCountExecutions'
              ? { countExecutionId: 'old-count' }
              : { warehouseTaskId: `old-${id}` }
      coladaState.queryDataById.set(id, {
        success: true,
        data: { items: [item], total: 7 },
      })
    }

    const results = [
      (() => {
        const result = useWmsInbound()
        return { rows: result.orders, ...result }
      })(),
      (() => {
        const result = useWmsOutbound()
        return { rows: result.orders, ...result }
      })(),
      (() => {
        const result = useWmsPicking()
        return { rows: result.tasks, ...result }
      })(),
      (() => {
        const result = useWmsPutaway()
        return { rows: result.tasks, ...result }
      })(),
      (() => {
        const result = useWmsCount()
        return { rows: result.executions, ...result }
      })(),
    ]
    for (const result of results) {
      expect(result.rows.value).toHaveLength(1)
      expect(result.total.value).toBe(7)
      expect(result.hasSuccessfulResponse.value).toBe(true)
      expect(result.lastUpdatedAt.value).not.toBeNull()
    }

    reactiveAuthState.principal = { organizationId: 'org-002', environmentId: 'env-prod' }
    await nextTick()

    for (const result of results) {
      expect(result.rows.value).toEqual([])
      expect(result.total.value).toBe(0)
      expect(result.hasSuccessfulResponse.value).toBe(false)
      expect(result.hasFailedResponse.value).toBe(false)
      expect(result.lastUpdatedAt.value).toBeNull()
    }
  })

  it('binds receiving detail lines to both principal scope and selected inbound order', async () => {
    coladaState.queryDataById.set('listBusinessConsoleWmsReceivingQualityGates', {
      success: true,
      data: {
        items: [{ inboundOrderLineId: 'line-old', inboundOrderNo: 'IN-1' }],
        total: 1,
      },
    })
    const orderNo = shallowRef('IN-1')
    const scopeKind = shallowRef<string | undefined>('self')
    const scopeId = shallowRef<string | undefined>('emp049')
    const result = useWmsReceivingLines(orderNo, { scopeKind, scopeId })

    expect(result.lines.value).toHaveLength(1)
    expect(result.hasSuccessfulResponse.value).toBe(true)
    expect(
      vi.mocked(listBusinessConsoleWmsReceivingQualityGatesQueryOptions).mock.calls.at(-1)?.[0],
    ).toEqual({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        scopeKind: 'self',
        scopeId: 'emp049',
        skip: 0,
        take: 500,
        inboundOrderNo: 'IN-1',
        includeNotRequired: true,
      },
    })

    scopeId.value = 'POOL-RECEIVING'
    await nextTick()
    expect(result.lines.value).toEqual([])
    expect(result.total.value).toBe(0)
    expect(result.hasSuccessfulResponse.value).toBe(false)

    orderNo.value = 'IN-2'
    await nextTick()
    expect(result.lines.value).toEqual([])
    expect(result.total.value).toBe(0)
    expect(result.hasSuccessfulResponse.value).toBe(false)
    expect(result.hasFailedResponse.value).toBe(false)

    coladaState.queryDataRefById.get('listBusinessConsoleWmsReceivingQualityGates')!.value = {
      success: false,
      message: '收货明细查询失败',
    }
    await nextTick()
    expect(result.hasSuccessfulResponse.value).toBe(false)
    expect(result.hasFailedResponse.value).toBe(true)
  })
})
