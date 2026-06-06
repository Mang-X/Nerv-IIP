import { mount } from '@vue/test-utils'
import { computed, reactive, shallowRef } from 'vue'
import { describe, expect, it, vi } from 'vitest'

import ErpPage from './index.vue'

vi.mock('@/composables/usePagedList', () => ({
  usePagedList: () => ({
    page: shallowRef(1),
    pageSize: shallowRef(10),
  }),
}))

vi.mock('@/composables/useBusinessErp', () => ({
  useBusinessErp: () => ({
    filters: reactive({
      environmentId: 'env-dev',
      organizationId: 'org-001',
      skip: 0,
      take: 10,
    }),
    purchaseOrders: computed(() => [
      {
        lines: [
          {
            lineNo: 'LINE-001',
            orderedQuantity: 10,
            promisedDate: '2026-07-01',
            receivedQuantity: 2,
            skuCode: 'SKU-001',
            unitPrice: 12,
          },
          {
            lineNo: 'LINE-002',
            orderedQuantity: 5,
            promisedDate: '2026-07-02',
            receivedQuantity: 5,
            skuCode: 'SKU-002',
            unitPrice: 20,
          },
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

vi.mock('@/layouts/BusinessLayout.vue', () => ({
  default: { template: '<main><slot /></main>' },
}))

vi.mock('@/components/business/BusinessPageHeader.vue', () => ({
  default: { props: ['title', 'summary'], template: '<header><h1>{{ title }}</h1><p>{{ summary }}</p></header>' },
}))

vi.mock('@nerv-iip/ui', () => ({
  Badge: { props: ['variant'], template: '<span data-badge><slot /></span>' },
  Button: { props: ['disabled', 'size', 'type', 'variant'], template: '<button v-bind="$attrs" :disabled="disabled" :type="type"><slot /></button>' },
  DataTablePagination: {
    props: ['page', 'pageSize', 'totalItems'],
    template: '<nav data-pagination :data-total-items="totalItems" />',
  },
  Field: { template: '<div><slot /></div>' },
  FieldGroup: { template: '<div><slot /></div>' },
  FieldLabel: { template: '<label><slot /></label>' },
  Input: { props: ['modelValue'], emits: ['update:modelValue'], template: '<input :value="modelValue" v-bind="$attrs" />' },
  Select: { props: ['modelValue'], template: '<div><slot /></div>' },
  SelectContent: { template: '<div><slot /></div>' },
  SelectItem: { props: ['value'], template: '<div><slot /></div>' },
  SelectTrigger: { props: ['id'], template: '<button :id="id"><slot /></button>' },
  SelectValue: { template: '<span />' },
  Spinner: { template: '<span />' },
  Table: { template: '<table><slot /></table>' },
  TableBody: { template: '<tbody><slot /></tbody>' },
  TableCell: { template: '<td><slot /></td>' },
  TableEmpty: { props: ['colspan'], template: '<tr><td :colspan="colspan"><slot /></td></tr>' },
  TableHead: { template: '<th><slot /></th>' },
  TableHeader: { template: '<thead><slot /></thead>' },
  TableRow: { template: '<tr><slot /></tr>' },
}))

vi.mock('lucide-vue-next', () => ({
  RefreshCwIcon: { template: '<span />' },
}))

function mountErpPage() {
  return mount(ErpPage)
}

describe('ERP procurement page server-paged semantics', () => {
  it('makes current-page metrics and order-level pagination explicit', () => {
    const wrapper = mountErpPage()

    expect(wrapper.text()).toContain('本页待到货明细')
    expect(wrapper.text()).toContain('本页部分收货')
    expect(wrapper.text()).toContain('本页未到数量')
    expect(wrapper.text()).toContain('按采购订单分页')
    expect(wrapper.get('[data-pagination]').attributes('data-total-items')).toBe('42')
  })

  it('shows order status separately from supply readiness', () => {
    const wrapper = mountErpPage()
    const badgeTexts = wrapper.findAll('[data-badge]').map((badge) => badge.text())

    expect(badgeTexts).toContain('已下达')
    expect(badgeTexts).toContain('待到货')
  })

  it('does not expose current-page-only column sorting as table actions', () => {
    const wrapper = mountErpPage()
    const buttonTexts = wrapper.findAll('button').map((button) => button.text())

    expect(buttonTexts).not.toContain('采购单')
    expect(buttonTexts).not.toContain('供应商')
    expect(buttonTexts).not.toContain('未到数量')
    expect(buttonTexts).toContain('刷新')
  })
})
