import {
  coladaState,
  lineSideInventoryFetch,
  reactiveAuthState,
  setupBusinessMesTests,
} from './useBusinessMesTestFixture'
import { expect, it, vi } from 'vitest'
import { nextTick } from 'vue'
import { listBusinessConsoleMesLineSideInventoryBalancesQueryOptions } from '@nerv-iip/api-client'
import { useMesLineSideInventoryBalances, useMesProductionMaterialLots } from '.'
import { describe } from 'vitest'

describe('pda useBusinessMes composables', () => {
  setupBusinessMesTests()

  // 状态字面量取运行时真实值（MES 域 MaterialIssueRequest 常量，#3912）。camelCase 的
  // received/partiallyReceived/inventoryPostingFailed 是旧展示层契约的产物、运行时不可达；
  // 这里按真实值摆数据，若 isAvailableMaterialLot 退回小写归一化，下面的负向断言会红。
  it('只把 Received 且仍有余量的批次提供给报工表单', () => {
    reactiveAuthState.principal = {
      ...reactiveAuthState.principal!,
      permissionCodes: ['business.mes.materials.read'],
    }
    coladaState.queryDataById.set('listBusinessConsoleMesMaterialIssueRequests', {
      success: true,
      data: {
        items: [
          {
            requestId: 'MIR-RECEIVED',
            materialId: 'MAT-1',
            materialLotId: 'LOT-1',
            receivedQuantity: 10,
            consumedQuantity: 2,
            status: 'Received',
          },
          {
            requestId: 'MIR-PARTIAL',
            materialId: 'MAT-2',
            materialLotId: 'LOT-2',
            receivedQuantity: 10,
            consumedQuantity: 2,
            status: 'PartiallyReceived',
          },
          {
            requestId: 'MIR-FAILED',
            materialId: 'MAT-3',
            materialLotId: 'LOT-3',
            receivedQuantity: 10,
            consumedQuantity: 2,
            status: 'InventoryPostingFailed',
          },
        ],
      },
    })

    const result = useMesProductionMaterialLots(() => ({
      workOrderId: 'WO-1',
      operationTaskId: 'OP-1',
    }))

    expect(result.availableMaterialLots.value.map((row) => row.requestId)).toEqual(['MIR-RECEIVED'])
  })

  it('首次请求身份正确但 payload 页码错误时失败关闭', () => {
    coladaState.queryDataById.set('listBusinessConsoleMesLineSideInventoryBalances', {
      success: true,
      data: {
        items: [
          { siteCode: 'SITE-A', locationCode: 'LINE-A', skuCode: 'SKU-WRONG', uomCode: 'pcs' },
        ],
        totalCount: 1,
        page: 2,
        pageSize: 200,
      },
    })
    const result = useMesLineSideInventoryBalances()

    expect(result.balances.value).toEqual([])
    expect(result.ready.value).toBe(false)
    expect(result.error.value).toEqual(
      expect.objectContaining({ message: '线边库存响应页码与请求不一致，请重试。' }),
    )
    expect(result.pending.value).toBe(false)
  })

  it('翻页后带当前请求身份的迟到错页 payload 不得投影', async () => {
    coladaState.queryDataById.set('listBusinessConsoleMesLineSideInventoryBalances', {
      success: true,
      data: { items: [], totalCount: 401, page: 1, pageSize: 200 },
    })
    lineSideInventoryFetch.mockResolvedValueOnce({
      data: {
        success: true,
        data: {
          items: [
            { siteCode: 'SITE-A', locationCode: 'LINE-A', skuCode: 'SKU-LATE', uomCode: 'pcs' },
          ],
          totalCount: 401,
          page: 1,
          pageSize: 200,
        },
      },
    })
    const result = useMesLineSideInventoryBalances()

    result.nextPage()
    const options = coladaState.queryFactoriesById.get(
      'listBusinessConsoleMesLineSideInventoryBalances',
    )?.()
    const lateWrongPage = await options?.query?.({ signal: new AbortController().signal })
    coladaState.queryDataRefById.get('listBusinessConsoleMesLineSideInventoryBalances')!.value =
      lateWrongPage
    await nextTick()

    expect(result.page.value).toBe(2)
    expect(result.balances.value).toEqual([])
    expect(result.ready.value).toBe(false)
    expect(result.error.value).toEqual(
      expect.objectContaining({ message: '线边库存响应页码与请求不一致，请重试。' }),
    )
    expect(result.pending.value).toBe(false)

    result.previousPage()
    expect(result.page.value).toBe(1)
  })

  it('切换组织环境时同页旧响应立即失效', async () => {
    coladaState.queryDataById.set('listBusinessConsoleMesLineSideInventoryBalances', {
      success: true,
      data: {
        items: [{ siteCode: 'SITE-A', locationCode: 'LINE-A', skuCode: 'SKU-OLD', uomCode: 'pcs' }],
        totalCount: 1,
        page: 1,
        pageSize: 200,
      },
    })
    const result = useMesLineSideInventoryBalances()
    await nextTick()

    expect(result.balances.value.map((item) => item.skuCode)).toEqual(['SKU-OLD'])

    reactiveAuthState.principal = { organizationId: 'org-002', environmentId: 'env-prod' }
    await nextTick()

    expect(result.page.value).toBe(1)
    expect(result.balances.value).toEqual([])
    expect(result.ready.value).toBe(false)
    coladaState.queryFactoriesById.get('listBusinessConsoleMesLineSideInventoryBalances')?.()
    expect(listBusinessConsoleMesLineSideInventoryBalancesQueryOptions).toHaveBeenLastCalledWith(
      expect.objectContaining({
        query: expect.objectContaining({
          organizationId: 'org-002',
          environmentId: 'env-prod',
          page: 1,
        }),
      }),
    )
  })

  it('分页请求报错后仍可退回上一页', async () => {
    coladaState.queryDataById.set('listBusinessConsoleMesLineSideInventoryBalances', {
      success: true,
      data: { items: [], totalCount: 401, page: 1, pageSize: 200 },
    })
    const result = useMesLineSideInventoryBalances()
    await nextTick()

    result.nextPage()
    expect(result.page.value).toBe(2)
    coladaState.queryErrorRefById.get('listBusinessConsoleMesLineSideInventoryBalances')!.value =
      new Error('第 2 页加载失败')

    expect(result.error.value).toEqual(expect.objectContaining({ message: '第 2 页加载失败' }))
    result.previousPage()
    expect(result.page.value).toBe(1)
  })

  it('总量收缩使当前页越界时回到最后有效页并忽略迟到旧页', async () => {
    coladaState.queryDataById.set('listBusinessConsoleMesLineSideInventoryBalances', {
      success: true,
      data: { items: [], totalCount: 401, page: 1, pageSize: 200 },
    })
    let resolveCorrectedPage!: (value: {
      data: {
        success: boolean
        data: {
          items: Array<{
            siteCode: string
            locationCode: string
            skuCode: string
            uomCode: string
          }>
          totalCount: number
          page: number
          pageSize: number
        }
      }
    }) => void
    const correctedPage = new Promise<Parameters<typeof resolveCorrectedPage>[0]>((resolve) => {
      resolveCorrectedPage = resolve
    })
    lineSideInventoryFetch
      .mockResolvedValueOnce({
        data: {
          success: true,
          data: { items: [], totalCount: 200, page: 2, pageSize: 200 },
        },
      })
      .mockImplementationOnce(() => correctedPage)
    const result = useMesLineSideInventoryBalances()
    await nextTick()

    result.nextPage()
    const pageTwoOptions = coladaState.queryFactoriesById.get(
      'listBusinessConsoleMesLineSideInventoryBalances',
    )?.()
    const shrunkenPageTwo = await pageTwoOptions?.query?.({
      signal: new AbortController().signal,
    })
    coladaState.queryDataRefById.get('listBusinessConsoleMesLineSideInventoryBalances')!.value =
      shrunkenPageTwo
    await nextTick()

    expect(result.page.value).toBe(1)
    expect(coladaState.invalidateQueries).toHaveBeenCalledWith(
      {
        exact: true,
        key: [
          { _id: 'listBusinessConsoleMesLineSideInventoryBalances', page: 1 },
          'scope-page:org-001:env-dev:page:1',
        ],
      },
      false,
    )
    expect(result.pending.value).toBe(true)
    expect(result.balances.value).toEqual([])

    const correctedOptions = coladaState.queryFactoriesById.get(
      'listBusinessConsoleMesLineSideInventoryBalances',
    )?.()
    const correctedRequest = correctedOptions?.query?.({ signal: new AbortController().signal })
    expect(lineSideInventoryFetch).toHaveBeenCalledTimes(2)
    expect(listBusinessConsoleMesLineSideInventoryBalancesQueryOptions).toHaveBeenLastCalledWith(
      expect.objectContaining({ query: expect.objectContaining({ page: 1, pageSize: 200 }) }),
    )

    coladaState.queryDataRefById.get('listBusinessConsoleMesLineSideInventoryBalances')!.value =
      shrunkenPageTwo
    await nextTick()
    expect(result.page.value).toBe(1)
    expect(result.balances.value).toEqual([])

    resolveCorrectedPage({
      data: {
        success: true,
        data: {
          items: [
            { siteCode: 'SITE-1', locationCode: 'LINE-1', skuCode: 'SKU-VALID', uomCode: 'pcs' },
          ],
          totalCount: 200,
          page: 1,
          pageSize: 200,
        },
      },
    })
    coladaState.queryDataRefById.get('listBusinessConsoleMesLineSideInventoryBalances')!.value =
      await correctedRequest
    await vi.waitFor(() => {
      expect(result.balances.value.map((item) => item.skuCode)).toEqual(['SKU-VALID'])
      expect(result.pending.value).toBe(false)
    })
  })

  it('校正期间切换组织环境后只投影新 scope 并解锁 pending', async () => {
    coladaState.queryDataById.set('listBusinessConsoleMesLineSideInventoryBalances', {
      success: true,
      data: { items: [], totalCount: 401, page: 1, pageSize: 200 },
    })
    lineSideInventoryFetch
      .mockResolvedValueOnce({
        data: { success: true, data: { items: [], totalCount: 200, page: 2, pageSize: 200 } },
      })
      .mockResolvedValueOnce({
        data: {
          success: true,
          data: {
            items: [
              { siteCode: 'SITE-B', locationCode: 'LINE-B', skuCode: 'SKU-NEW', uomCode: 'pcs' },
            ],
            totalCount: 1,
            page: 1,
            pageSize: 200,
          },
        },
      })
    const result = useMesLineSideInventoryBalances()
    await nextTick()

    result.nextPage()
    const pageTwoOptions = coladaState.queryFactoriesById.get(
      'listBusinessConsoleMesLineSideInventoryBalances',
    )?.()
    const oldScopePageTwo = await pageTwoOptions?.query?.({
      signal: new AbortController().signal,
    })
    coladaState.queryDataRefById.get('listBusinessConsoleMesLineSideInventoryBalances')!.value =
      oldScopePageTwo
    await nextTick()
    expect(result.pending.value).toBe(true)

    reactiveAuthState.principal = { organizationId: 'org-002', environmentId: 'env-prod' }
    await nextTick()
    const newScopeOptions = coladaState.queryFactoriesById.get(
      'listBusinessConsoleMesLineSideInventoryBalances',
    )?.()
    const newScopePageOne = await newScopeOptions?.query?.({
      signal: new AbortController().signal,
    })
    coladaState.queryDataRefById.get('listBusinessConsoleMesLineSideInventoryBalances')!.value =
      newScopePageOne
    await nextTick()

    expect(lineSideInventoryFetch).toHaveBeenCalledTimes(2)
    expect(result.balances.value.map((item) => item.skuCode)).toEqual(['SKU-NEW'])
    expect(result.pending.value).toBe(false)

    coladaState.queryDataRefById.get('listBusinessConsoleMesLineSideInventoryBalances')!.value =
      oldScopePageTwo
    await nextTick()
    expect(result.balances.value).toEqual([])
    expect(result.pending.value).toBe(false)
  })

  it('校正目标页请求失败后解锁 pending 并显示错误', async () => {
    coladaState.queryDataById.set('listBusinessConsoleMesLineSideInventoryBalances', {
      success: true,
      data: { items: [], totalCount: 401, page: 1, pageSize: 200 },
    })
    lineSideInventoryFetch
      .mockResolvedValueOnce({
        data: { success: true, data: { items: [], totalCount: 200, page: 2, pageSize: 200 } },
      })
      .mockRejectedValueOnce(new Error('校正页加载失败'))
    const result = useMesLineSideInventoryBalances()
    await nextTick()

    result.nextPage()
    const pageTwoOptions = coladaState.queryFactoriesById.get(
      'listBusinessConsoleMesLineSideInventoryBalances',
    )?.()
    const shrunkenPageTwo = await pageTwoOptions?.query?.({
      signal: new AbortController().signal,
    })
    coladaState.queryDataRefById.get('listBusinessConsoleMesLineSideInventoryBalances')!.value =
      shrunkenPageTwo
    await nextTick()
    expect(result.pending.value).toBe(true)

    const correctedOptions = coladaState.queryFactoriesById.get(
      'listBusinessConsoleMesLineSideInventoryBalances',
    )?.()
    await expect(
      correctedOptions?.query?.({ signal: new AbortController().signal }),
    ).rejects.toThrow('校正页加载失败')
    coladaState.queryErrorRefById.get('listBusinessConsoleMesLineSideInventoryBalances')!.value =
      new Error('校正页加载失败')
    await nextTick()

    expect(result.pending.value).toBe(false)
    expect(result.error.value).toEqual(expect.objectContaining({ message: '校正页加载失败' }))
    expect(result.page.value).toBe(1)
  })

  it('通过服务端分页访问第 201 条线边库存且把页码纳入请求身份', async () => {
    coladaState.queryDataById.set('listBusinessConsoleMesLineSideInventoryBalances', {
      success: true,
      data: {
        items: [{ siteCode: 'SITE-1', locationCode: 'LINE-1', skuCode: 'SKU-001', uomCode: 'pcs' }],
        totalCount: 401,
        page: 1,
        pageSize: 200,
      },
    })
    lineSideInventoryFetch
      .mockResolvedValueOnce({
        data: {
          success: true,
          data: {
            items: [
              { siteCode: 'SITE-1', locationCode: 'LINE-2', skuCode: 'SKU-201', uomCode: 'pcs' },
            ],
            totalCount: 401,
            page: 2,
            pageSize: 200,
          },
        },
      })
      .mockResolvedValueOnce({
        data: {
          success: true,
          data: {
            items: [
              { siteCode: 'SITE-1', locationCode: 'LINE-3', skuCode: 'SKU-401', uomCode: 'pcs' },
            ],
            totalCount: 401,
            page: 3,
            pageSize: 200,
          },
        },
      })

    const result = useMesLineSideInventoryBalances()
    const pageOneKey = coladaState.queryFactoriesById.get(
      'listBusinessConsoleMesLineSideInventoryBalances',
    )?.().key

    expect(result.page.value).toBe(1)
    expect(result.pageCount.value).toBe(3)
    expect(result.hasNextPage.value).toBe(true)

    result.nextPage()
    result.nextPage()
    expect(result.page.value).toBe(2)
    const options = coladaState.queryFactoriesById.get(
      'listBusinessConsoleMesLineSideInventoryBalances',
    )?.()
    expect(listBusinessConsoleMesLineSideInventoryBalancesQueryOptions).toHaveBeenLastCalledWith(
      expect.objectContaining({
        query: expect.objectContaining({ page: 2, pageSize: 200 }),
      }),
    )
    expect(options?.key).toEqual([
      { _id: 'listBusinessConsoleMesLineSideInventoryBalances', page: 2 },
      'scope-page:org-001:env-dev:page:2',
    ])
    expect(options?.key).not.toEqual(pageOneKey)
    const pageTwo = await options?.query?.({ signal: new AbortController().signal })
    coladaState.queryDataRefById.get('listBusinessConsoleMesLineSideInventoryBalances')!.value =
      pageTwo
    await nextTick()

    expect(result.balances.value.map((item) => item.skuCode)).toEqual(['SKU-201'])
    expect(result.hasPreviousPage.value).toBe(true)
    expect(result.hasNextPage.value).toBe(true)

    result.nextPage()
    const pageThreeOptions = coladaState.queryFactoriesById.get(
      'listBusinessConsoleMesLineSideInventoryBalances',
    )?.()
    const pageThree = await pageThreeOptions?.query?.({ signal: new AbortController().signal })
    coladaState.queryDataRefById.get('listBusinessConsoleMesLineSideInventoryBalances')!.value =
      pageThree
    await nextTick()
    expect(result.balances.value.map((item) => item.skuCode)).toEqual(['SKU-401'])
    expect(result.hasNextPage.value).toBe(false)

    result.previousPage()
    result.previousPage()
    expect(result.page.value).toBe(2)

    reactiveAuthState.principal = { organizationId: 'org-002', environmentId: 'env-prod' }
    await nextTick()
    expect(result.page.value).toBe(1)
  })

  it('把 HTTP 200 success:false 失败关闭且刷新重新执行公开 query', async () => {
    coladaState.queryDataById.set('listBusinessConsoleMesLineSideInventoryBalances', {
      success: false,
      message: '库存读模型暂不可用',
    })
    lineSideInventoryFetch
      .mockResolvedValueOnce({ data: { success: false, message: '刷新仍失败' } })
      .mockResolvedValueOnce({
        data: {
          success: true,
          data: {
            items: [
              { siteCode: 'SITE-1', locationCode: 'LINE-1', skuCode: 'SKU-OK', uomCode: 'pcs' },
            ],
            totalCount: 1,
            page: 1,
            pageSize: 200,
          },
        },
      })

    const result = useMesLineSideInventoryBalances()

    expect(result.error.value).toEqual(expect.objectContaining({ message: '库存读模型暂不可用' }))
    expect(result.balances.value).toEqual([])

    await result.refresh()
    expect(lineSideInventoryFetch).toHaveBeenCalledTimes(1)
    expect(result.error.value).toEqual(expect.objectContaining({ message: '刷新仍失败' }))

    await result.refresh()
    expect(lineSideInventoryFetch).toHaveBeenCalledTimes(2)
    expect(result.error.value).toBeNull()
    expect(result.balances.value.map((item) => item.skuCode)).toEqual(['SKU-OK'])
  })
})
