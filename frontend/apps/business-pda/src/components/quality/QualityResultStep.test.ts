import type { QualityResultState } from '@/composables/useInspectionExecution'
import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import QualityResultStep from './QualityResultStep.vue'

function submitted(
  over: { result?: string; ncrId?: string | null; ncrCode?: string | null } = {},
) {
  const state: QualityResultState = {
    phase: 'submitted',
    authoritative: {
      inspectionRecordId: 'rec-1',
      result: over.result ?? 'rejected',
      nonconformanceReportId: 'ncrId' in over ? (over.ncrId ?? null) : 'ncr-1',
      nonconformanceReportCode: 'ncrCode' in over ? (over.ncrCode ?? null) : 'NCR-2026-0001',
    },
  }
  return mount(QualityResultStep, { props: { state } })
}

describe('QualityResultStep', () => {
  it('rejected: shows business NCR code and the NCR interlink opens the report', async () => {
    const wrapper = submitted()
    const link = wrapper.get('[data-testid="ncr-link"]')
    expect(link.text()).toContain('NCR-2026-0001') // 人读单号，非 GUID
    await link.trigger('click')
    expect(wrapper.emitted('openNcr')).toHaveLength(1) // 点按 → 打开 NCR 详情互链
  })

  it('passed: green result with no NCR interlink', () => {
    const wrapper = submitted({ result: 'passed', ncrId: null, ncrCode: null })
    expect(wrapper.find('[data-testid="ncr-link"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="next-task"]').exists()).toBe(true)
  })
})
