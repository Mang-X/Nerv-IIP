import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import PurchaseRequisitionsPage from './index.vue'
import PurchaseOrdersPage from './procurement/purchase-orders.vue'
import ReceiptsPage from './procurement/receipts.vue'

const state = vi.hoisted(() => ({
  purchaseOrders: [] as Array<Record<string, unknown>>,
  purchaseOrdersTotal: 42,
  purchaseRequisitions: [] as Array<Record<string, unknown>>,
  purchaseRequisitionsTotal: 9,
  receipts: [] as Array<Record<string, unknown>>,
  receiptsTotal: 7,
  convertPurchaseRequisition: vi.fn(),
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
  useErpPurchaseRequisitions: () => ({
    filters: purchaseOrderFilters,
    items: computed(() => state.purchaseRequisitions),
    total: computed(() => state.purchaseRequisitionsTotal),
    organizationId: computed(() => 'org-001'),
    environmentId: computed(() => 'env-dev'),
    error: shallowRef(undefined),
    pending: shallowRef(false),
    refresh: vi.fn(),
    convertToPurchaseOrder: state.convertPurchaseRequisition,
    convertToPurchaseOrderPending: shallowRef(false),
    convertToPurchaseOrderError: shallowRef(undefined),
  }),
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

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {} }),
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
        <article v-for="row in rows" :key="(row.purchaseOrderNo || row.requisitionNo) + '-' + (row.lineNo || row.purchaseRequisitionId)">
          <span>{{ row.purchaseOrderNo || row.requisitionNo }}</span>
          <span>{{ row.lineNo }}</span>
          <span>{{ row.skuCode }}</span>
          <slot name="cell-actions" :row="row" />
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
  state.purchaseRequisitionsTotal = 9
  state.receiptsTotal = 7
  state.convertPurchaseRequisition.mockReset()
  state.convertPurchaseRequisition.mockResolvedValue({ success: true, data: { status: 'PurchaseOrderCreated', purchaseOrderNo: 'PO-REQ-001' } })
  state.purchaseRequisitions = [
    {
      purchaseRequisitionId: 'pr-001',
      requisitionNo: 'PR-001',
      suggestionId: 'suggestion-001',
      skuCode: 'SKU-RM-001',
      uomCode: 'EA',
      siteCode: 'SITE-01',
      quantity: 5,
      status: 'Open',
    },
    {
      purchaseRequisitionId: 'pr-002',
      requisitionNo: 'PR-002',
      suggestionId: 'suggestion-002',
      skuCode: 'SKU-RM-002',
      uomCode: 'EA',
      siteCode: 'SITE-01',
      quantity: 3,
      status: 'Converted',
      convertedPurchaseOrderNo: 'PO-OLD-001',
    },
  ]
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

describe('ERP procurement purchase requisition page', () => {
  it('offers conversion only for open requisitions and calls the conversion action once', async () => {
    const wrapper = mount(PurchaseRequisitionsPage, { global: { stubs: globalStubs } })
    await flushPromises()

    expect(wrapper.get('.data-table').attributes('data-total')).toBe('9')
    expect(wrapper.text()).toContain('PR-001')
    expect(wrapper.text()).toContain('PR-002')
    const convertButtons = wrapper.findAll('button').filter((button) => button.text().includes('转采购订单'))
    expect(convertButtons).toHaveLength(1)

    await convertButtons[0]!.trigger('click')
    await flushPromises()

    expect(state.convertPurchaseRequisition).toHaveBeenCalledWith(['PR-001'])
  })
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
