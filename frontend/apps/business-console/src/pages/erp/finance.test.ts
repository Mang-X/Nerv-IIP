import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import ArApPage from './finance/ar-ap.vue'
import CostCandidatesPage from './finance/cost-candidates.vue'
import VouchersPage from './finance/vouchers.vue'

// 名录解析不是这些用例的被测对象；给稳定桩（解析不出名称→页面回退显编码），
// 避免真实实现去取业务上下文 store 而要求测试装 Pinia。
vi.mock('@/composables/useSkuNames', async () => {
  const { computed } = await import('vue')
  return {
    useSkuNames: () => ({
      resolveSkuName: () => undefined,
      resolveSkuLabel: (code?: string | null) => code ?? '未指定物料',
      skuByCode: computed(() => new Map<string, string>()),
      skusPending: computed(() => false),
    }),
  }
})
vi.mock('@/composables/useBusinessPartnerNames', async () => {
  const { computed } = await import('vue')
  return {
    useBusinessPartnerNames: () => ({
      resolvePartner: () => undefined,
      resolvePartnerLabel: (code?: string | null, fallback = '未指定') => code ?? fallback,
      partnerByCode: computed(() => new Map<string, string>()),
      partners: computed(() => []),
      partnersPending: computed(() => false),
    }),
  }
})
vi.mock('@/composables/useMasterDataDisplayNames', async () => {
  const { computed } = await import('vue')
  const emptyIndex = computed(() => new Map<string, string>())
  return {
    useMasterDataDisplayNames: () => ({
      resolveDevice: () => undefined,
      resolveLocation: () => undefined,
      resolveWorkCenter: () => undefined,
      resolveTeam: () => undefined,
      resolveUom: () => undefined,
      resolveWorkshop: () => undefined,
      resolveLine: () => undefined,
      formatUom: (code?: string | null, fallback = '') => code ?? fallback,
      deviceByCode: emptyIndex,
      locationByCode: emptyIndex,
      workCenterByCode: emptyIndex,
      teamByCode: emptyIndex,
      uomByCode: emptyIndex,
      workshopByCode: emptyIndex,
      lineByCode: emptyIndex,
    }),
  }
})

const state = vi.hoisted(() => ({
  receivables: [] as Array<Record<string, unknown>>,
  payables: [] as Array<Record<string, unknown>>,
  vouchers: [] as Array<Record<string, unknown>>,
  costCandidates: [] as Array<Record<string, unknown>>,
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
    ready: computed(() => true),
    refresh: vi.fn(),
  }
}

vi.mock('@/composables/useBusinessErp', () => ({
  useErpReceivables: () => ({
    ...listShape(() => state.receivables),
    createReceivable: vi.fn(),
    createReceivablePending: shallowRef(false),
    createReceivableError: shallowRef(undefined),
  }),
  useErpPayables: () => ({
    ...listShape(() => state.payables),
    createPayable: vi.fn(),
    createPayablePending: shallowRef(false),
    createPayableError: shallowRef(undefined),
  }),
  useErpJournalVouchers: () => ({
    ...listShape(() => state.vouchers),
    postVoucher: vi.fn(),
    postVoucherPending: shallowRef(false),
    postVoucherError: shallowRef(undefined),
  }),
  useErpCostCandidates: () => ({
    ...listShape(() => state.costCandidates),
    createCostCandidate: vi.fn(),
    createCostCandidatePending: shallowRef(false),
    createCostCandidateError: shallowRef(undefined),
  }),
}))

// 客户/供应商与来源单据改成只选：目录 composable 内部走 pinia + colada，测试整体打桩给定候选。
vi.mock('@/composables/useErpPickerCatalog', () => ({
  useErpPartnerCatalog: () => ({
    customerOptions: computed(() => [{ value: 'CUST-001', label: '示例客户' }]),
    supplierOptions: computed(() => [{ value: 'SUP-001', label: '示例供应商' }]),
    partnersPending: shallowRef(false),
  }),
  useErpReceivableSourceCatalog: () => ({
    receivableSourceOptions: computed(() => [{ value: 'SO-001', label: 'SO-001' }]),
    receivableSourcesPending: shallowRef(false),
  }),
  useErpPayableSourceCatalog: () => ({
    payableSourceOptions: computed(() => [{ value: 'PO-001', label: 'PO-001' }]),
    payableSourcesPending: shallowRef(false),
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

beforeEach(() => {
  state.receivables = []
  state.payables = []
  state.vouchers = []
  state.costCandidates = []
})

describe('ERP finance AR/AP page', () => {
  it('keeps AR/AP status filters aligned with backend open/settled values', async () => {
    const wrapper = mount(ArApPage, { global: { stubs: { ...layoutStub, ...selectStubs } } })
    await flushPromises()

    const selects = wrapper.findAll('select')
    expect(selects).toHaveLength(2)
    for (const select of selects) {
      expect(new Set(select.findAll('option').map((o) => o.attributes('value')))).toEqual(
        new Set(['all', 'open', 'settled']),
      )
    }
    expect(wrapper.text()).toContain('未结')
    expect(wrapper.text()).toContain('已结清')
  })
})

describe('ERP finance voucher and cost pages', () => {
  it('voucher page keeps keyword search and no status select', async () => {
    const wrapper = mount(VouchersPage, { global: { stubs: { ...layoutStub, ...selectStubs } } })
    await flushPromises()

    expect(wrapper.find('[aria-label="凭证关键字"]').exists()).toBe(true)
    expect(wrapper.findAll('select')).toHaveLength(0)
  })

  it('cost candidate list has no status select; dialog source type select is not a list filter', async () => {
    const wrapper = mount(CostCandidatesPage, {
      global: { stubs: { ...layoutStub, ...selectStubs } },
    })
    await flushPromises()

    expect(wrapper.find('[aria-label="成本候选关键字"]').exists()).toBe(true)
    const allSentinelSelects = wrapper
      .findAll('select')
      .filter((select) =>
        select.findAll('option').some((option) => option.attributes('value') === 'all'),
      )
    expect(allSentinelSelects).toHaveLength(0)
  })
})
