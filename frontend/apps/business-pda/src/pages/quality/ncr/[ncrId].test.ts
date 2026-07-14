import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { ref } from 'vue'

// ---- vue-router mock ----------------------------------------------------------
const push = vi.fn(() => Promise.resolve())
const back = vi.fn()
vi.mock('vue-router', () => ({
  useRouter: () => ({ push, back }),
  useRoute: () => ({ params: { ncrId: 'ncr-001' }, query: { from: 'rec-1' } }),
}))

// ---- NCR composable mock ------------------------------------------------------
const ncr = ref<Record<string, unknown> | null>({
  id: 'ncr-001',
  code: 'NCR-2026-0001',
  status: 'open',
  skuCode: 'SKU-A',
  sourceDocumentId: 'RCV-1',
  defectReason: '外观不良',
  defectQuantity: 5,
  batchNo: 'LOT-1',
  serialNo: null,
})
const ncrPending = ref(false)
const ncrError = ref<unknown>(null)
vi.mock('@/composables/useBusinessNonconformanceReport', () => ({
  useNonconformanceReport: () => ({
    ncr,
    pending: ncrPending,
    error: ncrError,
    refresh: vi.fn(),
  }),
}))

import NcrDetailPage from './[ncrId].vue'

describe('NCR detail page', () => {
  it('renders NCR business number, status and defect fields', async () => {
    const wrapper = mount(NcrDetailPage)
    await flushPromises()
    const text = wrapper.get('[data-testid="ncr-detail"]').text()
    expect(text).toContain('NCR-2026-0001')
    expect(wrapper.text()).toContain('外观不良')
    expect(wrapper.text()).toContain('SKU-A')
  })

  it('shows a back-link to the source inspection record (from query) and navigates back', async () => {
    const wrapper = mount(NcrDetailPage)
    await flushPromises()
    const backLink = wrapper.get('[data-testid="back-to-record"]')
    expect(backLink.text()).toContain('rec-1') // 来源检验记录回链
    await backLink.trigger('click')
    // goBack: 有历史则 router.back，否则 push /quality/tasks——测试环境 history.length 通常 <=1。
    expect(back.mock.calls.length + push.mock.calls.length).toBeGreaterThan(0)
  })
})
