import { coladaState, setupBusinessMesTests } from './useBusinessMesTestFixture'
import { expect, it, vi } from 'vitest'
import {
  getBusinessConsoleMesBatchTraceabilityQueryOptions,
  getBusinessConsoleMesMaterialLotTraceabilityQueryOptions,
  getBusinessConsoleMesProductionReportQueryOptions,
  getBusinessConsoleMesWorkOrderTraceabilityQueryOptions,
  reverseBusinessConsoleMesProductionReportMutationOptions,
} from '@nerv-iip/api-client'
import { useMesProductionReports, useMesTraceability } from './useBusinessMes'
import { describe } from 'vitest'

describe('business MES composables', () => {
  setupBusinessMesTests()

  it('reverses a production report by reportNo and invalidates dependent MES lists', async () => {
    const { reverseProductionReport } = useMesProductionReports()

    await reverseProductionReport('PR-2001', { reason: '误报工', idempotencyKey: 'reverse-1' })

    expect(reverseBusinessConsoleMesProductionReportMutationOptions).toHaveBeenCalled()
    expect(
      vi.mocked(reverseBusinessConsoleMesProductionReportMutationOptions).mock.results[0]?.value
        .mutation,
    ).toHaveBeenCalledWith({
      path: { reportNo: 'PR-2001' },
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
      },
      body: { reason: '误报工', idempotencyKey: 'reverse-1' },
    })
    // 冲销回退工单累计并新增负向记录:本域 7 键(报工列表/工单详情/工单列表/概览/在制/工序任务/完工入库),
    // 不失效库存(冲销不发布库存过账事件)。
    expect(coladaState.invalidateQueries).toHaveBeenCalledTimes(7)
  })

  it('requests production report detail only while the reversal dialog has complete scope', () => {
    const reports = useMesProductionReports()

    expect(
      coladaState.queryFactoriesById.get('getBusinessConsoleMesProductionReport')?.(),
    ).toMatchObject({ enabled: false })

    reports.activateReverseDetail('PR-2001')
    coladaState.queryFactoriesById.get('getBusinessConsoleMesProductionReport')?.()
    expect(getBusinessConsoleMesProductionReportQueryOptions).toHaveBeenLastCalledWith({
      path: { reportNo: 'PR-2001' },
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
      },
    })
    expect(
      coladaState.queryFactoriesById.get('getBusinessConsoleMesProductionReport')?.(),
    ).toMatchObject({ enabled: true })

    reports.deactivateReverseDetail()
    expect(
      coladaState.queryFactoriesById.get('getBusinessConsoleMesProductionReport')?.(),
    ).toMatchObject({ enabled: false })
  })

  it('suppresses production report detail when organization or environment scope is empty', () => {
    const reports = useMesProductionReports()
    reports.activateReverseDetail('PR-2001')
    reports.filters.organizationId = ''

    expect(
      coladaState.queryFactoriesById.get('getBusinessConsoleMesProductionReport')?.(),
    ).toMatchObject({ enabled: false })
  })

  it('suppresses traceability queries when their required scope is empty', () => {
    const traceability = useMesTraceability()

    const workOrderOptions = coladaState.queryFactoriesById.get(
      'getBusinessConsoleMesWorkOrderTraceability',
    )?.()
    traceability.filters.mode = 'batch'
    const batchOptions = coladaState.queryFactoriesById.get(
      'getBusinessConsoleMesBatchTraceability',
    )?.()
    traceability.filters.mode = 'material-lot'
    const materialLotOptions = coladaState.queryFactoriesById.get(
      'getBusinessConsoleMesMaterialLotTraceability',
    )?.()

    expect(workOrderOptions).toMatchObject({ enabled: false })
    expect(batchOptions).toMatchObject({ enabled: false })
    expect(materialLotOptions).toMatchObject({ enabled: false })
    expect(getBusinessConsoleMesWorkOrderTraceabilityQueryOptions).not.toHaveBeenCalledWith(
      expect.objectContaining({ path: { workOrderId: 'WO-001' } }),
    )
    expect(getBusinessConsoleMesBatchTraceabilityQueryOptions).not.toHaveBeenCalledWith(
      expect.objectContaining({ path: { batchOrSerial: 'BATCH-001' } }),
    )
    expect(getBusinessConsoleMesMaterialLotTraceabilityQueryOptions).not.toHaveBeenCalledWith(
      expect.objectContaining({ path: { materialLotId: 'LOT-001' } }),
    )
  })
})
