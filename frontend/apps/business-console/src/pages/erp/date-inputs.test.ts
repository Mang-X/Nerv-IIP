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
vi.mock('@/composables/erp/useBusinessErp', () => ({
  useErpRequestsForQuotation: () => state.current,
  useErpSupplierQuotations: () => state.current,
  useErpPurchaseOrders: () => state.current,
  useErpQuotations: () => state.current,
  useErpJournalVouchers: () => state.current,
}))
vi.mock('@/composables/erp/useErpPickerCatalog', () => ({
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
vi.mock('@/composables/master-data/useBusinessPartnerNames', () => ({
  useBusinessPartnerNames: () => ({
    resolvePartnerLabel: (code: string) => code,
    resolvePartner: (code: string) => code,
  }),
}))
vi.mock('@/composables/master-data/useSkuNames', () => ({
  useSkuNames: () => ({ resolveSkuName: (code: string) => code }),
}))
vi.mock('@/composables/shared/usePagedList', () => ({
  usePagedList: () => ({ page: shallowRef(1), pageSize: shallowRef('10') }),
}))
vi.mock('vue-router', () => ({
  RouterLink: { template: '<a><slot /></a>' },
  useRoute: () => ({ query: {} }),
}))
vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({
    principal: { principalId: 'buyer', permissionCodes: ['business.erp.procurement.manage'] },
  }),
}))
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
  NvEntityPicker: {
    ...input,
    props: ['modelValue', 'invalid'],
    template: `<input :value="modelValue" :aria-invalid="invalid || undefined" @input="$emit('update:modelValue', $event.target.value)" />`,
  },
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
    fields: {
      'erp-rfq-suppliers': 'SUP-001',
      'erp-rfq-sku-2': 'SKU-RM-001',
      'erp-rfq-uom-2': 'EA',
    },
    dates: ['erp-rfq-date-2'],
    payload: {
      supplierCodes: ['SUP-001'],
      lines: [{ ...line, siteCode: 'SITE-01', requiredDate: '2026-06-19' }],
    },
  },
  {
    name: '采购报价承诺日期',
    page: SupplierQuotations,
    open: '录入报价',
    fields: { 'erp-sq-price': '10' },
    dates: ['erp-sq-date'],
    payload: {
      rfqNo: 'RFQ-001',
      supplierCode: 'SUP-001',
      quotationNo: undefined,
      lines: [{ ...line, unitPrice: 10, promisedDate: '2026-06-19' }],
    },
  },
  {
    name: '采购订单承诺日期',
    page: PurchaseOrders,
    open: '新建采购单',
    fields: {
      'erp-po-supplier': 'SUP-001',
      'erp-po-site': 'SITE-01',
      'erp-po-sku-2': 'SKU-RM-001',
      'erp-po-uom-2': 'EA',
      'erp-po-price-2': '10',
    },
    dates: ['erp-po-date-2'],
    payload: {
      supplierCode: 'SUP-001',
      siteCode: 'SITE-01',
      lines: [{ ...line, unitPrice: 10, promisedDate: '2026-06-19' }],
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
    organizationId: shallowRef('org'),
    environmentId: shallowRef('env'),
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
      if (page === Rfqs) {
        await wrapper.get('form').trigger('submit')
        expect(state.save).not.toHaveBeenCalled()
        expect(wrapper.get('fieldset [role="alert"]').text()).toContain('第 1 行')
        expect(wrapper.get('#erp-rfq-site-2').attributes('aria-invalid')).toBe('true')
        await wrapper.get('#erp-rfq-site-2').setValue('SITE-01')
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

it.each([
  { page: Rfqs, action: '新建 RFQ', prefix: 'rfq', dateField: 'requiredDate' },
  { page: PurchaseOrders, action: '新建采购单', prefix: 'po', dateField: 'promisedDate' },
])(
  '$action：第二行缺项拦截整单，修正后一次提交，失败保留两行',
  async ({ page, action, prefix, dateField }) => {
    const wrapper = mount(page, { attachTo: document.body, global: { stubs } })
    const button = (label: string) =>
      wrapper.findAll('button').find((candidate) => candidate.text() === label)!
    await button(action).trigger('click')
    await button('添加明细').trigger('click')
    if (page === Rfqs) {
      await wrapper.get('#erp-rfq-suppliers').setValue('SUP-001')
      await wrapper.get('#erp-rfq-batch-site').setValue('SITE-01')
      await button('应用到全部明细').trigger('click')
    } else {
      await wrapper.get('#erp-po-supplier').setValue('SUP-001')
      await wrapper.get('#erp-po-site').setValue('SITE-01')
    }
    for (const id of [2, 3]) {
      await wrapper.get(`#erp-${prefix}-sku-${id}`).setValue(`SKU-${id}`)
      if (page === PurchaseOrders) await wrapper.get(`#erp-po-price-${id}`).setValue('10')
      await chooseDate(wrapper.get(`#erp-${prefix}-date-${id}`))
    }
    await wrapper.get(`#erp-${prefix}-uom-2`).setValue('EA')
    await wrapper.get('form').trigger('submit')
    expect(wrapper.get('fieldset [role="alert"]').text()).toContain('第 2 行')
    expect(state.save).not.toHaveBeenCalled()

    if (page === PurchaseOrders) expect(wrapper.text()).toContain('合计 ¥20.00')
    await wrapper.get(`#erp-${prefix}-uom-3`).setValue('EA')
    state.save.mockRejectedValueOnce(new Error('请求失败'))
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(state.save).toHaveBeenCalledTimes(1)
    expect(state.save.mock.calls[0]?.[0]).toEqual(
      expect.objectContaining({
        lines: [2, 3].map((id) => ({
          lineNo: String((id - 1) * 10),
          skuCode: `SKU-${id}`,
          uomCode: 'EA',
          quantity: 1,
          [dateField]: '2026-06-19',
          ...(page === Rfqs ? { siteCode: 'SITE-01' } : { unitPrice: 10 }),
        })),
      }),
    )
    expect(wrapper.findAll('fieldset')).toHaveLength(2)
    await wrapper.get('[aria-label="删除第 2 行"]').trigger('click')
    expect(wrapper.findAll('fieldset')).toHaveLength(1)
    wrapper.unmount()
  },
)

it('供应商报价必须明确选择多行询价中的具体行，并提交所选行的物料和数量', async () => {
  const data = state.current as { items: { value: unknown[] } }
  data.items.value = [
    {
      rfqNo: 'RFQ-001',
      supplierCodes: ['SUP-001'],
      lines: [
        { ...line, requiredDate: '2026-06-18' },
        { ...line, lineNo: '20', skuCode: 'SKU-SECOND', quantity: 3, requiredDate: '2026-06-20' },
      ],
    },
  ]
  const wrapper = mount(SupplierQuotations, { global: { stubs } })
  await wrapper
    .findAll('button')
    .find((button) => button.text() === '录入报价')!
    .trigger('click')
  await wrapper.get('form select').setValue('RFQ-001')
  await wrapper.get('form').trigger('submit')
  expect(state.save).not.toHaveBeenCalled()
  await wrapper.findAll('form select')[1]!.setValue('20')
  expect(wrapper.text()).toContain('SKU-SECOND')
  await wrapper.get('#erp-sq-price').setValue('0')
  await wrapper.get('form').trigger('submit')
  expect(wrapper.text()).toContain('单价必须大于 0。')
  expect(state.save).not.toHaveBeenCalled()
  await wrapper.get('#erp-sq-price').setValue('10')
  await wrapper.get('form').trigger('submit')
  await flushPromises()
  expect(state.save).toHaveBeenCalledWith(
    expect.objectContaining({
      lines: [
        {
          ...line,
          lineNo: '20',
          skuCode: 'SKU-SECOND',
          quantity: 3,
          unitPrice: 10,
          promisedDate: '2026-06-20',
        },
      ],
    }),
  )
})
