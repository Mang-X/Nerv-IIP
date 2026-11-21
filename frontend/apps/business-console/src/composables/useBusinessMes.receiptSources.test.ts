import { coladaState } from './useBusinessMesTestFixture'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import {
  getBusinessConsoleMesFinishedGoodsReceiptInventoryLinkQueryOptions,
  listBusinessConsoleMesFinishedGoodsReceiptRequestsQueryOptions,
  listBusinessConsoleMesHistoricalProducedLotsQueryOptions,
} from '@nerv-iip/api-client'
import {
  useMesReceiptInventorySource,
  useMesFinishedGoodsReceipts,
  useMesWorkOrderHistoricalProducedLots,
  useMesWorkOrderProducedLots,
} from './useBusinessMes'
import { useBusinessContextStore } from '@/stores/businessContext'

describe('MES receipt inventory source', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })
  it('首次登记列表请求已限定真实工单，不先读取全部登记', () => {
    useBusinessContextStore().patchContext({ organizationId: 'org-001', environmentId: 'env-dev' })
    useMesFinishedGoodsReceipts({ workOrderId: 'WO-001', requestNo: 'FGR-001' })
    expect(listBusinessConsoleMesFinishedGoodsReceiptRequestsQueryOptions).toHaveBeenLastCalledWith(
      {
        query: expect.objectContaining({ workOrderId: 'WO-001', keyword: 'FGR-001' }),
      },
    )
  })
  it('以真实 FGR 与工单及当前组织环境读取来源', () => {
    const context = useBusinessContextStore()
    context.patchContext({ organizationId: 'org-4414', environmentId: 'env-4414' })
    useMesReceiptInventorySource(() => ({ requestNo: 'FGR-001', workOrderId: 'WO-001' }))
    expect(
      getBusinessConsoleMesFinishedGoodsReceiptInventoryLinkQueryOptions,
    ).toHaveBeenLastCalledWith({
      path: { requestNo: 'FGR-001' },
      query: { organizationId: 'org-4414', environmentId: 'env-4414', workOrderId: 'WO-001' },
    })
  })
})
describe('历史产出与入库候选消费边界', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    useBusinessContextStore().patchContext({ organizationId: 'org-001', environmentId: 'env-dev' })
    coladaState.queryDataById.clear()
    coladaState.queryErrorById.clear()
    vi.mocked(listBusinessConsoleMesHistoricalProducedLotsQueryOptions).mockClear()
  })
  it.each([12, 5, 0])('剩余 %s 时追溯保留历史产出，入库仍读取候选端点', (remaining) => {
    const row = { producedLotNo: 'LOT-20261018-01', reportNo: 'PRPT-900', quantity: 12 }
    coladaState.queryDataById.set('listBusinessConsoleMesHistoricalProducedLots', {
      success: true,
      data: { items: [row] },
    })
    coladaState.queryDataById.set('listBusinessConsoleMesReceivableProducedLots', {
      success: true,
      data: { items: remaining ? [{ ...row, remainingQuantity: remaining }] : [] },
    })
    const history = useMesWorkOrderHistoricalProducedLots(() => ' WO-20261014-000001 ')
    const receipt = useMesWorkOrderProducedLots(() => 'WO-20261014-000001')
    expect(history.producedLots.value).toEqual([row])
    expect(history.producedLotsState.value).toBe('ready')
    expect(receipt.producedLots.value.map((lot) => lot.remainingQuantity)).toEqual(
      remaining ? [remaining] : [],
    )
    expect(listBusinessConsoleMesHistoricalProducedLotsQueryOptions).toHaveBeenCalledWith({
      path: { workOrderId: 'WO-20261014-000001' },
      query: { organizationId: 'org-001', environmentId: 'env-dev' },
    })
  })

  it('无工单不请求历史目录，失败时不暴露缓存目录', () => {
    const empty = useMesWorkOrderHistoricalProducedLots(() => '')
    expect(
      coladaState.queryFactoriesById.get('listBusinessConsoleMesHistoricalProducedLots')?.()
        .enabled,
    ).toBe(false)
    expect(empty.producedLotsState.value).toBe('idle')
    coladaState.queryDataById.set('listBusinessConsoleMesHistoricalProducedLots', {
      success: true,
      data: { items: [{ producedLotNo: 'LOT-OLD' }] },
    })
    coladaState.queryErrorById.set('listBusinessConsoleMesHistoricalProducedLots', { status: 403 })
    const denied = useMesWorkOrderHistoricalProducedLots(() => 'WO-20261014-000001')
    expect(denied.producedLotsState.value).toBe('error')
    expect(denied.producedLots.value).toEqual([])
    expect(denied.producedLotsError.value).toEqual({ status: 403 })
  })
})
