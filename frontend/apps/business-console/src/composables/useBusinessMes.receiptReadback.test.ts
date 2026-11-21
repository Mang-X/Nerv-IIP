import {
  coladaState,
  materialReadinessFetch,
  setupBusinessMesTests,
} from './useBusinessMesTestFixture'
import { expect, it, vi } from 'vitest'
import { confirmBusinessConsoleMesLineSideMaterialReceiptMutationOptions } from '@nerv-iip/api-client'
import { useMesWorkOrderDetail } from './useBusinessMes'
import { describe } from 'vitest'

describe('business MES composables', () => {
  setupBusinessMesTests()

  it('收料受理后跨过 ReceiptPosting 再结束齐套自动读回，失败后重试只读取', async () => {
    const detail = useMesWorkOrderDetail()
    detail.filters.workOrderId = 'WO-RECEIPT'
    const issueData = coladaState.queryDataRefById.get(
      'listBusinessConsoleMesMaterialIssueRequests',
    )!
    const issueRefetch = coladaState.queryRefetchById.get(
      'listBusinessConsoleMesMaterialIssueRequests',
    )!
    issueRefetch.mockImplementation(async () => {
      issueData.value = {
        success: true,
        data: {
          items: [{ requestId: 'MIR-1', workOrderId: 'WO-RECEIPT', status: 'ReceiptPosting' }],
        },
      }
    })
    materialReadinessFetch.mockResolvedValue({
      success: true,
      data: { readinessStatus: 'Blocked' },
    })
    await detail.confirmLineSideReceipt('MIR-1', { receivedQuantity: 2 })
    const options = coladaState.queryFactoriesById.get('getBusinessConsoleMesMaterialReadiness')!()
    expect(detail.materialReadinessPending.value).toBe(true)
    await options.query!({ signal: new AbortController().signal })
    expect(options.autoRefetch!({ status: 'success' })).toBe(1000)
    expect(detail.materialReadinessPending.value).toBe(true)
    issueRefetch.mockImplementation(async () => {
      issueData.value = {
        success: true,
        data: {
          items: [{ requestId: 'MIR-1', workOrderId: 'WO-RECEIPT', status: 'PartiallyReceived' }],
        },
      }
    })
    materialReadinessFetch.mockRejectedValueOnce(new Error('读取齐套失败'))
    await expect(options.query!({ signal: new AbortController().signal })).rejects.toThrow(
      '读取齐套失败',
    )
    expect(options.autoRefetch!({ status: 'error' })).toBe(false)
    expect(detail.materialReadinessPending.value).toBe(true)
    expect(await options.query!({ signal: new AbortController().signal })).toMatchObject({
      data: { readinessStatus: 'Blocked' },
    })
    expect(detail.materialReadinessPending.value).toBe(false)
    expect(options.autoRefetch!({ status: 'success' })).toBe(false)
  })

  it('收料POST已受理后，齐套读取失败不把受理结果改为失败且重试不重复POST', async () => {
    const detail = useMesWorkOrderDetail()
    detail.filters.workOrderId = 'WO-RECEIPT'
    coladaState.invalidateQueries.mockImplementation(async (_options, refetchActive) => {
      if (refetchActive !== false) throw new Error('齐套GET失败')
    })
    const readinessRefetch = coladaState.queryRefetchById.get(
      'getBusinessConsoleMesMaterialReadiness',
    )!
    readinessRefetch.mockImplementation(async () => {
      coladaState.queryErrorRefById.get('getBusinessConsoleMesMaterialReadiness')!.value =
        new Error('齐套GET失败')
    })
    await expect(
      detail.confirmLineSideReceipt('MIR-1', { receivedQuantity: 2 }),
    ).resolves.toMatchObject({ success: true })
    expect(detail.materialReadinessError.value).toEqual(new Error('齐套GET失败'))
    await detail.refreshMaterialReadiness()
    expect(readinessRefetch).toHaveBeenCalledTimes(2)
    const options = vi.mocked(confirmBusinessConsoleMesLineSideMaterialReceiptMutationOptions).mock
      .results[0]!.value
    expect(options.mutation).toHaveBeenCalledTimes(1)
  })

  it('同工单A仍在过账时B先完成，继续跟踪直到A完成并读取最新齐套', async () => {
    const detail = useMesWorkOrderDetail()
    detail.filters.workOrderId = 'WO-RECEIPT'
    const issueData = coladaState.queryDataRefById.get(
      'listBusinessConsoleMesMaterialIssueRequests',
    )!
    const issueRefetch = coladaState.queryRefetchById.get(
      'listBusinessConsoleMesMaterialIssueRequests',
    )!
    let aStatus = 'ReceiptPosting'
    issueRefetch.mockImplementation(async () => {
      issueData.value = {
        success: true,
        data: {
          items: [
            { requestId: 'MIR-A', workOrderId: 'WO-RECEIPT', status: aStatus },
            { requestId: 'MIR-B', workOrderId: 'WO-RECEIPT', status: 'Received' },
          ],
        },
      }
    })
    materialReadinessFetch.mockResolvedValue({
      success: true,
      data: { readinessStatus: 'Blocked' },
    })
    await detail.confirmLineSideReceipt('MIR-A', { receivedQuantity: 2 })
    await detail.confirmLineSideReceipt('MIR-B', { receivedQuantity: 2 })
    const options = coladaState.queryFactoriesById.get('getBusinessConsoleMesMaterialReadiness')!()
    await options.query!({ signal: new AbortController().signal })
    expect(detail.materialReadinessPending.value).toBe(true)
    expect(options.autoRefetch!({ status: 'success' })).toBe(1000)
    aStatus = 'Received'
    materialReadinessFetch.mockResolvedValue({ success: true, data: { readinessStatus: 'Ready' } })
    expect(await options.query!({ signal: new AbortController().signal })).toMatchObject({
      data: { readinessStatus: 'Ready' },
    })
    expect(detail.materialReadinessPending.value).toBe(false)
    expect(options.autoRefetch!({ status: 'success' })).toBe(false)
  })
})
