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

  it('shows the source inspection record as display-only context and back returns to the flow', async () => {
    const wrapper = mount(NcrDetailPage)
    await flushPromises()
    // 来源检验记录仅展示上下文（PDA 无检验记录详情路由，不做可点击入口）。
    const source = wrapper.get('[data-testid="source-record"]')
    expect(source.text()).toContain('rec-1')
    expect(source.attributes('role')).toBeUndefined() // 无 arrow → 非 button 语义，不可点击
    // 「返回检验流程」按钮回到来路。
    await wrapper.get('[data-testid="ncr-back"]').trigger('click')
    expect(back.mock.calls.length + push.mock.calls.length).toBeGreaterThan(0)
  })
})
