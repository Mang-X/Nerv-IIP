import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import ArApPage from './finance/ar-ap.vue'
import CostCandidatesPage from './finance/cost-candidates.vue'
import VouchersPage from './finance/vouchers.vue'

const state = vi.hoisted(() => ({
  receivables: [] as Array<Record<string, unknown>>,
  payables: [] as Array<Record<string, unknown>>,
  vouchers: [] as Array<Record<string, unknown>>,
  costCandidates: [] as Array<Record<string, unknown>>,
}))

function listShape(itemsRef: () => Array<Record<string, unknown>>) {
  return {
    filters: reactive({ status: undefined as string | undefined, keyword: undefined as string | undefined, skip: 0, take: 10 }),
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

vi.mock('@/composables/usePagedList', () => ({
  usePagedList: () => ({ page: shallowRef(1), pageSize: shallowRef('10'), pageSizeNumber: shallowRef(10), resetPage: vi.fn() }),
}))

const layoutStub = { BusinessLayout: { template: '<main><slot /></main>' } }
const selectStubs = {
  SelectPro: { props: ['modelValue'], emits: ['update:modelValue'], template: '<select :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)"><slot /></select>' },
  SelectProTrigger: { template: '<span><slot /></span>' },
  SelectValue: { template: '<span />' },
  SelectProContent: { template: '<slot />' },
  SelectProItem: { props: ['value'], template: '<option :value="value"><slot /></option>' },
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
      expect(new Set(select.findAll('option').map((o) => o.attributes('value')))).toEqual(new Set(['all', 'open', 'settled']))
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
    const wrapper = mount(CostCandidatesPage, { global: { stubs: { ...layoutStub, ...selectStubs } } })
    await flushPromises()

    expect(wrapper.find('[aria-label="成本候选关键字"]').exists()).toBe(true)
    const allSentinelSelects = wrapper.findAll('select').filter((select) => select.findAll('option').some((option) => option.attributes('value') === 'all'))
    expect(allSentinelSelects).toHaveLength(0)
  })
})
