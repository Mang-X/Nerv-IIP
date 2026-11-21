import {
  coladaState,
  receiptState,
  reactiveAuthState,
  deferred,
  operationActionOptions,
  setupBusinessMesTests,
} from './useBusinessMesTestFixture'
import { expect, it, vi } from 'vitest'
import { nextTick } from 'vue'
import {
  claimBusinessConsoleMesOperationTaskMutationOptions,
  completeBusinessConsoleMesOperationTaskMutationOptions,
  listBusinessConsoleMesOperationTasks,
  startBusinessConsoleMesOperationTaskMutationOptions,
} from '@nerv-iip/api-client'
import { MES_WORK_SCOPE_UNAVAILABLE_MESSAGE, useMesOperationTasks } from '.'
import { describe } from 'vitest'

describe('pda useBusinessMes composables', () => {
  setupBusinessMesTests()

  it('keeps the original operation key after accepted-readback failure and PDA page re-entry', async () => {
    receiptState.confirm
      .mockRejectedValueOnce(
        Object.assign(new Error('请求已受理，但权威状态尚未确认'), {
          code: 'business-operation-unconfirmed',
        }),
      )
      .mockImplementation(async (value) => value)

    const firstPage = useMesOperationTasks()
    const firstMutation = coladaState.mutateById.get('startBusinessConsoleMesOperationTask')!
    await expect(
      firstPage.startTask(
        'wo-1',
        'ot-reentry',
        operationActionOptions(
          firstPage,
          'start',
          'wo-1',
          'ot-reentry',
          'pda-page-key-1',
          'OPERATOR_READY',
        ),
      ),
    ).rejects.toThrow('权威状态尚未确认')

    const returnedPage = useMesOperationTasks()
    const retryMutation = coladaState.mutateById.get('startBusinessConsoleMesOperationTask')!
    await returnedPage.startTask(
      'wo-1',
      'ot-reentry',
      operationActionOptions(
        returnedPage,
        'start',
        'wo-1',
        'ot-reentry',
        'pda-page-key-2',
        'OPERATOR_READY',
      ),
    )

    const newIntentPage = useMesOperationTasks()
    const newIntentMutation = coladaState.mutateById.get('startBusinessConsoleMesOperationTask')!
    await newIntentPage.startTask(
      'wo-1',
      'ot-reentry',
      operationActionOptions(
        newIntentPage,
        'start',
        'wo-1',
        'ot-reentry',
        'pda-page-key-3',
        'OPERATOR_READY',
      ),
    )

    expect(firstMutation.mock.calls[0]?.[0].body.idempotencyKey).toBe('pda-page-key-1')
    expect(retryMutation.mock.calls[0]?.[0].body.idempotencyKey).toBe('pda-page-key-1')
    expect(newIntentMutation.mock.calls[0]?.[0].body.idempotencyKey).toBe('pda-page-key-3')
  })

  it('completes an operation task forwarding the caller-supplied idempotency key', async () => {
    const mes = useMesOperationTasks()

    await mes.completeTask(
      'wo-1',
      'ot-9',
      operationActionOptions(mes, 'complete', 'wo-1', 'ot-9', 'op-complete-1'),
    )

    expect(completeBusinessConsoleMesOperationTaskMutationOptions).toHaveBeenCalled()
    const mutateAsync = coladaState.mutateById.get('completeBusinessConsoleMesOperationTask')
    expect(mutateAsync).toHaveBeenCalledTimes(1)
    const payload = mutateAsync!.mock.calls[0][0]
    expect(payload.path).toEqual({ operationTaskId: 'ot-9' })
    expect(payload.query).toEqual({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      scopeKind: 'work-center',
      scopeId: 'WC-A',
    })
    expect(payload.body.idempotencyKey).toBe('op-complete-1')
    expect(listBusinessConsoleMesOperationTasks).toHaveBeenCalledWith(
      expect.objectContaining({
        query: expect.objectContaining({
          scopeKind: 'work-center',
          scopeId: 'WC-A',
        }),
      }),
    )
  })

  it('claims an unassigned queued task with the authenticated work-center scope and a stable key', async () => {
    vi.mocked(listBusinessConsoleMesOperationTasks).mockResolvedValue({
      data: {
        success: true,
        data: {
          items: [
            {
              operationTaskId: 'ot-claim',
              workOrderId: 'wo-claim',
              status: 'Queued',
              assignedUserId: 'user-001',
            },
          ],
          total: 1,
        },
      },
    } as never)
    const mes = useMesOperationTasks()
    const mutateAsync = coladaState.mutateById.get('claimBusinessConsoleMesOperationTask')!
    mutateAsync.mockResolvedValue({ success: true, data: { status: 'Accepted' } })
    const context = mes.captureOperationActionContext('claim', 'wo-claim', 'ot-claim')

    const claimed = await mes.claimTask('wo-claim', 'ot-claim', {
      idempotencyKey: 'operation-claim-1',
      context,
    })

    expect(claimBusinessConsoleMesOperationTaskMutationOptions).toHaveBeenCalled()
    expect(mutateAsync).toHaveBeenCalledWith({
      path: { operationTaskId: 'ot-claim' },
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        scopeKind: 'work-center',
        scopeId: 'WC-A',
      },
      body: { idempotencyKey: 'operation-claim-1' },
    })
    expect(claimed?.assignedUserId).toBe('user-001')
  })

  it('reuses the pending claim key after an unknown result even when the retry supplies a new key', async () => {
    vi.mocked(listBusinessConsoleMesOperationTasks).mockResolvedValue({
      data: {
        success: true,
        data: {
          items: [
            { operationTaskId: 'ot-claim', workOrderId: 'wo-claim', assignedUserId: 'user-001' },
          ],
          total: 1,
        },
      },
    } as never)
    const mes = useMesOperationTasks()
    const mutateAsync = coladaState.mutateById.get('claimBusinessConsoleMesOperationTask')!
    mutateAsync
      .mockRejectedValueOnce(new TypeError('response lost after commit'))
      .mockResolvedValueOnce({ success: true, data: { status: 'Accepted' } })
    const context = mes.captureOperationActionContext('claim', 'wo-claim', 'ot-claim')

    await expect(
      mes.claimTask('wo-claim', 'ot-claim', { idempotencyKey: 'claim-key-1', context }),
    ).rejects.toThrow('response lost after commit')
    await mes.claimTask('wo-claim', 'ot-claim', { idempotencyKey: 'claim-key-2', context })

    expect(mutateAsync.mock.calls[0][0].body.idempotencyKey).toBe('claim-key-1')
    expect(mutateAsync.mock.calls[1][0].body.idempotencyKey).toBe('claim-key-1')
  })

  it('fails closed before task readback and mutation when no operation scope is selected', async () => {
    coladaState.queryDataById.set(
      'getBusinessConsolePrincipalWorkContext:business.mes.operations.manage',
      { success: true, data: { selectedScope: null } },
    )
    vi.mocked(listBusinessConsoleMesOperationTasks).mockClear()
    const mes = useMesOperationTasks()
    const mutateAsync = coladaState.mutateById.get('startBusinessConsoleMesOperationTask')!

    expect(mes.operationScopeReady.value).toBe(false)
    expect(
      () => mes.captureOperationActionContext('start', 'wo-1', 'ot-no-scope'),
      // 同上：授权清单为空的真话是「没有已授权范围」，不能说成「还没选」（#1297）。
    ).toThrow(MES_WORK_SCOPE_UNAVAILABLE_MESSAGE)
    expect(listBusinessConsoleMesOperationTasks).not.toHaveBeenCalled()
    expect(mutateAsync).not.toHaveBeenCalled()
  })

  it('clears an operation intent after a determinate 422 so the next attempt uses a new key', async () => {
    const mes = useMesOperationTasks()
    const mutateAsync = coladaState.mutateById.get('pauseBusinessConsoleMesOperationTask')!
    mutateAsync
      .mockRejectedValueOnce({ status: 422, message: 'invalid transition' })
      .mockResolvedValueOnce({ success: true, data: {} })

    await expect(
      mes.pauseTask(
        'wo-1',
        'ot-determinate',
        operationActionOptions(mes, 'pause', 'wo-1', 'ot-determinate', 'operation-key-1'),
      ),
    ).rejects.toMatchObject({ status: 422 })
    await mes.pauseTask(
      'wo-1',
      'ot-determinate',
      operationActionOptions(mes, 'pause', 'wo-1', 'ot-determinate', 'operation-key-2'),
    )

    expect(mutateAsync.mock.calls[0][0].body.idempotencyKey).toBe('operation-key-1')
    expect(mutateAsync.mock.calls[1][0].body.idempotencyKey).toBe('operation-key-2')
  })

  it('re-reads the exact operation task and blocks a completed task before mutation', async () => {
    vi.mocked(listBusinessConsoleMesOperationTasks).mockResolvedValue({
      data: {
        success: true,
        data: {
          items: [{ operationTaskId: 'ot-9', workOrderId: 'wo-1', status: 'Completed' }],
          total: 1,
        },
      },
    } as never)
    const mes = useMesOperationTasks()

    await expect(
      mes.completeTask(
        'wo-1',
        'ot-9',
        operationActionOptions(mes, 'complete', 'wo-1', 'ot-9', 'op-complete-1'),
      ),
    ).rejects.toThrow('状态已被其他操作更新')

    expect(
      coladaState.mutateById.get('completeBusinessConsoleMesOperationTask'),
    ).not.toHaveBeenCalled()
  })

  it('re-reads the exact strong-ID pair and rejects an action omitted by server allowedActions', async () => {
    vi.mocked(listBusinessConsoleMesOperationTasks).mockResolvedValue({
      data: {
        success: true,
        data: {
          items: [
            {
              operationTaskId: 'ot-blocked',
              workOrderId: 'wo-blocked',
              status: 'Queued',
              allowedActions: [],
              blockReasons: ['MATERIAL_SHORTAGE: 物料 MAT-1 缺口 2'],
              evaluatedAtUtc: '2026-08-02T08:30:00.000Z',
            },
          ],
          total: 1,
        },
      },
    } as never)
    const mes = useMesOperationTasks()

    await expect(
      mes.startTask(
        'wo-blocked',
        'ot-blocked',
        operationActionOptions(mes, 'start', 'wo-blocked', 'ot-blocked', 'op-blocked-1'),
      ),
    ).rejects.toThrow('状态已被其他操作更新')

    expect(listBusinessConsoleMesOperationTasks).toHaveBeenCalledWith(
      expect.objectContaining({
        query: expect.objectContaining({
          workOrderId: 'wo-blocked',
          operationTaskId: 'ot-blocked',
          scopeKind: 'work-center',
          scopeId: 'WC-A',
        }),
      }),
    )
    expect(
      coladaState.mutateById.get('startBusinessConsoleMesOperationTask'),
    ).not.toHaveBeenCalled()
  })

  it('fails closed when a retained start intent still reads Queued without a server action', async () => {
    vi.mocked(listBusinessConsoleMesOperationTasks)
      .mockResolvedValueOnce({
        data: {
          success: true,
          data: {
            items: [
              {
                operationTaskId: 'ot-replay-blocked',
                workOrderId: 'wo-replay-blocked',
                status: 'Queued',
                allowedActions: ['start'],
              },
            ],
            total: 1,
          },
        },
      } as never)
      .mockResolvedValueOnce({
        data: {
          success: true,
          data: {
            items: [
              {
                operationTaskId: 'ot-replay-blocked',
                workOrderId: 'wo-replay-blocked',
                status: 'Queued',
                allowedActions: [],
              },
            ],
            total: 1,
          },
        },
      } as never)
    receiptState.confirm.mockRejectedValueOnce(new TypeError('response lost'))
    const mes = useMesOperationTasks()
    const mutateAsync = coladaState.mutateById.get('startBusinessConsoleMesOperationTask')!
    const request = operationActionOptions(
      mes,
      'start',
      'wo-replay-blocked',
      'ot-replay-blocked',
      'op-replay-blocked-1',
      'OPERATOR_READY',
    )

    await expect(mes.startTask('wo-replay-blocked', 'ot-replay-blocked', request)).rejects.toThrow(
      'response lost',
    )
    await expect(mes.startTask('wo-replay-blocked', 'ot-replay-blocked', request)).rejects.toThrow(
      '状态已被其他操作更新',
    )

    expect(mutateAsync).toHaveBeenCalledTimes(1)
  })

  it('allows the same retained start intent to replay when the authoritative status is its legal result', async () => {
    vi.mocked(listBusinessConsoleMesOperationTasks)
      .mockResolvedValueOnce({
        data: {
          success: true,
          data: {
            items: [
              {
                operationTaskId: 'ot-replay-applied',
                workOrderId: 'wo-replay-applied',
                status: 'Queued',
                allowedActions: ['start'],
              },
            ],
            total: 1,
          },
        },
      } as never)
      .mockResolvedValueOnce({
        data: {
          success: true,
          data: {
            items: [
              {
                operationTaskId: 'ot-replay-applied',
                workOrderId: 'wo-replay-applied',
                status: 'InProgress',
                allowedActions: [],
              },
            ],
            total: 1,
          },
        },
      } as never)
    receiptState.confirm.mockRejectedValueOnce(new TypeError('response lost'))
    const mes = useMesOperationTasks()
    const mutateAsync = coladaState.mutateById.get('startBusinessConsoleMesOperationTask')!
    const request = operationActionOptions(
      mes,
      'start',
      'wo-replay-applied',
      'ot-replay-applied',
      'op-replay-applied-1',
      'OPERATOR_READY',
    )

    await expect(mes.startTask('wo-replay-applied', 'ot-replay-applied', request)).rejects.toThrow(
      'response lost',
    )
    await expect(
      mes.startTask('wo-replay-applied', 'ot-replay-applied', request),
    ).resolves.toBeUndefined()

    expect(mutateAsync).toHaveBeenCalledTimes(2)
    expect(mutateAsync.mock.calls[1]?.[0].body.idempotencyKey).toBe('op-replay-applied-1')
  })

  it('requires a complete non-empty frozen context before pending acquire, readback, or mutation', async () => {
    const mes = useMesOperationTasks() as ReturnType<typeof useMesOperationTasks> & {
      captureOperationActionContext: (
        action: 'start',
        workOrderId: string,
        operationTaskId: string,
      ) => Record<string, string>
    }
    const mutateAsync = coladaState.mutateById.get('startBusinessConsoleMesOperationTask')!
    vi.mocked(listBusinessConsoleMesOperationTasks).mockClear()

    await expect(
      mes.startTask('wo-1', 'ot-reentry', { idempotencyKey: 'missing-context' } as never),
    ).rejects.toThrow('账号、组织、环境或作业范围已变化')

    const context = mes.captureOperationActionContext('start', 'wo-1', 'ot-reentry')
    await expect(
      mes.startTask('wo-1', 'ot-reentry', {
        idempotencyKey: 'empty-context',
        context: { ...context, organizationId: '' },
      } as never),
    ).rejects.toThrow('账号、组织、环境或作业范围已变化')

    expect(listBusinessConsoleMesOperationTasks).not.toHaveBeenCalled()
    expect(mutateAsync).not.toHaveBeenCalled()
  })

  it.each([
    {
      name: 'principal',
      drift: () => {
        reactiveAuthState.principal = {
          principalId: 'user-002',
          organizationId: 'org-001',
          environmentId: 'env-dev',
        }
      },
    },
    {
      name: 'manage scope',
      drift: () => {
        coladaState.queryDataRefById.get(
          'getBusinessConsolePrincipalWorkContext:business.mes.operations.manage',
        )!.value = {
          success: true,
          data: { selectedScope: { kind: 'work-center', id: 'WC-B', displayName: '精加工二线' } },
        }
      },
    },
    {
      name: 'organization/environment',
      drift: (mes: ReturnType<typeof useMesOperationTasks>) => {
        mes.filters.organizationId = 'org-002'
        mes.filters.environmentId = 'env-qa'
      },
    },
  ])(
    'fails closed after deferred exact read when $name drifts mid-flight',
    async ({ drift, name }) => {
      const exactRead = deferred<never>()
      vi.mocked(listBusinessConsoleMesOperationTasks).mockReturnValueOnce(exactRead.promise)
      const mes = useMesOperationTasks() as ReturnType<typeof useMesOperationTasks> & {
        captureOperationActionContext: (
          action: 'start',
          workOrderId: string,
          operationTaskId: string,
        ) => Record<string, string>
      }
      const mutateAsync = coladaState.mutateById.get('startBusinessConsoleMesOperationTask')!
      const context = mes.captureOperationActionContext('start', 'wo-1', 'ot-reentry')

      const action = mes.startTask('wo-1', 'ot-reentry', {
        idempotencyKey: `mid-flight-${name}`,
        context,
      } as never)
      await vi.waitFor(() => expect(listBusinessConsoleMesOperationTasks).toHaveBeenCalledTimes(1))
      drift(mes)
      await nextTick()
      exactRead.resolve({
        data: {
          success: true,
          data: {
            items: [
              {
                operationTaskId: 'ot-reentry',
                workOrderId: 'wo-1',
                status: 'Queued',
                allowedActions: ['start'],
              },
            ],
            total: 1,
          },
        },
      } as never)

      await expect(action).rejects.toThrow('账号、组织、环境或作业范围已变化')
      expect(mutateAsync).not.toHaveBeenCalled()
    },
  )

  it('fails closed before replay when the principal changes after an unconfirmed result', async () => {
    receiptState.confirm.mockRejectedValueOnce(new TypeError('response lost'))
    const mes = useMesOperationTasks()
    const mutateAsync = coladaState.mutateById.get('startBusinessConsoleMesOperationTask')!
    const request = operationActionOptions(
      mes,
      'start',
      'wo-1',
      'ot-reentry',
      'principal-drift-key',
    )

    await expect(mes.startTask('wo-1', 'ot-reentry', request)).rejects.toThrow('response lost')
    reactiveAuthState.principal = {
      principalId: 'user-002',
      organizationId: 'org-001',
      environmentId: 'env-dev',
    }
    await nextTick()

    await expect(mes.startTask('wo-1', 'ot-reentry', request)).rejects.toThrow(
      '账号、组织、环境或作业范围已变化',
    )
    expect(mutateAsync).toHaveBeenCalledTimes(1)
  })

  it('fails closed before replay when the manage scope changes after an unconfirmed result', async () => {
    receiptState.confirm.mockRejectedValueOnce(new TypeError('response lost'))
    const mes = useMesOperationTasks()
    const mutateAsync = coladaState.mutateById.get('startBusinessConsoleMesOperationTask')!
    const request = operationActionOptions(mes, 'start', 'wo-1', 'ot-reentry', 'scope-drift-key')

    await expect(mes.startTask('wo-1', 'ot-reentry', request)).rejects.toThrow('response lost')
    coladaState.queryDataRefById.get(
      'getBusinessConsolePrincipalWorkContext:business.mes.operations.manage',
    )!.value = {
      success: true,
      data: { selectedScope: { kind: 'work-center', id: 'WC-B', displayName: '精加工二线' } },
    }
    await nextTick()

    await expect(mes.startTask('wo-1', 'ot-reentry', request)).rejects.toThrow(
      '账号、组织、环境或作业范围已变化',
    )
    expect(mutateAsync).toHaveBeenCalledTimes(1)
  })

  it('reports context drift before scope validation when the manage scope disappears', async () => {
    receiptState.confirm.mockRejectedValueOnce(new TypeError('response lost'))
    const mes = useMesOperationTasks()
    const mutateAsync = coladaState.mutateById.get('startBusinessConsoleMesOperationTask')!
    const request = operationActionOptions(mes, 'start', 'wo-1', 'ot-reentry', 'scope-removed-key')

    await expect(mes.startTask('wo-1', 'ot-reentry', request)).rejects.toThrow('response lost')
    coladaState.queryDataRefById.get(
      'getBusinessConsolePrincipalWorkContext:business.mes.operations.manage',
    )!.value = { success: true, data: { selectedScope: null } }
    await nextTick()

    await expect(mes.startTask('wo-1', 'ot-reentry', request)).rejects.toThrow(
      '账号、组织、环境或作业范围已变化',
    )
    expect(mutateAsync).toHaveBeenCalledTimes(1)
  })

  it('starts an operation task forwarding an optional reason code with the caller-supplied key', async () => {
    const mes = useMesOperationTasks()

    await mes.startTask(
      'wo-1',
      'ot-3',
      operationActionOptions(mes, 'start', 'wo-1', 'ot-3', 'op-start-1', 'OPERATOR_READY'),
    )

    const mutateAsync = coladaState.mutateById.get('startBusinessConsoleMesOperationTask')
    expect(startBusinessConsoleMesOperationTaskMutationOptions).toHaveBeenCalled()
    const payload = mutateAsync!.mock.calls[0][0]
    expect(payload.path).toEqual({ operationTaskId: 'ot-3' })
    expect(payload.body).toMatchObject({ reasonCode: 'OPERATOR_READY' })
    expect(payload.body.idempotencyKey).toBe('op-start-1')
  })
})
