import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import PurchaseOrdersPage from './procurement/purchase-orders.vue'
import ReceiptsPage from './procurement/receipts.vue'

const state = vi.hoisted(() => ({
  purchaseOrders: [] as Array<Record<string, unknown>>,
  purchaseOrdersTotal: 42,
  receipts: [] as Array<Record<string, unknown>>,
  receiptsTotal: 7,
}))

const purchaseOrderFilters = reactive<{ status?: string, keyword?: string, skip: number, take: number }>({
  status: undefined,
  keyword: undefined,
  skip: 0,
  take: 10,
})

const receiptFilters = reactive<{ status?: string, keyword?: string, skip: number, take: number }>({
  status: undefined,
  keyword: undefined,
  skip: 0,
  take: 10,
})

vi.mock('@/composables/useBusinessErp', () => ({
  useErpPurchaseOrders: () => ({
    filters: purchaseOrderFilters,
    items: computed(() => state.purchaseOrders),
    total: computed(() => state.purchaseOrdersTotal),
    organizationId: computed(() => 'org-001'),
    environmentId: computed(() => 'env-dev'),
    error: shallowRef(undefined),
    pending: shallowRef(false),
    refresh: vi.fn(),
    createPurchaseOrder: vi.fn(),
    createPurchaseOrderPending: shallowRef(false),
    createPurchaseOrderError: shallowRef(undefined),
  }),
  useErpPurchaseReceipts: () => ({
    filters: receiptFilters,
    items: computed(() => state.receipts),
    total: computed(() => state.receiptsTotal),
    organizationId: computed(() => 'org-001'),
    environmentId: computed(() => 'env-dev'),
    error: shallowRef(undefined),
    pending: shallowRef(false),
    refresh: vi.fn(),
    recordPurchaseReceipt: vi.fn(),
    recordPurchaseReceiptPending: shallowRef(false),
    recordPurchaseReceiptError: shallowRef(undefined),
  }),
}))

vi.mock('@/composables/usePagedList', () => ({
  usePagedList: () => ({ page: shallowRef(1), pageSize: shallowRef('10'), pageSizeNumber: shallowRef(10), resetPage: vi.fn() }),
}))

const layoutStub = { BusinessLayout: { template: '<main><slot /></main>' } }
const selectStubs = {
  SelectPro: { props: ['modelValue'], emits: ['update:modelValue'], template: '<select :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)"><slot /></select>' },
  SelectProTrigger: { template: '<span><slot /></span>' },
  SelectProValue: { template: '<span />' },
  SelectValue: { template: '<span />' },
  SelectProContent: { template: '<slot />' },
  SelectProItem: { props: ['value'], template: '<option :value="value"><slot /></option>' },
}
const tableStub = {
  DataTablePro: {
    props: ['rows', 'totalItems'],
    template: `
      <section class="data-table" :data-total="totalItems">
        <article v-for="row in rows" :key="row.purchaseOrderNo + '-' + row.lineNo">
          <span>{{ row.purchaseOrderNo }}</span>
          <span>{{ row.lineNo }}</span>
          <span>{{ row.skuCode }}</span>
          <slot name="cell-openQuantity" :row="row">{{ row.openQuantity }}</slot>
        </article>
      </section>
    `,
  },
}

const globalStubs = { ...layoutStub, ...selectStubs, ...tableStub }

beforeEach(() => {
  purchaseOrderFilters.status = undefined
  purchaseOrderFilters.keyword = undefined
  purchaseOrderFilters.skip = 0
  purchaseOrderFilters.take = 10
  receiptFilters.status = undefined
  receiptFilters.keyword = undefined
  receiptFilters.skip = 0
  receiptFilters.take = 10
  state.purchaseOrdersTotal = 42
  state.receiptsTotal = 7
  state.purchaseOrders = [
    {
      purchaseOrderNo: 'PO-001',
      supplierCode: 'SUP-001',
      siteCode: 'SITE-01',
      status: 'Released',
      receiptReadiness: 'Partial',
      lines: [
        { lineNo: '10', skuCode: 'SKU-RM-001', orderedQuantity: 5, receivedQuantity: 2, unitPrice: 10 },
        { lineNo: '20', skuCode: 'SKU-RM-002', orderedQuantity: 2, receivedQuantity: 5, unitPrice: 6 },
      ],
    },
  ]
  state.receipts = [
    {
      purchaseOrderNo: 'PO-002',
      supplierCode: 'SUP-002',
      status: 'Released',
      receiptReadiness: 'Open',
      lines: [
        { lineNo: '10', skuCode: 'SKU-RM-003', orderedQuantity: 8, receivedQuantity: 3 },
        { lineNo: '20', skuCode: 'SKU-RM-004', orderedQuantity: 1, receivedQuantity: 4 },
      ],
    },
  ]
})

describe('ERP procurement purchase order page', () => {
  it('restores purchase order status filtering with backend status values', async () => {
    const wrapper = mount(PurchaseOrdersPage, { global: { stubs: globalStubs } })
    await flushPromises()

    const select = wrapper.get('select')
    expect(new Set(select.findAll('option').map((o) => o.attributes('value')))).toEqual(new Set(['all', 'Released', 'Closed', 'Cancelled']))

    await select.setValue('Released')
    expect(purchaseOrderFilters.status).toBe('Released')

    await select.setValue('all')
    expect(purchaseOrderFilters.status).toBeUndefined()
  })

  it('keeps order-level total while rendering line rows and clamps open quantity at zero', async () => {
    const wrapper = mount(PurchaseOrdersPage, { global: { stubs: globalStubs } })
    await flushPromises()

    expect(wrapper.get('.data-table').attributes('data-total')).toBe('42')
    expect(wrapper.text()).toContain('PO-001')
    expect(wrapper.text()).toContain('SKU-RM-001')
    expect(wrapper.text()).toContain('3')
    expect(wrapper.text()).not.toContain('-3')
  })
})

describe('ERP procurement receipt page', () => {
  it('keeps purchase-order-source total while rendering receivable lines and clamps open quantity at zero', async () => {
    const wrapper = mount(ReceiptsPage, { global: { stubs: globalStubs } })
    await flushPromises()

    expect(wrapper.get('.data-table').attributes('data-total')).toBe('7')
    expect(wrapper.text()).toContain('PO-002')
    expect(wrapper.text()).toContain('SKU-RM-003')
    expect(wrapper.text()).toContain('5')
    expect(wrapper.text()).not.toContain('-3')
  })
})
