import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'

// ---- vue-router mock ----------------------------------------------------------
const push = vi.fn(() => Promise.resolve())
const back = vi.fn()
// query.from 故意给一个被篡改的值：互链必须来自服务端权威字段，query 不得影响导航目标。
vi.mock('vue-router', () => ({
  useRouter: () => ({ push, back }),
  useRoute: () => ({ params: { ncrId: 'ncr-001' }, query: { from: 'rec-tampered' } }),
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
  // 服务端权威回链（NCR 聚合的 SourceInspectionRecordId 投影）。
  sourceInspectionRecordId: 'rec-1',
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

  it('interlinks to the source record from the server-side field — tampered ?from cannot redirect it', async () => {
    const wrapper = mount(NcrDetailPage)
    await flushPromises()
    // NCR → 检验记录互链：目标取服务端权威 sourceInspectionRecordId（rec-1），
    // 路由 query 里被篡改的 from=rec-tampered 不影响导航目标。
    await wrapper.get('[data-testid="source-record"]').trigger('click')
    expect(push).toHaveBeenCalledWith('/quality/record/rec-1')
    // 「返回检验流程」按钮回到来路。
    await wrapper.get('[data-testid="ncr-back"]').trigger('click')
    expect(back.mock.calls.length + push.mock.calls.length).toBeGreaterThan(1)
  })

  it('hides the source-record interlink when the NCR has no server-side backlink', async () => {
    // 非检验来源的 NCR（无 SourceInspectionRecordId）→ 不渲染互链入口（也不因 query 出现）。
    const previous = ncr.value
    ncr.value = { ...previous, sourceInspectionRecordId: null }
    const wrapper = mount(NcrDetailPage)
    await flushPromises()
    expect(wrapper.find('[data-testid="source-record"]').exists()).toBe(false)
    ncr.value = previous
  })
})
