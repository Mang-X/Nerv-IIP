import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'

// ---- vue-router mock ----------------------------------------------------------
const push = vi.fn(() => Promise.resolve())
const back = vi.fn()
vi.mock('vue-router', () => ({
  useRouter: () => ({ push, back }),
  useRoute: () => ({ params: { recordId: 'rec-1' }, query: {} }),
}))

// ---- record composable mock ----------------------------------------------------
const record = shallowRef<Record<string, unknown> | null>({
  inspectionRecordId: 'rec-1',
  sourceType: 'receiving',
  sourceService: 'wms',
  sourceDocumentId: 'RCV-1',
  skuCode: 'SKU-A',
  inspectedQuantity: 10,
  batchNo: 'LOT-1',
  serialNo: null,
  uomCode: 'pcs',
  result: 'rejected',
  dispositionReason: '外观不良，判退',
  nonconformanceReportId: 'ncr-001',
  resultLines: [
    {
      characteristicCode: 'appearance',
      observedValue: '不合格',
      measuredValue: null,
      unitCode: null,
      result: 'failed',
      defectReason: 'SCRATCH',
      defectQuantity: 2,
    },
  ],
  createdAtUtc: '2026-07-14T01:00:00Z',
})
const recordPending = shallowRef(false)
const recordError = shallowRef<unknown>(null)
vi.mock('@/composables/useBusinessInspectionRecord', () => ({
  useInspectionRecordDetail: () => ({
    record,
    pending: recordPending,
    error: recordError,
    refresh: vi.fn(),
  }),
}))

import RecordDetailPage from './[recordId].vue'

describe('inspection record detail page', () => {
  it('renders the authoritative result, disposition and characteristic lines', async () => {
    const wrapper = mount(RecordDetailPage)
    await flushPromises()
    const header = wrapper.get('[data-testid="record-detail"]').text()
    expect(header).toContain('SKU-A')
    expect(header).toContain('不合格') // 权威结论中文标签
    expect(wrapper.text()).toContain('外观不良，判退')
    expect(wrapper.get('[data-testid="record-line"]').text()).toContain('appearance')
  })

  it('navigates from the record to its NCR via a real route (bidirectional interlink)', async () => {
    const wrapper = mount(RecordDetailPage)
    await flushPromises()
    // 记录 → NCR 互链：点按打开 /quality/ncr/{id}，带上本记录 id 供 NCR 页回链。
    await wrapper.get('[data-testid="record-ncr-link"]').trigger('click')
    expect(push).toHaveBeenCalledWith({ path: '/quality/ncr/ncr-001', query: { from: 'rec-1' } })
  })
})
