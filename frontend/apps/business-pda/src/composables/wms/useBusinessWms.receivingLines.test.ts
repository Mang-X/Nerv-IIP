import { coladaState, setupBusinessWmsTests } from './useBusinessWmsTestFixture'
import { expect, it, vi } from 'vitest'
import { nextTick, shallowRef } from 'vue'
import { listBusinessConsoleWmsReceivingQualityGatesQueryOptions } from '@nerv-iip/api-client'
import { useWmsReceivingLines } from '.'
import { describe } from 'vitest'

describe('PDA WMS composables', () => {
  setupBusinessWmsTests()

  it('binds receiving detail lines to both principal scope and selected inbound order', async () => {
    coladaState.queryDataById.set('listBusinessConsoleWmsReceivingQualityGates', {
      success: true,
      data: {
        items: [{ inboundOrderLineId: 'line-old', inboundOrderNo: 'IN-1' }],
        total: 1,
      },
    })
    const orderNo = shallowRef('IN-1')
    const scopeKind = shallowRef<string | undefined>('self')
    const scopeId = shallowRef<string | undefined>('emp049')
    const result = useWmsReceivingLines(orderNo, { scopeKind, scopeId })

    expect(result.lines.value).toHaveLength(1)
    expect(result.hasSuccessfulResponse.value).toBe(true)
    expect(
      vi.mocked(listBusinessConsoleWmsReceivingQualityGatesQueryOptions).mock.calls.at(-1)?.[0],
    ).toEqual({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        scopeKind: 'self',
        scopeId: 'emp049',
        skip: 0,
        take: 500,
        inboundOrderNo: 'IN-1',
        includeNotRequired: true,
      },
    })

    scopeId.value = 'POOL-RECEIVING'
    await nextTick()
    expect(result.lines.value).toEqual([])
    expect(result.total.value).toBe(0)
    expect(result.hasSuccessfulResponse.value).toBe(false)

    orderNo.value = 'IN-2'
    await nextTick()
    expect(result.lines.value).toEqual([])
    expect(result.total.value).toBe(0)
    expect(result.hasSuccessfulResponse.value).toBe(false)
    expect(result.hasFailedResponse.value).toBe(false)

    coladaState.queryDataRefById.get('listBusinessConsoleWmsReceivingQualityGates')!.value = {
      success: false,
      message: '收货明细查询失败',
    }
    await nextTick()
    expect(result.hasSuccessfulResponse.value).toBe(false)
    expect(result.hasFailedResponse.value).toBe(true)
  })
})
