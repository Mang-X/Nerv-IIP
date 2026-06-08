import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import SalesPage from './sales.vue'

const erp = vi.hoisted(() => ({ createSalesOrder: vi.fn() }))

vi.mock('@nerv-iip/ui', async (orig) => ({
  ...(await orig<typeof import('@nerv-iip/ui')>()),
  toast: { success: vi.fn(), error: vi.fn() },
}))

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
  useErpSalesOrders: () => ({
    filters: reactive({ skip: 0, take: 10 }),
    salesOrders: computed(() => [
      { salesOrderNo: 'SO-1', customerCode: 'CUST-1', status: 'confirmed', totalAmount: 1234.5 },
    ]),
    salesOrdersError: shallowRef(undefined),
    salesOrdersPending: shallowRef(false),
    salesOrdersTotal: computed(() => 1),
    refreshSalesOrders: vi.fn(),
    createSalesOrder: erp.createSalesOrder,
    createSalesOrderPending: shallowRef(false),
    createSalesOrderError: shallowRef(undefined),
  }),
  useErpQuotations: () => listMock([
    { quotationNo: 'Q-1', customerCode: 'CUST-1', status: 'approved', expiresOn: '2026-07-01', totalAmount: 999 },
  ]),
  useErpOpportunities: () => listMock([
    { opportunityNo: 'OPP-1', customerCode: 'CUST-1', topic: '新产线扩产', status: 'open', openedAtUtc: '2026-06-01' },
  ]),
  useErpDeliveryOrders: () => listMock([
    { deliveryOrderNo: 'DO-1', salesOrderNo: 'SO-1', customerCode: 'CUST-1', status: 'released', releasedAtUtc: '2026-06-05' },
  ]),
}))

const layoutStub = { BusinessLayout: { template: '<main><slot /></main>' } }

function mountPage() {
  return mount(SalesPage, { global: { stubs: layoutStub } })
}

describe('ERP sales orders page', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    document.body.innerHTML = ''
    erp.createSalesOrder.mockResolvedValue(undefined)
  })

  it('renders sales orders with customer, status and amount', async () => {
    const wrapper = mountPage()
    await flushPromises()

    expect(wrapper.text()).toContain('SO-1')
    expect(wrapper.text()).toContain('CUST-1')
    expect(wrapper.text()).toContain('新建销售订单')
  })

  it('organises the sales funnel into 商机/报价/订单/发货 tabs', async () => {
    const wrapper = mountPage()
    await flushPromises()

    const tabLabels = wrapper.findAll('[role="tab"]').map((t) => t.text())
    expect(tabLabels).toEqual(expect.arrayContaining(['销售订单', '报价单', '商机', '发货单']))
  })

  it('creates a sales order from a quotation number', async () => {
    const wrapper = mountPage()
    await flushPromises()

    await wrapper.findAll('button').find((b) => b.text().includes('新建销售订单'))!.trigger('click')
    await flushPromises()

    const quotation = document.body.querySelector<HTMLInputElement>('#erp-so-quotation')!
    quotation.value = 'Q-9'
    quotation.dispatchEvent(new Event('input', { bubbles: true }))
    await flushPromises()

    document.body.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()

    expect(erp.createSalesOrder).toHaveBeenCalledWith({ quotationNo: 'Q-9', salesOrderNo: undefined })
  })

  it('blocks creation without a quotation number', async () => {
    const wrapper = mountPage()
    await flushPromises()

    await wrapper.findAll('button').find((b) => b.text().includes('新建销售订单'))!.trigger('click')
    await flushPromises()

    document.body.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()

    expect(erp.createSalesOrder).not.toHaveBeenCalled()
    expect(document.body.textContent).toContain('请输入报价单号')
  })
})
