import { flushPromises, mount } from '@vue/test-utils'
import { computed, reactive, shallowRef } from 'vue'
import { describe, expect, it, vi } from 'vitest'

import ErpPage from './index.vue'

const erpFilters = reactive<{
  purchaseOrderStatus?: string
  purchaseRequisitionStatus?: string
  keyword?: string
  skip: number
  take: number
}>({ purchaseOrderStatus: undefined, purchaseRequisitionStatus: undefined, keyword: undefined, skip: 0, take: 10 })

vi.mock('@/composables/usePagedList', () => ({
  usePagedList: () => ({
    page: shallowRef(1),
    pageSize: shallowRef('10'),
  }),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({
    query: {
      keyword: 'PR-001',
    },
  }),
}))

vi.mock('@/composables/useBusinessErp', () => ({
  useBusinessErp: () => ({
    filters: erpFilters,
    purchaseRequisitions: computed(() => [
      {
        requisitionNo: 'PR-001',
        requiredDate: '2026-07-03',
        quantity: 8,
        siteCode: 'SITE-01',
        skuCode: 'SKU-RM-001',
        status: 'Open',
        suggestionId: 'suggestion-001',
        uomCode: 'kg',
      },
    ]),
    purchaseRequisitionsPending: shallowRef(false),
    purchaseRequisitionsTotal: computed(() => 1),
    purchaseOrders: computed(() => [
      {
        lines: [
          { lineNo: 'LINE-001', orderedQuantity: 10, promisedDate: '2026-07-01', receivedQuantity: 2, skuCode: 'SKU-001', unitPrice: 12 },
          { lineNo: 'LINE-002', orderedQuantity: 5, promisedDate: '2026-07-02', receivedQuantity: 5, skuCode: 'SKU-002', unitPrice: 20 },
        ],
        purchaseOrderNo: 'PO-001',
        receiptReadiness: 'awaiting-arrival',
        siteCode: 'SITE-01',
        status: 'Released',
        supplierCode: 'SUP-001',
      },
    ]),
    purchaseOrdersPending: shallowRef(false),
    purchaseOrdersTotal: computed(() => 42),
    refreshProcurementDocuments: vi.fn(),
  }),
}))

const layoutStub = { BusinessLayout: { template: '<main><slot /></main>' } }

describe('ERP procurement page server-paged semantics', () => {
  it('initializes keyword from downstream purchase requisition route query', async () => {
    const wrapper = mount(ErpPage, { global: { stubs: { ...layoutStub } } })
    await flushPromises()

    expect(erpFilters.keyword).toBe('PR-001')
    expect(wrapper.text()).toContain('PR-001')
    expect(wrapper.text()).toContain('suggestion-001')
  })

  it('shows semantic supply KPIs and order-level pagination total', async () => {
    const wrapper = mount(ErpPage, { global: { stubs: { ...layoutStub } } })
    await flushPromises()

    expect(wrapper.text()).toContain('待到货明细')
    expect(wrapper.text()).toContain('部分收货')
    expect(wrapper.text()).toContain('未到数量')
    // 服务端分页：总数来自订单级 total，而非本页展开行数。
    expect(wrapper.html()).toContain('42')
  })

  it('renders order status separately from supply readiness via StatusBadge', async () => {
    const wrapper = mount(ErpPage, { global: { stubs: { ...layoutStub } } })
    await flushPromises()

    expect(wrapper.text()).toContain('已下达')
    expect(wrapper.text()).toContain('待到货')
    expect(wrapper.text()).toContain('PO-001')
  })

  it('exposes a refresh action and renders the line through DataTable', async () => {
    const wrapper = mount(ErpPage, { global: { stubs: { ...layoutStub } } })
    await flushPromises()

    const buttonTexts = wrapper.findAll('button').map((b) => b.text())
    expect(buttonTexts).toContain('刷新')
    expect(wrapper.text()).toContain('SKU-001')
  })

  it('renders independent status filters for requisitions and purchase orders', async () => {
    const wrapper = mount(ErpPage, { global: { stubs: { ...layoutStub } } })
    await flushPromises()

    expect(wrapper.text()).toContain('全部申请')
    expect(wrapper.text()).toContain('全部订单')
  })
})
