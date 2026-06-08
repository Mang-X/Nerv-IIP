import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import FinancePage from './finance.vue'

vi.mock('@/composables/useBusinessErp', () => ({
  useErpFinance: () => ({
    summary: computed(() => ({
      openPayableAmount: 1000,
      openReceivableAmount: 2500,
      costCandidateAmount: 300,
      postedVoucherCount: 7,
    })),
    summaryError: shallowRef(undefined),
    summaryPending: shallowRef(false),
    refreshSummary: vi.fn(),
    filters: reactive({ skip: 0, take: 10 }),
    receivables: computed(() => [
      { receivableNo: 'AR-1', sourceDocumentNo: 'SO-1', customerCode: 'CUST-1', amount: 2500, openAmount: 2500, currencyCode: 'CNY', status: 'open' },
    ]),
    receivablesTotal: computed(() => 1),
    receivablesError: shallowRef(undefined),
    receivablesPending: shallowRef(false),
    refreshReceivables: vi.fn(),
  }),
}))

const layoutStub = { BusinessLayout: { template: '<main><slot /></main>' } }

describe('ERP finance page', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    document.body.innerHTML = ''
  })

  it('renders the finance summary and receivables', async () => {
    const wrapper = mount(FinancePage, { global: { stubs: layoutStub } })
    await flushPromises()

    expect(wrapper.text()).toContain('应付未结')
    expect(wrapper.text()).toContain('应收未结')
    expect(wrapper.text()).toContain('已过账凭证')
    expect(wrapper.text()).toContain('AR-1')
    expect(wrapper.text()).toContain('CUST-1')
  })
})
