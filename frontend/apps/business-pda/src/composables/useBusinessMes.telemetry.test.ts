import { coladaState, setupBusinessMesTests } from './useBusinessMesTestFixture'
import { expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'
import { listBusinessConsoleMesReportableOperationTasks } from '@nerv-iip/api-client'
import {
  useMesTelemetryCandidateTargetTasks,
  useMesTelemetryProductionReportCandidates,
} from './useBusinessMes'
import { describe } from 'vitest'

describe('pda useBusinessMes composables', () => {
  setupBusinessMesTests()

  it('遥测候选的目标工序：默认按候选设备收窄，输入工单号搜索时放开设备', async () => {
    vi.mocked(listBusinessConsoleMesReportableOperationTasks).mockResolvedValue({
      data: { success: true, data: { items: [{ operationTaskId: 'OP-1', workOrderId: 'WO-1' }] } },
    } as never)
    const context = shallowRef({
      principalId: 'user-001',
      organizationId: 'org-001',
      environmentId: 'env-dev',
      scopeKind: 'work-center',
      scopeId: 'WC-A',
      generation: 1,
    })
    const keyword = shallowRef('')
    useMesTelemetryCandidateTargetTasks(context, shallowRef('DEV-01'), keyword)
    const factory = coladaState.queryFactoriesById.get('mes-telemetry-candidate-target-tasks')!

    expect(await factory().query?.({ signal: new AbortController().signal })).toEqual([
      { operationTaskId: 'OP-1', workOrderId: 'WO-1' },
    ])
    expect(listBusinessConsoleMesReportableOperationTasks).toHaveBeenLastCalledWith(
      expect.objectContaining({
        query: expect.objectContaining({
          deviceAssetId: 'DEV-01',
          keyword: undefined,
          scopeKind: 'work-center',
          scopeId: 'WC-A',
        }),
      }),
    )

    keyword.value = 'WO-1'
    await factory().query?.({ signal: new AbortController().signal })
    expect(listBusinessConsoleMesReportableOperationTasks).toHaveBeenLastCalledWith(
      expect.objectContaining({
        query: expect.objectContaining({ deviceAssetId: undefined, keyword: 'WO-1' }),
      }),
    )
  })

  it('遥测候选转正 / 忽略：success=false 的信封按失败抛出，成功时返回报工单号', async () => {
    const queue = useMesTelemetryProductionReportCandidates()
    const promote = coladaState.mutateById.get(
      'promoteBusinessConsoleMesTelemetryProductionReportCandidate',
    )!
    const dismiss = coladaState.mutateById.get(
      'dismissBusinessConsoleMesTelemetryProductionReportCandidate',
    )!
    const rejected = { success: false, message: '工序已完工，不能再报工。', data: null }

    promote.mockResolvedValueOnce(rejected)
    await expect(queue.promote('cand-1', 'WO-1', 'OP-1')).rejects.toBe(rejected)
    dismiss.mockResolvedValueOnce(rejected)
    await expect(queue.dismiss('cand-1', '重复采集')).rejects.toBe(rejected)

    promote.mockResolvedValueOnce({ success: true, data: { reportNo: 'PRPT-1' } })
    await expect(queue.promote('cand-1', 'WO-1', 'OP-1')).resolves.toBe('PRPT-1')
    dismiss.mockResolvedValueOnce({ success: true, data: { accepted: true } })
    await expect(queue.dismiss('cand-1', '重复采集')).resolves.toBeUndefined()
  })
})
