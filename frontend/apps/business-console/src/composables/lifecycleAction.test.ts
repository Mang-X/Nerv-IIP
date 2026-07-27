import { describe, expect, it, vi } from 'vitest'
import {
  LIFECYCLE_STATE_CHANGED_MESSAGE,
  LifecycleStateChangedError,
  executeLifecycleAction,
  recoverLifecycleAction,
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
          facts: { status: 'Cancelled' },
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
