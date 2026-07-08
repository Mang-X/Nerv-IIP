import { flushPromises, mount } from '@vue/test-utils'
import { computed, reactive, shallowRef } from 'vue'
import { describe, expect, it, vi } from 'vitest'

import ErpPage from './index.vue'

const filters = reactive<{ status?: string, keyword?: string, skip: number, take: number }>({ status: undefined, keyword: undefined, skip: 0, take: 10 })

vi.mock('@/composables/usePagedList', () => ({
  usePagedList: () => ({ page: shallowRef(1), pageSize: shallowRef('10') }),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: { keyword: 'PR-001' } }),
}))

vi.mock('@/composables/useBusinessErp', () => ({
  useErpPurchaseRequisitions: () => ({
    filters,
    items: computed(() => [
      { purchaseRequisitionId: 'pr-id-001', requisitionNo: 'PR-001', requiredDate: '2026-07-03', quantity: 8, siteCode: 'SITE-01', skuCode: 'SKU-RM-001', status: 'Open', suggestionId: 'suggestion-001', uomCode: 'kg' },
    ]),
    total: computed(() => 1),
    error: shallowRef(undefined),
    pending: shallowRef(false),
    refresh: vi.fn(),
    convertToPurchaseOrder: vi.fn(),
    convertToPurchaseOrderError: shallowRef(undefined),
    convertToPurchaseOrderPending: shallowRef(false),
  }),
}))

vi.mock('@/composables/useBusinessMasterData', () => ({
  useBusinessPartners: () => ({
    filters: reactive({ includeDisabled: undefined }),
    partners: computed(() => []),
  }),
}))

const layoutStub = { BusinessLayout: { template: '<main><slot /></main>' } }

describe('ERP purchase requisition page', () => {
  it('initializes keyword from downstream route query and renders real requisition rows', async () => {
    const wrapper = mount(ErpPage, { global: { stubs: { ...layoutStub } } })
    await flushPromises()

    expect(filters.keyword).toBe('PR-001')
    expect(wrapper.text()).toContain('PR-001')
    expect(wrapper.text()).toContain('suggestion-001')
  })

  it('renders requisition status filter and semantic KPIs', async () => {
    const wrapper = mount(ErpPage, { global: { stubs: { ...layoutStub } } })
    await flushPromises()

    expect(wrapper.text()).toContain('待处理申请')
    expect(wrapper.text()).toContain('已转单申请')
    expect(wrapper.text()).toContain('本页申请数量')
    expect(wrapper.text()).toContain('全部申请')
  })
})
