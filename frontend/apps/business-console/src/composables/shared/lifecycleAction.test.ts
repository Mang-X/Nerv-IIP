import { describe, expect, it, vi } from 'vitest'
import {
  acquirePendingBusinessIntent,
  clearPendingBusinessIntent,
  completePendingBusinessIntent,
  peekPendingBusinessIntent,
} from '@nerv-iip/business-core'
import {
  LIFECYCLE_STATE_CHANGED_MESSAGE,
  LifecycleStateChangedError,
  executeLifecycleAction,
  isIndeterminateLifecycleWriteError,
  recoverLifecycleAction,
  useLifecycleWriteIntent,
} from './lifecycleAction'

describe('executeLifecycleAction', () => {
  it('blocks the command when the authoritative reread is terminal', async () => {
    const command = vi.fn()

    await expect(
      executeLifecycleAction({
        readLatest: async () => ({
          domain: 'maintenance-work-order',
          action: 'complete',
          facts: { status: 'Completed' },
        }),
        command,
      }),
    ).rejects.toMatchObject({ source: 'preflight' })

    expect(command).not.toHaveBeenCalled()
  })

  it('allows an idempotent legal no-op to reach the command replay', async () => {
    const command = vi.fn(async () => ({
      data: { success: true, data: { id: 'WO-1' } },
      response: { status: 200 },
    }))

    await expect(
      executeLifecycleAction({
        readLatest: async () => ({
          domain: 'mes-work-order',
          action: 'cancel',
          facts: { status: 'Cancelled', idempotentReplay: true },
        }),
        command,
      }),
    ).resolves.toEqual({ success: true, data: { id: 'WO-1' } })

    expect(command).toHaveBeenCalledOnce()
  })

  it('classifies only HTTP 409 as a lifecycle conflict', async () => {
    await expect(
      executeLifecycleAction({
        readLatest: async () => ({
          domain: 'wms-inbound',
          action: 'complete',
          facts: { status: 'Open' },
        }),
        command: async () => ({
          error: { success: false, message: 'lifecycle-conflict', code: 409 },
          response: { status: 409 },
        }),
      }),
    ).rejects.toMatchObject({ source: 'conflict' })
  })

  it('classifies a generated rejected HTTP 409 as a conflict and recovers', async () => {
    const generatedConflict = Object.assign(new Error('generated conflict'), {
      response: { status: 409 },
    })
    const error = await executeLifecycleAction({
      readLatest: async () => ({
        domain: 'maintenance-work-order',
        action: 'complete',
        facts: { status: 'Open' },
      }),
      command: async () => {
        throw generatedConflict
      },
    }).catch((reason: unknown) => reason)

    expect(error).toBeInstanceOf(LifecycleStateChangedError)
    expect(error).toMatchObject({ source: 'conflict' })

    const reset = vi.fn()
    const refresh = vi.fn(async () => undefined)
    const notify = vi.fn()
    await expect(recoverLifecycleAction(error, { reset, refresh, notify })).resolves.toBe(true)
    expect(reset).toHaveBeenCalledOnce()
    expect(refresh).toHaveBeenCalledOnce()
    expect(notify).toHaveBeenCalledWith(LIFECYCLE_STATE_CHANGED_MESSAGE)
  })

  it.each([400, 422])('preserves a generated rejected HTTP %s error identity', async (status) => {
    const generatedValidation = Object.assign(new Error('generated validation'), {
      response: { status },
    })

    await expect(
      executeLifecycleAction({
        readLatest: async () => ({
          domain: 'maintenance-work-order',
          action: 'complete',
          facts: { status: 'Open' },
        }),
        command: async () => {
          throw generatedValidation
        },
      }),
    ).rejects.toBe(generatedValidation)
  })

  it('keeps a generated rejected HTTP 503 indeterminate and retains the pending intent', async () => {
    const pendingScope = {
      principalId: 'principal-generated-503',
      organizationId: 'org-generated-503',
      environmentId: 'env-generated-503',
      operationType: 'maintenance.work-order.complete',
      payloadFingerprint: 'generated-503',
    }
    const generatedUnavailable = Object.assign(new Error('generated unavailable'), {
      response: { status: 503 },
    })
    clearPendingBusinessIntent(pendingScope)
    acquirePendingBusinessIntent(pendingScope, () => 'generated-503-old')

    try {
      await expect(
        completePendingBusinessIntent(pendingScope, () =>
          executeLifecycleAction({
            readLatest: async () => ({
              domain: 'maintenance-work-order',
              action: 'complete',
              facts: { status: 'Open' },
            }),
            command: async () => {
              throw generatedUnavailable
            },
          }),
        ),
      ).rejects.toBe(generatedUnavailable)

      expect(isIndeterminateLifecycleWriteError(generatedUnavailable)).toBe(true)
      expect(peekPendingBusinessIntent(pendingScope)?.idempotencyKey).toBe('generated-503-old')
    } finally {
      clearPendingBusinessIntent(pendingScope)
    }
  })

  it('preserves an ordinary validation error instead of treating it as a conflict', async () => {
    const validationError = { success: false, message: '数量必须大于零', code: 400 }

    await expect(
      executeLifecycleAction({
        readLatest: async () => ({
          domain: 'wms-count',
          action: 'complete',
          facts: { status: 'Open' },
        }),
        command: async () => ({
          error: validationError,
          response: { status: 400 },
        }),
      }),
    ).rejects.toBe(validationError)
  })

  it.each([400, 422])('keeps HTTP %s as a determinate validation failure', (statusCode) => {
    expect(isIndeterminateLifecycleWriteError({ statusCode })).toBe(false)
  })

  it('classifies a generated statusCode 500 error as indeterminate', () => {
    expect(isIndeterminateLifecycleWriteError({ statusCode: 500 })).toBe(true)
  })

  it('preserves an otherwise untyped response 5xx for indeterminate classification', async () => {
    const serviceError = { success: false, message: 'service unavailable' }

    const caught = await executeLifecycleAction({
      readLatest: async () => ({
        domain: 'wms-outbound',
        action: 'complete',
        facts: { status: 'Open' },
      }),
      command: async () => ({
        error: serviceError,
        response: { status: 503 },
      }),
    }).catch((error: unknown) => error)

    expect(caught).toBe(serviceError)
    expect(isIndeterminateLifecycleWriteError(caught)).toBe(true)
  })

  it.each([
    [503, true],
    [422, false],
  ] as const)(
    'keeps generated envelope HTTP %s visible to pending-intent cleanup',
    async (status, shouldRetain) => {
      const pendingScope = {
        principalId: 'principal-lifecycle-status',
        organizationId: 'org-lifecycle-status',
        environmentId: 'env-lifecycle-status',
        operationType: 'wms.count-execution.complete',
        payloadFingerprint: `count-lifecycle-status-${status}`,
      }
      const envelopeError = { success: false, message: `generated envelope ${status}` }
      clearPendingBusinessIntent(pendingScope)
      acquirePendingBusinessIntent(pendingScope, () => `lifecycle-status-${status}`)

      try {
        await expect(
          completePendingBusinessIntent(pendingScope, () =>
            executeLifecycleAction({
              readLatest: async () => ({
                domain: 'wms-count',
                action: 'complete',
                facts: { status: 'Open' },
              }),
              command: async () => ({
                data: envelopeError,
                response: { status },
              }),
            }),
          ),
        ).rejects.toBe(envelopeError)

        expect(Boolean(peekPendingBusinessIntent(pendingScope))).toBe(shouldRetain)
      } finally {
        clearPendingBusinessIntent(pendingScope)
      }
    },
  )

  it('returns successful command data without replaying the command', async () => {
    const command = vi.fn(async () => ({
      data: { success: true, data: { id: 'WO-1' } },
      response: { status: 200 },
    }))

    await expect(
      executeLifecycleAction({
        readLatest: async () => ({
          domain: 'mes-operation-task',
          action: 'pause',
          facts: { status: 'InProgress' },
        }),
        command,
      }),
    ).resolves.toEqual({ success: true, data: { id: 'WO-1' } })

    expect(command).toHaveBeenCalledTimes(1)
  })
})

describe('recoverLifecycleAction', () => {
  it('clears stale UI, refreshes authoritative data, and shows the fixed operator message', async () => {
    const reset = vi.fn()
    const refresh = vi.fn(async () => undefined)
    const notify = vi.fn()

    await expect(
      recoverLifecycleAction(new LifecycleStateChangedError('conflict'), {
        reset,
        refresh,
        notify,
      }),
    ).resolves.toBe(true)

    expect(reset).toHaveBeenCalledTimes(1)
    expect(refresh).toHaveBeenCalledTimes(1)
    expect(notify).toHaveBeenCalledWith(LIFECYCLE_STATE_CHANGED_MESSAGE)
  })

  it('still shows the fixed operator message when the best-effort refresh fails', async () => {
    const reset = vi.fn()
    const refresh = vi.fn(async () => {
      throw new Error('refresh unavailable')
    })
    const notify = vi.fn()

    await expect(
      recoverLifecycleAction(new LifecycleStateChangedError('conflict'), {
        reset,
        refresh,
        notify,
      }),
    ).resolves.toBe(true)

    expect(reset).toHaveBeenCalledTimes(1)
    expect(refresh).toHaveBeenCalledTimes(1)
    expect(notify).toHaveBeenCalledWith(LIFECYCLE_STATE_CHANGED_MESSAGE)
  })

  it('leaves an ordinary error and the current form context untouched', async () => {
    const reset = vi.fn()
    const refresh = vi.fn(async () => undefined)
    const notify = vi.fn()

    await expect(
      recoverLifecycleAction(new Error('数量必须大于零'), { reset, refresh, notify }),
    ).resolves.toBe(false)

    expect(reset).not.toHaveBeenCalled()
    expect(refresh).not.toHaveBeenCalled()
    expect(notify).not.toHaveBeenCalled()
  })
})

describe('useLifecycleWriteIntent', () => {
  it('locks an indeterminate result to the original task, action, and idempotency key', () => {
    const intent = useLifecycleWriteIntent((taskId, action) => `${taskId}-${action}-stable`)

    const first = intent.acquire('OP-1', 'complete')
    expect(first?.key).toBe('OP-1-complete-stable')

    expect(intent.recordFailure({ statusCode: 503 })).toBe(true)
    expect(intent.locked.value).toBe(true)
    expect(intent.acquire('OP-2', 'complete')).toBeUndefined()
    expect(intent.acquire('OP-1', 'pause')).toBeUndefined()
    expect(intent.acquire('OP-1', 'complete')?.key).toBe(first?.key)

    expect(intent.recordFailure({ statusCode: 422 })).toBe(false)
    expect(intent.locked.value).toBe(false)
    expect(intent.current.value).toBeNull()
  })
})
