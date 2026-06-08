import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import ErpPage from './index.vue'

function listMock<T>(items: T[]) {
  return {
    filters: reactive({ skip: 0, take: 10 }),
    items: computed(() => items),
    total: computed(() => items.length),
    error: shallowRef(undefined),
    pending: shallowRef(false),
    refresh: vi.fn(),
  }
}

vi.mock('@/composables/useBusinessErp', () => ({
  useBusinessErp: () => ({
    filters: reactive({ environmentId: 'env-dev', organizationId: 'org-001', skip: 0, take: 10 }),
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
  useErpRequestsForQuotation: () => listMock([
    { rfqNo: 'RFQ-1', supplierCodes: ['SUP-001', 'SUP-002'], status: 'open', createdAtUtc: '2026-06-02' },
  ]),
}))

const layoutStub = { BusinessLayout: { template: '<main><slot /></main>' } }

function mountErpPage() {
  return mount(ErpPage, { global: { stubs: layoutStub } })
}

describe('ERP procurement page', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    document.body.innerHTML = ''
  })

  it('makes current-page metrics and order-level pagination explicit', async () => {
    const wrapper = mountErpPage()
    await flushPromises()

    expect(wrapper.text()).toContain('本页待到货明细')
    expect(wrapper.text()).toContain('本页部分收货')
    expect(wrapper.text()).toContain('本页未到数量')
    expect(wrapper.text()).toContain('按采购订单分页')
    expect(wrapper.text()).toContain('PO-001')
  })

  it('separates 采购订单 and 询价单 into tabs', async () => {
    const wrapper = mountErpPage()
    await flushPromises()

    const tabLabels = wrapper.findAll('[role="tab"]').map((t) => t.text())
    expect(tabLabels).toEqual(expect.arrayContaining(['采购订单', '询价单']))
  })

  it('shows order status and supply readiness as status badges', async () => {
    const wrapper = mountErpPage()
    await flushPromises()

    expect(wrapper.text()).toContain('已下达')
    expect(wrapper.text()).toContain('待到货')
  })
})
