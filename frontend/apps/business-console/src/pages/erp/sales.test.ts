import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import DeliveriesPage from './sales/deliveries.vue'
import OrdersPage from './sales/orders.vue'
import QuotationsPage from './sales/quotations.vue'

vi.mock('@/composables/useOrderUrgency', () => ({
  useOrderUrgencies: () => ({ byReference: { value: new Map() } }),
}))
vi.mock('@/components/urgency/OrderUrgencyBadge.vue', () => ({
  default: { template: '<span data-testid="order-urgency">未计算</span>' },
}))

const state = vi.hoisted(() => ({
  quotations: [] as Array<Record<string, unknown>>,
  deliveries: [] as Array<Record<string, unknown>>,
  salesOrders: [] as Array<Record<string, unknown>>,
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: { keyword: 'SO-DEMO-001' } }),
}))

function listShape(itemsRef: () => Array<Record<string, unknown>>) {
  return {
    filters: reactive({
      status: undefined as string | undefined,
      keyword: undefined as string | undefined,
      skip: 0,
      take: 10,
    }),
    items: computed(() => itemsRef()),
    total: computed(() => itemsRef().length),
    organizationId: computed(() => 'org-001'),
    environmentId: computed(() => 'env-dev'),
    error: shallowRef(undefined),
    pending: shallowRef(false),
    refresh: vi.fn(),
  }
}

vi.mock('@/composables/useBusinessErp', () => ({
  useErpQuotations: () => ({
    ...listShape(() => state.quotations),
    approveQuotation: vi.fn(),
    approveQuotationPending: shallowRef(false),
    approveQuotationError: shallowRef(undefined),
    createQuotation: vi.fn(),
    createQuotationPending: shallowRef(false),
    createQuotationError: shallowRef(undefined),
  }),
  useErpSalesOrders: () => {
    const base = listShape(() => state.salesOrders)
    return {
      filters: base.filters,
      salesOrders: base.items,
      salesOrdersTotal: base.total,
      salesOrdersError: shallowRef(undefined),
      salesOrdersPending: shallowRef(false),
      refreshSalesOrders: vi.fn(),
      createSalesOrder: vi.fn(),
      createSalesOrderPending: shallowRef(false),
      createSalesOrderError: shallowRef(undefined),
    }
  },
  useErpDeliveryOrders: () => ({
    ...listShape(() => state.deliveries),
    releaseDeliveryOrder: vi.fn(),
    releaseDeliveryOrderPending: shallowRef(false),
    releaseDeliveryOrderError: shallowRef(undefined),
  }),
}))

vi.mock('@/composables/usePagedList', () => ({
  usePagedList: () => ({
    page: shallowRef(1),
    pageSize: shallowRef('10'),
    pageSizeNumber: shallowRef(10),
    resetPage: vi.fn(),
  }),
}))

const layoutStub = { BusinessLayout: { template: '<main><slot /></main>' } }
const selectStubs = {
  NvSelect: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template:
      '<select :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)"><slot /></select>',
  },
  NvSelectTrigger: { template: '<span><slot /></span>' },
  SelectValue: { template: '<span />' },
  NvSelectContent: { template: '<slot />' },
  NvSelectItem: { props: ['value'], template: '<option :value="value"><slot /></option>' },
}
const rowActionStubs = {
  RowActions: { template: '<div data-testid="row-actions"><slot /></div>' },
  DropdownMenuItem: {
    emits: ['click'],
    template: '<button type="button" @click="$emit(\'click\', $event)"><slot /></button>',
  },
}

beforeEach(() => {
  state.salesOrders = []
  state.deliveries = []
  state.quotations = []
})

describe('ERP sales quotation page', () => {
  it('keeps quotation status filter aligned with backend values', async () => {
    const wrapper = mount(QuotationsPage, { global: { stubs: { ...layoutStub, ...selectStubs } } })
    await flushPromises()

    const values = wrapper.findAll('option').map((o) => o.attributes('value'))
    expect(new Set(values)).toEqual(new Set(['all', 'Draft', 'Approved', 'Rejected', 'Expired']))
    expect(values).not.toContain('submitted')
    expect(wrapper.text()).toContain('待审')
    expect(wrapper.text()).not.toContain('待审批')
  })

  it('renders approve action only for Draft quotations and counts Draft KPI', async () => {
    state.quotations = [
      {
        quotationNo: 'QUO-DRAFT-1',
        customerCode: 'CUST-A',
        status: 'Draft',
        totalAmount: 1000,
        expiresOn: '2026-12-31',
      },
      {
        quotationNo: 'QUO-APPROVED-1',
        customerCode: 'CUST-B',
        status: 'Approved',
        totalAmount: 2000,
        expiresOn: '2026-12-31',
      },
    ]
    const wrapper = mount(QuotationsPage, {
      global: { stubs: { ...layoutStub, ...rowActionStubs } },
    })
    await flushPromises()

    expect(wrapper.findAll('[data-testid="row-actions"]')).toHaveLength(1)
    expect(wrapper.findAll('button').filter((b) => b.text().includes('审批通过'))).toHaveLength(1)
    expect(wrapper.text()).toMatch(/待审报价[^0-9]*1/)
  })
})

describe('ERP sales order and delivery pages', () => {
  it('sales orders keep keyword search and no status select', async () => {
    const wrapper = mount(OrdersPage, { global: { stubs: { ...layoutStub, ...selectStubs } } })
    await flushPromises()

    expect(wrapper.find('[aria-label="销售订单关键字"]').exists()).toBe(true)
    expect((wrapper.get('[aria-label="销售订单关键字"]').element as HTMLInputElement).value).toBe(
      'SO-DEMO-001',
    )
    expect(wrapper.findAll('select')).toHaveLength(0)
  })

  it('deliveries keep keyword search and no status select', async () => {
    const wrapper = mount(DeliveriesPage, { global: { stubs: { ...layoutStub, ...selectStubs } } })
    await flushPromises()

    expect(wrapper.find('[aria-label="发货关键字"]').exists()).toBe(true)
    expect(wrapper.findAll('select')).toHaveLength(0)
  })
})
