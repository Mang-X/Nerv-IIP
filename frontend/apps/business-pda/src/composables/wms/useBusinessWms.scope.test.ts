import { coladaState, reactiveAuthState, setupBusinessWmsTests } from './useBusinessWmsTestFixture'
import { expect, it, vi } from 'vitest'
import { nextTick } from 'vue'
import {
  listBusinessConsoleWmsCountExecutionsQueryOptions,
  listBusinessConsoleWmsInboundOrdersQueryOptions,
  listBusinessConsoleWmsOutboundOrdersQueryOptions,
  listBusinessConsoleWmsPickingTasksQueryOptions,
  listBusinessConsoleWmsPutawayTasksQueryOptions,
} from '@nerv-iip/api-client'
import { useWmsCount, useWmsInbound, useWmsOutbound, useWmsPicking, useWmsPutaway } from '.'
import { describe } from 'vitest'

describe('PDA WMS composables', () => {
  setupBusinessWmsTests()

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
    }

    reactiveAuthState.principal = { organizationId: 'org-002', environmentId: 'env-prod' }
    await nextTick()

    for (const result of results) {
      expect(result.rows.value).toEqual([])
      expect(result.total.value).toBe(0)
      expect(result.hasSuccessfulResponse.value).toBe(false)
      expect(result.hasFailedResponse.value).toBe(false)
    }
  })
})
