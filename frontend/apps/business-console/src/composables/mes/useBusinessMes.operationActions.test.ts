import { coladaState, receiptState, setupBusinessMesTests } from './useBusinessMesTestFixture'
import { expect, it, vi } from 'vitest'
import {
  listBusinessConsoleMesOperationTasks,
  startBusinessConsoleMesOperationTaskMutationOptions,
} from '@nerv-iip/api-client'
import { useMesOperationTasks } from '.'
import { describe } from 'vitest'

describe('business MES composables', () => {
  setupBusinessMesTests()

  it('keeps the original MES action key across failed accepted readback and composable re-entry', async () => {
    const context = {
      organizationId: 'org-001',
      environmentId: 'env-dev',
      workOrderId: 'wo-1',
    }
    vi.mocked(listBusinessConsoleMesOperationTasks).mockResolvedValue({
      data: {
        success: true,
        data: {
          items: [{ operationTaskId: 'task-1', workOrderId: 'wo-1', status: 'Queued' }],
          total: 1,
        },
      },
    } as never)
    receiptState.confirm
      .mockRejectedValueOnce(
        Object.assign(new Error('请求已受理，但权威状态尚未确认'), {
          code: 'business-operation-unconfirmed',
        }),
      )
      .mockImplementation(async (value) => value)

    const firstPage = useMesOperationTasks()
    await expect(
      firstPage.startOperationTask('task-1', context, {
        reasonCode: 'OPERATOR_READY',
        idempotencyKey: 'page-random-key-1',
      }),
    ).rejects.toThrow('权威状态尚未确认')

    const returnedPage = useMesOperationTasks()
    await returnedPage.startOperationTask('task-1', context, {
      reasonCode: 'OPERATOR_READY',
      idempotencyKey: 'page-random-key-2',
    })

    const newIntentPage = useMesOperationTasks()
    await newIntentPage.startOperationTask('task-1', context, {
      reasonCode: 'OPERATOR_READY',
      idempotencyKey: 'page-random-key-3',
    })

    const sentKeys = vi
      .mocked(startBusinessConsoleMesOperationTaskMutationOptions)
      .mock.results.map(
        ({ value }) =>
          (value.mutation as ReturnType<typeof vi.fn>).mock.calls[0]?.[0].body.idempotencyKey,
      )

    expect(sentKeys).toEqual(['page-random-key-1', 'page-random-key-1', 'page-random-key-3'])
    const firstRequest = vi.mocked(startBusinessConsoleMesOperationTaskMutationOptions).mock
      .results[0]?.value.mutation as ReturnType<typeof vi.fn>
    expect(firstRequest.mock.calls[0]?.[0].query).toEqual({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      scopeKind: 'work-center',
      scopeId: 'WC-A',
    })
    expect(listBusinessConsoleMesOperationTasks).toHaveBeenCalledWith(
      expect.objectContaining({
        query: expect.objectContaining({
          scopeKind: 'work-center',
          scopeId: 'WC-A',
        }),
      }),
    )
  })

  it('fails closed before readback or mutation when no authorized operation scope is selected', async () => {
    coladaState.queryDataById.set(
      'getBusinessConsolePrincipalWorkContext:business.mes.operations.manage',
      { success: true, data: { selectedScope: null } },
    )
    vi.mocked(listBusinessConsoleMesOperationTasks).mockClear()
    const page = useMesOperationTasks()
    const mutation = vi.mocked(startBusinessConsoleMesOperationTaskMutationOptions).mock.results[0]
      ?.value.mutation as ReturnType<typeof vi.fn>

    await expect(
      page.startOperationTask(
        'task-no-scope',
        {
          organizationId: 'org-001',
          environmentId: 'env-dev',
          workOrderId: 'wo-no-scope',
        },
        { idempotencyKey: 'no-scope-key' },
      ),
      // 读到了但授权清单为空：提示「没有已授权的作业范围」并指向管理员配置授权（#1288），不再泛泛说「尚未选择」。
    ).rejects.toThrow('没有已授权的作业范围')

    expect(listBusinessConsoleMesOperationTasks).not.toHaveBeenCalled()
    expect(mutation).not.toHaveBeenCalled()
  })

  it('rotates the MES action key after an explicit 422 rejection', async () => {
    const context = {
      organizationId: 'org-001',
      environmentId: 'env-dev',
      workOrderId: 'wo-422',
    }
    vi.mocked(listBusinessConsoleMesOperationTasks).mockResolvedValue({
      data: {
        success: true,
        data: {
          items: [{ operationTaskId: 'task-422', workOrderId: 'wo-422', status: 'Queued' }],
          total: 1,
        },
      },
    } as never)
    receiptState.confirm
      .mockRejectedValueOnce(Object.assign(new Error('validation failed'), { statusCode: 422 }))
      .mockImplementation(async (value) => value)

    const page = useMesOperationTasks()
    await expect(
      page.startOperationTask('task-422', context, {
        reasonCode: 'OPERATOR_READY',
        idempotencyKey: 'mes-key-1',
      }),
    ).rejects.toThrow('validation failed')

    const returnedPage = useMesOperationTasks()
    await returnedPage.startOperationTask('task-422', context, {
      reasonCode: 'OPERATOR_READY',
      idempotencyKey: 'mes-key-2',
    })

    const sentKeys = vi
      .mocked(startBusinessConsoleMesOperationTaskMutationOptions)
      .mock.results.map(
        ({ value }) =>
          (value.mutation as ReturnType<typeof vi.fn>).mock.calls[0]?.[0].body.idempotencyKey,
      )
    expect(sentKeys).toEqual(['mes-key-1', 'mes-key-2'])
  })
})
