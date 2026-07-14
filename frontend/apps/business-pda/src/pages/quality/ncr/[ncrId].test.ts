import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'

// ---- vue-router mock ----------------------------------------------------------
const push = vi.fn(() => Promise.resolve())
const back = vi.fn()
vi.mock('vue-router', () => ({
  useRouter: () => ({ push, back }),
  useRoute: () => ({ params: { ncrId: 'ncr-001' }, query: { from: 'rec-1' } }),
}))

// ---- NCR composable mock ------------------------------------------------------
const ncr = shallowRef<Record<string, unknown> | null>({
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
const ncrPending = shallowRef(false)
const ncrError = shallowRef<unknown>(null)
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

  it('navigates from NCR to the source inspection record via a real route (bidirectional interlink)', async () => {
    const wrapper = mount(NcrDetailPage)
    await flushPromises()
    // NCR → 检验记录互链：点按打开 /quality/record/{id} 真实路由。
    await wrapper.get('[data-testid="source-record"]').trigger('click')
    expect(push).toHaveBeenCalledWith('/quality/record/rec-1')
    // 「返回检验流程」按钮回到来路。
    await wrapper.get('[data-testid="ncr-back"]').trigger('click')
    expect(back.mock.calls.length + push.mock.calls.length).toBeGreaterThan(1)
  })
})
