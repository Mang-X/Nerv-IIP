import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'

import {
  listBusinessConsoleWmsCountExecutionsQueryOptions,
  listBusinessConsoleWmsInboundOrdersQueryOptions,
  listBusinessConsoleWmsOutboundOrdersQueryOptions,
  listBusinessConsoleWmsPickingTasksQueryOptions,
  listBusinessConsoleWmsPutawayTasksQueryOptions,
} from '@nerv-iip/api-client'
import {
  useWmsCount,
  useWmsInbound,
  useWmsOutbound,
  useWmsPicking,
  useWmsPutaway,
} from './useBusinessWms'

const coladaState = vi.hoisted(() => ({
  queryDataById: new Map<string, unknown>(),
  queryOptionsById: new Map<string, { enabled?: boolean }>(),
  lastMutationVars: new Map<string, unknown>(),
}))

const authState = vi.hoisted(() => ({
  principal: undefined as { organizationId?: string; environmentId?: string } | undefined,
}))

// 真实 Pinia store 会解包 principal ref；mock 直接返回解包后的值以贴合运行时。
vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({
    get principal() {
      return authState.principal
    },
  }),
}))

vi.mock('@nerv-iip/api-client', () => ({
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
  completeBusinessConsoleWmsInboundOrderMutationOptions: vi.fn(() => ({ _mutationId: 'completeInbound' })),
  completeBusinessConsoleWmsOutboundOrderMutationOptions: vi.fn(() => ({ _mutationId: 'completeOutbound' })),
  completeBusinessConsoleWmsCountExecutionMutationOptions: vi.fn(() => ({ _mutationId: 'completeCount' })),
}))

vi.mock('@pinia/colada', () => ({
  useQuery: vi.fn((optionsFactory) => {
    const options = optionsFactory()
    const key = Array.isArray(options.key) ? options.key[0] : undefined
    const id = key && typeof key === 'object' && '_id' in key ? String(key._id) : ''
    coladaState.queryOptionsById.set(id, options)

    return {
      data: shallowRef(coladaState.queryDataById.get(id)),
      error: shallowRef(),
      isLoading: shallowRef(false),
      refetch: vi.fn(),
    }
  }),
  useMutation: vi.fn((mutationOptions: { _mutationId?: string }) => ({
    mutateAsync: vi.fn((vars: unknown) => {
      coladaState.lastMutationVars.set(mutationOptions._mutationId ?? '', vars)
      return Promise.resolve(undefined)
    }),
    isLoading: shallowRef(false),
    error: shallowRef(),
  })),
}))

const SCOPE = { organizationId: 'org-001', environmentId: 'env-dev' }

describe('PDA WMS composables', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    coladaState.queryDataById.clear()
    coladaState.queryOptionsById.clear()
    coladaState.lastMutationVars.clear()
    authState.principal = { ...SCOPE }
  })

  it('disables every list query when the principal has no org/env scope', () => {
    authState.principal = undefined

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

  it('enables lists and threads the principal scope into inbound query', () => {
    const result = useWmsInbound()

    expect(listBusinessConsoleWmsInboundOrdersQueryOptions).toHaveBeenCalledWith({
      query: expect.objectContaining({
        organizationId: 'org-001',
        environmentId: 'env-dev',
        skip: 0,
      }),
    })
    expect(coladaState.queryOptionsById.get('listBusinessConsoleWmsInboundOrders')?.enabled).toBe(true)
    expect(result.orders.value).toEqual([])
  })

  it('injects an idempotency key into the inbound complete body and targets the path', async () => {
    const { completeInbound } = useWmsInbound()
    await completeInbound('inbound-1')

    const vars = coladaState.lastMutationVars.get('completeInbound') as {
      path: { inboundOrderId: string }
      query: { organizationId: string; environmentId: string }
      body: { idempotencyKey: string }
    }
    expect(vars.path).toEqual({ inboundOrderId: 'inbound-1' })
    expect(vars.query).toEqual(SCOPE)
    expect(typeof vars.body.idempotencyKey).toBe('string')
    expect(vars.body.idempotencyKey.length).toBeGreaterThan(0)
  })

  it('keeps packReviewNo/passed and never lets a caller-supplied idempotencyKey win on outbound complete', async () => {
    const { completeOutbound } = useWmsOutbound()
    await completeOutbound('outbound-1', {
      packReviewNo: 'PR',
      passed: true,
      // 调用方试图覆盖幂等键——必须被内部注入覆盖。
      idempotencyKey: 'evil',
    } as never)

    const vars = coladaState.lastMutationVars.get('completeOutbound') as {
      path: { outboundOrderId: string }
      body: { packReviewNo: string; passed?: boolean; idempotencyKey: string }
    }
    expect(vars.path).toEqual({ outboundOrderId: 'outbound-1' })
    expect(vars.body.packReviewNo).toBe('PR')
    expect(vars.body.passed).toBe(true)
    expect(vars.body.idempotencyKey).not.toBe('evil')
    expect(vars.body.idempotencyKey.length).toBeGreaterThan(0)
  })

  it('injects an idempotency key alongside countedQuantity on count complete', async () => {
    const { completeCount } = useWmsCount()
    await completeCount('count-1', { countedQuantity: 5 })

    const vars = coladaState.lastMutationVars.get('completeCount') as {
      path: { countExecutionId: string }
      body: { countedQuantity?: number; idempotencyKey: string }
    }
    expect(vars.path).toEqual({ countExecutionId: 'count-1' })
    expect(vars.body.countedQuantity).toBe(5)
    expect(typeof vars.body.idempotencyKey).toBe('string')
    expect(vars.body.idempotencyKey.length).toBeGreaterThan(0)
  })

  it('enables picking/putaway read-only lists without a non-empty operatorUserId', () => {
    useWmsPicking()
    useWmsPutaway()

    expect(coladaState.queryOptionsById.get('listBusinessConsoleWmsPickingTasks')?.enabled).toBe(true)
    expect(coladaState.queryOptionsById.get('listBusinessConsoleWmsPutawayTasks')?.enabled).toBe(true)

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
})
