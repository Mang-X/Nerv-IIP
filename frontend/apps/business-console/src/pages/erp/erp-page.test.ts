import { flushPromises, mount } from '@vue/test-utils'
import { computed, reactive, shallowRef } from 'vue'
import { describe, expect, it, vi } from 'vitest'

import ErpPage from './index.vue'

vi.mock('@/composables/usePagedList', () => ({
  usePagedList: () => ({
    page: shallowRef(1),
    pageSize: shallowRef('10'),
  }),
}))

vi.mock('@/composables/useBusinessErp', () => ({
  useBusinessErp: () => ({
    filters: reactive({ status: undefined, keyword: undefined, skip: 0, take: 10 }),
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
    refreshPurchaseOrders: vi.fn(),
  }),
}))

const layoutStub = { BusinessLayout: { template: '<main><slot /></main>' } }

describe('ERP procurement page server-paged semantics', () => {
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
})
