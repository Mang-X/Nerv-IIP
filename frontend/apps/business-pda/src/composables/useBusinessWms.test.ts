import { beforeEach, describe, expect, it, vi } from 'vitest'
import { nextTick, reactive, shallowRef, type ShallowRef } from 'vue'

import {
  confirmBusinessConsoleOperation,
  listBusinessConsoleWmsCountExecutionsQueryOptions,
  listBusinessConsoleWmsInboundOrdersQueryOptions,
  listBusinessConsoleWmsOutboundOrdersQueryOptions,
  listBusinessConsoleWmsPickingTasksQueryOptions,
  listBusinessConsoleWmsPutawayTasksQueryOptions,
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
}))

const authState = vi.hoisted(() => ({
  principal: undefined as { organizationId?: string; environmentId?: string } | undefined,
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
    reactiveAuthState.principal = { ...SCOPE }
    coladaState.listInbound.mockResolvedValue({
      data: {
        success: true,
        data: { items: [{ inboundOrderId: 'inbound-1', status: 'Open' }], total: 1 },
      },
    })
    coladaState.listOutbound.mockResolvedValue({
      data: {
        success: true,
        data: { items: [{ outboundOrderId: 'outbound-1', status: 'Open' }], total: 1 },
      },
    })
    coladaState.listCount.mockResolvedValue({
      data: {
        success: true,
        data: { items: [{ countExecutionId: 'count-1', status: 'Open' }], total: 1 },
      },
    })
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
      body: { idempotencyKey: string }
    }
    expect(vars.path).toEqual({ inboundOrderId: 'inbound-1' })
    expect(vars.query).toEqual(SCOPE)
    // 页面提供的稳定键原样透传，封装不再生成。
    expect(vars.body.idempotencyKey).toBe('KEY-1')
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
      body: { packReviewNo: string; passed?: boolean; idempotencyKey: string }
    }
    expect(vars.path).toEqual({ outboundOrderId: 'outbound-1' })
    expect(vars.body.packReviewNo).toBe('PR')
    expect(vars.body.passed).toBe(true)
    // 页面提供的稳定键原样透传。
    expect(vars.body.idempotencyKey).toBe('KEY-OUT')
    // org/env 取自登录主体，敌意值永远不进 query。
    expect(vars.query).toEqual(SCOPE)
  })

  it.each([
    {
      name: 'inbound',
      operationType: 'wms.inbound-order.complete',
      resourceId: 'inbound-1',
      payloadFingerprint: 'inbound-1:[]',
      payloadSnapshot: { lines: [] },
      mutationId: 'completeInbound',
      expectedBody: { idempotencyKey: 'KEY-OLD' },
      arrangeTerminal: () =>
        coladaState.listInbound.mockResolvedValue({
          data: {
            success: true,
            data: { items: [{ inboundOrderId: 'inbound-1', status: 'Completed' }], total: 1 },
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
      payloadSnapshot: { packReviewNo: 'PR', passed: true },
      mutationId: 'completeOutbound',
      expectedBody: { packReviewNo: 'PR', passed: true, idempotencyKey: 'KEY-OLD' },
      arrangeTerminal: () =>
        coladaState.listOutbound.mockResolvedValue({
          data: {
            success: true,
            data: { items: [{ outboundOrderId: 'outbound-1', status: 'Completed' }], total: 1 },
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
      payloadSnapshot: { countedQuantity: 5 },
      mutationId: 'completeCount',
      expectedBody: { countedQuantity: 5, idempotencyKey: 'KEY-OLD' },
      arrangeTerminal: () =>
        coladaState.listCount.mockResolvedValue({
          data: {
            success: true,
            data: { items: [{ countExecutionId: 'count-1', status: 'Completed' }], total: 1 },
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
        principalId: 'unrestored-session',
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

  it('allows only a persisted same-key inbound retry after completion', async () => {
    coladaState.listInbound.mockResolvedValue({
      data: {
        success: true,
        data: { items: [{ inboundOrderId: 'inbound-1', status: 'Completed' }], total: 1 },
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
        principalId: 'unrestored-session',
        ...SCOPE,
        operationType: 'wms.inbound-order.complete',
        payloadFingerprint: 'inbound-1:[]',
      },
      () => 'KEY-1',
      { lines: [] },
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
          items: [{ outboundOrderId: 'outbound-1', status: 'InventoryPostingPending' }],
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
        principalId: 'unrestored-session',
        ...SCOPE,
        operationType: 'wms.outbound-order.complete',
        payloadFingerprint: 'outbound-1:{"packReviewNo":"PR","passed":true}',
      },
      () => 'KEY-OUT',
      { packReviewNo: 'PR', passed: true },
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
    expect(vars.query).toEqual(SCOPE)
  })

  it('re-reads the exact count execution and does not mutate after it became completed', async () => {
    coladaState.listCount.mockResolvedValue({
      data: {
        success: true,
        data: { items: [{ countExecutionId: 'count-1', status: 'Completed' }], total: 1 },
      },
    })
    const { completeCount } = useWmsCount()

    await expect(
      completeCount('count-1', { countedQuantity: 5, idempotencyKey: 'KEY-CNT' }),
    ).rejects.toThrow('状态已被其他操作更新')

    expect(coladaState.listCount).toHaveBeenCalledWith({
      query: { ...SCOPE, countExecutionId: 'count-1', skip: 0, take: 2 },
      throwOnError: true,
    })
    expect(coladaState.lastMutationVars.has('completeCount')).toBe(false)
  })

  it('allows a completed count retry with the frozen key and returns the authoritative receipt', async () => {
    const receipt = { countExecutionId: 'count-1', status: 'Completed', countedQuantity: 5 }
    coladaState.listCount.mockResolvedValue({
      data: {
        success: true,
        data: { items: [{ countExecutionId: 'count-1', status: 'Completed' }], total: 1 },
      },
    })
    coladaState.mutationResultById.set('completeCount', receipt)
    const { completeCount } = useWmsCount()
    acquirePendingBusinessIntent(
      {
        principalId: 'unrestored-session',
        ...SCOPE,
        operationType: 'wms.count-execution.complete',
        payloadFingerprint: 'count-1:{"countedQuantity":5}',
      },
      () => 'KEY-CNT-FROZEN',
      { countedQuantity: 5 },
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
      body: { countedQuantity: 5, idempotencyKey: 'KEY-CNT-FROZEN' },
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
        data: { items: [{ countExecutionId: 'count-1', status: 'Completed' }], total: 1 },
      },
    })
    await expect(completeCount('count-1', input, { attempt: 'retry' })).rejects.toThrow(
      '状态已被其他操作更新',
    )

    coladaState.listCount.mockResolvedValue({
      data: {
        success: true,
        data: { items: [{ countExecutionId: 'count-2', status: 'Open' }], total: 1 },
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
        data: { items: [{ countExecutionId: 'count-2', status: 'Completed' }], total: 1 },
      },
    })
    await expect(
      completeCount('count-2', unconfirmedInput, { attempt: 'retry' }),
    ).resolves.toBeUndefined()
  })

  it('enables picking/putaway read-only lists without a non-empty operatorUserId', () => {
    useWmsPicking()
    useWmsPutaway()

    expect(coladaState.queryOptionsById.get('listBusinessConsoleWmsPickingTasks')?.enabled).toBe(
      true,
    )
    expect(coladaState.queryOptionsById.get('listBusinessConsoleWmsPutawayTasks')?.enabled).toBe(
      true,
    )

    for (const fn of [
      listBusinessConsoleWmsPickingTasksQueryOptions,
      listBusinessConsoleWmsPutawayTasksQueryOptions,
    ]) {
      const call = vi.mocked(fn).mock.calls.at(-1)?.[0] as { query: Record<string, unknown> }
      expect(call.query).toEqual(
        expect.objectContaining({ organizationId: 'org-001', environmentId: 'env-dev' }),
      )
      // operatorUserId P1 未实装：传非空会返回空集，所以不能出现非空值。
      expect(call.query.operatorUserId ?? '').toBe('')
    }
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
      coladaState.queryDataById.set(id, {
        success: true,
        data: { items: [{ id: `old-${id}` }], total: 7 },
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
    const result = useWmsReceivingLines(orderNo)

    expect(result.lines.value).toHaveLength(1)
    expect(result.hasSuccessfulResponse.value).toBe(true)

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
