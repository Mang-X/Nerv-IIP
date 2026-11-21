import { coladaState, reactiveAuthState, setupBusinessMesTests } from './useBusinessMesTestFixture'
import { expect, it, vi } from 'vitest'
import { nextTick, shallowRef } from 'vue'
import {
  getBusinessConsoleMesCurrentOperationSopsQueryOptions,
  listBusinessConsoleMesReportableOperationTasks,
} from '@nerv-iip/api-client'
import { useMesCurrentOperationSops, useMesExactOperationTask } from './useBusinessMes'
import { describe } from 'vitest'

describe('pda useBusinessMes composables', () => {
  setupBusinessMesTests()

  it('continues exact task pagination across a full page when total is omitted', async () => {
    vi.mocked(listBusinessConsoleMesReportableOperationTasks)
      .mockResolvedValueOnce({
        data: {
          success: true,
          data: {
            items: Array.from({ length: 100 }, (_, index) => ({
              operationTaskId: `OP-${index + 1}`,
              workOrderId: 'WO-501',
            })),
          },
        },
      } as never)
      .mockResolvedValueOnce({
        data: {
          success: true,
          data: {
            items: [{ operationTaskId: 'OP-101', workOrderId: 'WO-501' }],
          },
        },
      } as never)
    const detail = shallowRef({
      workOrderId: 'WO-501',
      operationTasks: [],
    })

    useMesExactOperationTask(shallowRef('WO-501'), shallowRef('OP-101'), detail as never)
    const options = coladaState.queryFactoriesById.get('mes-report-exact-operation-task')?.()
    const result = await options?.query?.({ signal: new AbortController().signal })

    expect(result).toMatchObject({ operationTaskId: 'OP-101', workOrderId: 'WO-501' })
    expect(listBusinessConsoleMesReportableOperationTasks).toHaveBeenNthCalledWith(
      2,
      expect.objectContaining({
        query: expect.objectContaining({
          skip: 100,
          take: 100,
          workOrderId: 'WO-501',
          scopeKind: 'work-center',
          scopeId: 'WC-A',
        }),
      }),
    )
  })

  it('does not query or manually refresh an exact report task before reporting-read scope is ready', async () => {
    coladaState.queryDataById.delete(
      'getBusinessConsolePrincipalWorkContext:business.mes.reporting.read',
    )
    const detail = shallowRef({
      workOrderId: 'WO-501',
      operationTasks: [],
    })

    const exactTask = useMesExactOperationTask(
      shallowRef('WO-501'),
      shallowRef('OP-101'),
      detail as never,
    )
    const options = coladaState.queryFactoriesById.get('mes-report-exact-operation-task')?.()
    await exactTask.refresh()

    expect(options?.enabled).toBe(false)
    expect(coladaState.refetchById.get('mes-report-exact-operation-task')).not.toHaveBeenCalled()
    expect(listBusinessConsoleMesReportableOperationTasks).not.toHaveBeenCalled()
    expect(exactTask.reportingReadScopeReady.value).toBe(false)
    expect(exactTask.reportingReadScopeMessage.value).toContain('尚未选择已授权作业范围')
  })

  it('freezes exact task scope for every page of one query execution', async () => {
    let resolveFirstPage!: (value: unknown) => void
    vi.mocked(listBusinessConsoleMesReportableOperationTasks)
      .mockReturnValueOnce(
        new Promise((resolve) => {
          resolveFirstPage = resolve
        }) as never,
      )
      .mockResolvedValueOnce({
        data: {
          success: true,
          data: {
            items: [{ operationTaskId: 'OP-101', workOrderId: 'WO-501' }],
          },
        },
      } as never)
    const detail = shallowRef({
      workOrderId: 'WO-501',
      operationTasks: [],
    })

    useMesExactOperationTask(shallowRef('WO-501'), shallowRef('OP-101'), detail as never)
    const options = coladaState.queryFactoriesById.get('mes-report-exact-operation-task')?.()
    const resultPromise = options?.query?.({ signal: new AbortController().signal })
    reactiveAuthState.principal = {
      organizationId: 'org-002',
      environmentId: 'env-prod',
    }
    await nextTick()
    resolveFirstPage({
      data: {
        success: true,
        data: {
          items: Array.from({ length: 100 }, (_, index) => ({
            operationTaskId: `OP-${index + 1}`,
            workOrderId: 'WO-501',
          })),
        },
      },
    })
    await resultPromise

    expect(listBusinessConsoleMesReportableOperationTasks).toHaveBeenNthCalledWith(
      2,
      expect.objectContaining({
        query: expect.objectContaining({
          organizationId: 'org-001',
          environmentId: 'env-dev',
          skip: 100,
        }),
      }),
    )
  })

  it('queries current operation SOPs only after operation code is selected', () => {
    coladaState.queryDataById.set('getBusinessConsoleMesCurrentOperationSops', {
      success: true,
      data: {
        items: [
          { documentNumber: 'SOP-10', revision: 'A', operationCode: 'OP-10', fileId: 'file-10' },
        ],
      },
    })
    const sops = useMesCurrentOperationSops()

    expect(
      coladaState.queryOptionsById.get('getBusinessConsoleMesCurrentOperationSops')?.enabled,
    ).toBe(false)

    sops.filters.operationCode = ' OP-10 '
    sops.filters.workCenterCode = ' WC-10 '
    const options = coladaState.queryFactoriesById.get(
      'getBusinessConsoleMesCurrentOperationSops',
    )?.()

    expect(getBusinessConsoleMesCurrentOperationSopsQueryOptions).toHaveBeenLastCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        operationCode: 'OP-10',
        workCenterCode: 'WC-10',
      },
    })
    expect(options).toMatchObject({ enabled: true })
    expect(sops.currentSops.value[0]).toMatchObject({ fileId: 'file-10' })
  })
})
