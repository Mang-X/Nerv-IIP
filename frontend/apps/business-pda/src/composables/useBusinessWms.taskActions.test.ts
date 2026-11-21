import { coladaState, SCOPE, setupBusinessWmsTests } from './useBusinessWmsTestFixture'
import { expect, it, vi } from 'vitest'
import {
  listBusinessConsoleWmsPickingTasks,
  startBusinessConsoleWmsPickingTask,
} from '@nerv-iip/api-client'
import { peekPendingBusinessIntent } from '@nerv-iip/business-core'
import { useWmsPicking } from './useBusinessWms'
import { describe } from 'vitest'

describe('PDA WMS composables', () => {
  setupBusinessWmsTests()

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
})
