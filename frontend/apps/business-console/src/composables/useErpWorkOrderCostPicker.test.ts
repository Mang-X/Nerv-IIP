import { listBusinessConsoleErpWorkOrderCostsQueryOptions } from '@nerv-iip/api-client'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useErpWorkOrderCostPicker } from './useErpWorkOrderCostPicker'

const colada = vi.hoisted(() => ({
  factory: undefined as undefined | (() => unknown),
  data: undefined as unknown,
}))

vi.mock('@nerv-iip/api-client', () => ({
  listBusinessConsoleErpWorkOrderCostsQueryOptions: vi.fn(() => ({ key: [], query: vi.fn() })),
}))

vi.mock('@pinia/colada', () => ({
  useQuery: vi.fn((optionsFactory: () => unknown) => {
    colada.factory = optionsFactory
    optionsFactory()
    return { data: shallowRef(colada.data), isLoading: shallowRef(false) }
  }),
}))

// PublicContract: #3783 财务页工单候选取自 ERP 工单成本，服务端按关键字搜索。
describe('ERP work-order cost picker', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    colada.data = undefined
    vi.useFakeTimers()
    useBusinessContextStore().patchContext({ organizationId: 'org-001', environmentId: 'env-dev' })
  })
  afterEach(() => {
    vi.useRealTimers()
  })

  it('searches ERP work-order costs by keyword inside the business scope', async () => {
    const picker = useErpWorkOrderCostPicker('')

    picker.search.value = ' 0186 '
    await vi.advanceTimersByTimeAsync(300)
    colada.factory!()

    expect(listBusinessConsoleErpWorkOrderCostsQueryOptions).toHaveBeenLastCalledWith({
      query: { organizationId: 'org-001', environmentId: 'env-dev', take: 50, keyword: '0186' },
    })
  })

  it('offers work orders with their SKU and keeps a selection outside the current page', () => {
    colada.data = {
      success: true,
      data: {
        items: [
          { workOrderId: 'WO-0186', skuCode: 'FG-01', costKind: 'ordinary' },
          { workOrderId: 'WO-RW-0007', skuCode: 'FG-01', costKind: 'rework' },
        ],
        total: 120,
      },
    }

    const picker = useErpWorkOrderCostPicker('WO-0999')

    expect(picker.options.value).toEqual([
      { value: 'WO-0999', label: 'WO-0999' },
      { value: 'WO-0186', label: 'WO-0186', hint: 'FG-01' },
      { value: 'WO-RW-0007', label: 'WO-RW-0007', hint: 'FG-01 · 返工' },
    ])
    expect(picker.total.value).toBe(120)
  })
})
