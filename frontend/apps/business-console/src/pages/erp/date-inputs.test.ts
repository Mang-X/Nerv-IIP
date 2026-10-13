import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { reactive, shallowRef } from 'vue'
import { i18n } from '@/i18n'
import Rfqs from './procurement/rfqs.vue'
import SupplierQuotations from './procurement/supplier-quotations.vue'
import PurchaseOrders from './procurement/purchase-orders.vue'
import Quotations from './sales/quotations.vue'
import Vouchers from './finance/vouchers.vue'

const state = vi.hoisted(() => ({ current: undefined as unknown, save: vi.fn() }))
vi.mock('@/composables/useBusinessErp', () => ({
  useErpRequestsForQuotation: () => state.current,
  useErpSupplierQuotations: () => state.current,
  useErpPurchaseOrders: () => state.current,
  useErpQuotations: () => state.current,
  useErpJournalVouchers: () => state.current,
}))
vi.mock('@/composables/useErpPickerCatalog', () => ({
  useErpPartnerCatalog: () => ({
    supplierOptions: shallowRef([]),
    customerOptions: shallowRef([]),
    partnersPending: shallowRef(false),
  }),
  useErpSiteCatalog: () => ({ siteOptions: shallowRef([]), sitesPending: shallowRef(false) }),
  useErpItemCatalog: () => ({
    uomOptions: shallowRef([]),
    uomsPending: shallowRef(false),
    baseUomBySku: shallowRef(new Map()),
  }),
}))
vi.mock('@/composables/useBusinessPartnerNames', () => ({
  useBusinessPartnerNames: () => ({
    resolvePartnerLabel: (code: string) => code,
    resolvePartner: (code: string) => code,
  }),
}))
vi.mock('@/composables/useSkuNames', () => ({
  useSkuNames: () => ({ resolveSkuName: (code: string) => code }),
}))
vi.mock('@/composables/usePagedList', () => ({
  usePagedList: () => ({ page: shallowRef(1), pageSize: shallowRef('10') }),
}))
vi.mock('vue-router', () => ({ useRoute: () => ({ query: {} }) }))
vi.mock('@/utils/notify', () => ({ notifySuccess: vi.fn(), notifyOperationFailure: vi.fn() }))

const input = {
  props: ['modelValue'],
  emits: ['update:modelValue'],
  template: `<input :value="modelValue" @input="$emit('update:modelValue', $event.target.value)" />`,
}
const stubs = {
  BusinessLayout: { template: '<main><slot /></main>' },
  NvDialog: { props: ['open'], template: '<section v-if="open"><slot /></section>' },
  NvDialogContent: { template: '<div><slot /></div>' },
  NvDialogHeader: { template: '<header><slot /></header>' },
  NvDialogTitle: { template: '<h2><slot /></h2>' },
  NvDialogDescription: { template: '<p><slot /></p>' },
  NvDialogFooter: { template: '<footer><slot /></footer>' },
  NvDialogClose: { template: '<span><slot /></span>' },
  NvDataTable: true,
  NvEntityPicker: input,
  DirectoryPicker: input,
  EntityMultiPicker: input,
  NvSelect: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template: `<select :value="modelValue" @change="$emit('update:modelValue', $event.target.value)"><slot /></select>`,
  },
  NvSelectTrigger: { template: '<span><slot /></span>' },
  NvSelectValue: true,
  NvSelectContent: { template: '<slot />' },
  NvSelectItem: { props: ['value'], template: '<option :value="value"><slot /></option>' },
}

const line = { lineNo: '10', skuCode: 'SKU-RM-001', uomCode: 'EA', quantity: 1 }
const cases = [
  {
    name: '询价需求日期',
    page: Rfqs,
    open: '新建 RFQ',
    fields: { 'erp-rfq-suppliers': 'SUP-001', 'erp-rfq-sku': 'SKU-RM-001', 'erp-rfq-uom': 'EA' },
    dates: ['erp-rfq-date'],
    payload: { supplierCodes: ['SUP-001'], lines: [{ ...line, requiredDate: '2026-06-19' }] },
  },
  {
    name: '采购报价承诺日期',
    page: SupplierQuotations,
    open: '录入报价',
    fields: {},
    dates: ['erp-sq-date'],
    payload: {
      rfqNo: 'RFQ-001',
      supplierCode: 'SUP-001',
      quotationNo: undefined,
      lines: [{ ...line, unitPrice: 0, promisedDate: '2026-06-19' }],
    },
  },
  {
    name: '采购订单承诺日期',
    page: PurchaseOrders,
    open: '新建采购单',
    fields: {
      'erp-po-supplier': 'SUP-001',
      'erp-po-site': 'SITE-01',
      'erp-po-sku': 'SKU-RM-001',
      'erp-po-uom': 'EA',
    },
    dates: ['erp-po-date'],
    payload: {
      supplierCode: 'SUP-001',
      siteCode: 'SITE-01',
      lines: [{ ...line, unitPrice: 0, promisedDate: '2026-06-19' }],
    },
  },
  {
    name: '销售报价有效期及需求日期',
    page: Quotations,
    open: '新建报价',
    fields: { 'erp-quo-customer': 'CUS-001', 'erp-quo-sku': 'SKU-RM-001', 'erp-quo-uom': 'EA' },
    dates: ['erp-quo-expires', 'erp-quo-required'],
    payload: {
      customerCode: 'CUS-001',
      expiresOn: '2026-06-19',
      lines: [{ ...line, unitPrice: 0, requiredDate: '2026-06-19' }],
    },
  },
  {
    name: '凭证过账日期',
    page: Vouchers,
    open: '过账凭证',
    fields: {
      'erp-jv-amount': '100',
      'erp-jv-debit': '1401',
      'erp-jv-credit': '2202',
      'erp-jv-memo': '采购材料入账',
    },
    dates: ['erp-jv-date'],
    payload: {
      postingDate: '2026-06-19',
      lines: [
        { accountCode: '1401', debitAmount: 100, creditAmount: 0, memo: '采购材料入账' },
        { accountCode: '2202', debitAmount: 0, creditAmount: 100, memo: '采购材料入账' },
      ],
    },
  },
]

// PublicContract: #3905 r1 / #4388。真实日期组件，目录与请求端使用页面既有挂载 seam。
async function chooseDate(trigger: { trigger: (event: string) => Promise<void> }) {
  await trigger.trigger('click')
  await flushPromises()
  const label = document.querySelector('[data-slot="popover-content"] .tabular-nums')!
  for (let i = 0; i < 120 && label.textContent?.trim() !== '2026 年 6 月'; i++) {
    const [year, month] = label.textContent!.match(/\d+/g)!.map(Number)
    const direction = year * 12 + month > 2026 * 12 + 6 ? '上个月' : '下个月'
    document.querySelector<HTMLButtonElement>(`[aria-label="${direction}"]`)!.click()
    await flushPromises()
  }
  expect(label.textContent?.trim()).toBe('2026 年 6 月')
  Array.from(document.querySelectorAll<HTMLButtonElement>('.nv-dp-cell'))
    .find((cell) => cell.textContent?.trim() === '19')!
    .click()
  await flushPromises()
}

beforeEach(() => {
  i18n.global.locale.value = 'zh-CN'
  state.save.mockReset().mockResolvedValue({})
  const data: Record<string, unknown> = {
    ready: shallowRef(true),
    pending: shallowRef(false),
    error: shallowRef(undefined),
    refresh: vi.fn(),
    filters: reactive({ skip: 0, take: 10 }),
    total: shallowRef(0),
    items: shallowRef([
      {
        rfqNo: 'RFQ-001',
        supplierCodes: ['SUP-001'],
        lines: [{ ...line, requiredDate: '2026-06-18' }],
      },
    ]),
  }
  for (const name of [
    'createRequestForQuotation',
    'receiveSupplierQuotation',
    'createPurchaseOrder',
    'createQuotation',
    'approveQuotation',
    'postVoucher',
  ]) {
    data[name] = state.save
    data[`${name}Pending`] = shallowRef(false)
    data[`${name}Error`] = shallowRef(undefined)
  }
  state.current = data
})
afterEach(() => {
  i18n.global.locale.value = 'zh-CN'
})

describe('ERP 日期输入', () => {
  it.each(cases)(
    '$name：本地化选择、清空必填校验及最终 ISO 提交',
    async ({ page, open, fields, dates, payload }) => {
      const wrapper = mount(page, { attachTo: document.body, global: { stubs } })
      await flushPromises()
      await wrapper
        .findAll('button')
        .find((button) => button.text().includes(open))!
        .trigger('click')
      if (page === SupplierQuotations) await wrapper.get('form select').setValue('RFQ-001')
      for (const [id, value] of Object.entries(fields)) await wrapper.get(`#${id}`).setValue(value)
      for (const id of dates) {
        expect(wrapper.find(`label[for="${id}"]`).exists()).toBe(true)
        await chooseDate(wrapper.get(`#${id}`))
        expect(wrapper.get(`#${id}`).text()).toBe('2026/6/19')
      }
      i18n.global.locale.value = 'en-US'
      await flushPromises()
      for (const id of dates) expect(wrapper.get(`#${id}`).text()).toBe('6/19/2026')
      for (const id of dates) {
        await wrapper
          .get(`#${id}`)
          .element.parentElement!.querySelector<HTMLButtonElement>('[aria-label="清除所选日期"]')!
          .click()
        await flushPromises()
        await wrapper.get('form').trigger('submit')
        expect(state.save).not.toHaveBeenCalled()
        expect(wrapper.get(`#${id}`).attributes('aria-invalid')).toBe('true')
        expect(wrapper.get(`#${id}`).element.parentElement!.className).toContain(
          'border-destructive',
        )
        await chooseDate(wrapper.get(`#${id}`))
        expect(wrapper.get(`#${id}`).attributes('aria-invalid')).toBeUndefined()
      }
      await wrapper.get('form').trigger('submit')
      await flushPromises()
      expect(state.save).toHaveBeenCalledExactlyOnceWith(payload)
    },
  )
})
