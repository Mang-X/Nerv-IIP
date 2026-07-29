import { describe, expect, it } from 'vitest'

import {
  LIFECYCLE_ACTION_UPDATED_MESSAGE,
  LifecycleActionUnavailableError,
  assertLifecycleActionExecutable,
  isLifecycleConflictError,
  useLifecycleActionRecovery,
} from './lifecycleActionRecovery'
import { vi } from 'vitest'

describe('lifecycle action recovery', () => {
  it('fails closed when the authoritative status is unknown', () => {
    expect(() =>
      assertLifecycleActionExecutable({
        domain: 'wms-count',
        action: 'complete',
        facts: { status: 'mystery' },
      }),
    ).toThrow(LifecycleActionUnavailableError)
  })

  it('allows only the canonical persisted operation-task status', () => {
    expect(() =>
      assertLifecycleActionExecutable({
        domain: 'mes-operation-task',
        action: 'start',
        facts: { status: 'Queued' },
      }),
    ).not.toThrow()

    expect(() =>
      assertLifecycleActionExecutable({
        domain: 'mes-operation-task',
        action: 'start',
        facts: { status: 'Ready' },
      }),
    ).toThrow(LIFECYCLE_ACTION_UPDATED_MESSAGE)
  })

  it('allows an authoritative legal no-op only for an explicit idempotent replay', () => {
    expect(() =>
      assertLifecycleActionExecutable({
        domain: 'quality-inspection-task',
        action: 'create-record',
        facts: { status: 'completed', inspectionRecordId: 'IR-1' },
      }),
    ).toThrow(LIFECYCLE_ACTION_UPDATED_MESSAGE)

    expect(() =>
      assertLifecycleActionExecutable({
        domain: 'quality-inspection-task',
        action: 'create-record',
        facts: {
          status: 'completed',
          inspectionRecordId: 'IR-1',
          idempotentReplay: true,
        },
      }),
    ).not.toThrow()
  })

  it('recognizes only the stable typed lifecycle conflict body', () => {
    expect(isLifecycleConflictError({ success: false, message: 'lifecycle-conflict' })).toBe(true)
    expect(isLifecycleConflictError({ success: true, message: 'lifecycle-conflict' })).toBe(false)
    expect(isLifecycleConflictError({ message: 'lifecycle-conflict' })).toBe(false)
    expect(isLifecycleConflictError({ statusCode: 400, message: 'validation failed' })).toBe(false)
    expect(isLifecycleConflictError({ statusCode: 422, message: 'lifecycle-conflict-ish' })).toBe(
      false,
    )
    expect(isLifecycleConflictError(new Error('lifecycle-conflict'))).toBe(false)
  })

  it('clears stale UI before refreshing and exposes the mobile toast state', async () => {
    const order: string[] = []
    const reset = vi.fn(() => order.push('reset'))
    const refresh = vi.fn(async () => {
      order.push('refresh')
    })
    const recovery = useLifecycleActionRecovery({ reset, refresh })

    await expect(recovery.handle({ success: false, message: 'lifecycle-conflict' })).resolves.toBe(
      true,
    )

    expect(order).toEqual(['reset', 'refresh'])
    expect(recovery.toast.value).toEqual({
      show: true,
      message: LIFECYCLE_ACTION_UPDATED_MESSAGE,
      type: 'error',
    })

    await expect(recovery.handle({ statusCode: 400, message: 'validation failed' })).resolves.toBe(
      false,
    )
    expect(reset).toHaveBeenCalledTimes(1)
  })

  it('still exposes the fixed toast when the best-effort refresh fails', async () => {
    const reset = vi.fn()
    const refresh = vi.fn(async () => {
      throw new Error('refresh unavailable')
    })
    const recovery = useLifecycleActionRecovery({ reset, refresh })

    await expect(recovery.handle({ success: false, message: 'lifecycle-conflict' })).resolves.toBe(
      true,
    )

    expect(reset).toHaveBeenCalledTimes(1)
    expect(refresh).toHaveBeenCalledTimes(1)
    expect(recovery.toast.value).toEqual({
      show: true,
      message: LIFECYCLE_ACTION_UPDATED_MESSAGE,
      type: 'error',
    })
  })
})
