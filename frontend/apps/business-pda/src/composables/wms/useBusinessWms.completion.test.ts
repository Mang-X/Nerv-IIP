import { coladaState, SCOPE, setupBusinessWmsTests } from './useBusinessWmsTestFixture'
import { expect, it, vi } from 'vitest'
import { nextTick } from 'vue'
import { confirmBusinessConsoleOperation } from '@nerv-iip/api-client'
import {
  acquirePendingBusinessIntent,
  clearPendingBusinessIntent,
  peekPendingBusinessIntent,
} from '@nerv-iip/business-core'
import { useWmsCount, useWmsInbound, useWmsOutbound } from '.'
import { describe } from 'vitest'

describe('PDA WMS composables', () => {
  setupBusinessWmsTests()

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
        passed?: boolean
        idempotencyKey: string
        scopeKind: string
        scopeId: string
        expectedVersion: number
      }
    }
    expect(vars.path).toEqual({ outboundOrderId: 'outbound-1' })
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
      payloadFingerprint: 'outbound-1:{"passed":true}',
      payloadSnapshot: {
        passed: true,
        scopeKind: 'self',
        scopeId: 'emp049',
        expectedVersion: 6,
      },
      mutationId: 'completeOutbound',
      expectedBody: {
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
          { passed: true, idempotencyKey: 'KEY-NEW' },
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
        // 出库复核另带更长的回读预算（#3926），这里只核意图键与回读范围。
        expect(confirmBusinessConsoleOperation).toHaveBeenCalledWith(
          receipt,
          expect.objectContaining({
            expectedOperationType: operationType,
            expectedIdempotencyKey: 'KEY-OLD',
            expectedResourceId: resourceId,
            readbackScope: { scopeKind: 'self', scopeId: 'emp049' },
          }),
        )
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
        result.completeOutbound('outbound-1', { passed: true, idempotencyKey: key }, { attempt }),
      expectedBody: {
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
      expect(vi.mocked(confirmBusinessConsoleOperation).mock.calls.at(-1)?.[1]).toMatchObject({
        readbackScope: { scopeKind: 'self', scopeId: 'emp049' },
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
    const input = { passed: true, idempotencyKey: 'KEY-OUT' }

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
        payloadFingerprint: 'outbound-1:{"passed":true}',
      },
      () => 'KEY-OUT',
      {
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
})
