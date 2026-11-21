import { coladaState, SCOPE, setupBusinessWmsTests } from './useBusinessWmsTestFixture'
import { expect, it } from 'vitest'
import { acquirePendingBusinessIntent } from '@nerv-iip/business-core'
import { useWmsCount } from '.'
import { describe } from 'vitest'

describe('PDA WMS composables', () => {
  setupBusinessWmsTests()

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
})
