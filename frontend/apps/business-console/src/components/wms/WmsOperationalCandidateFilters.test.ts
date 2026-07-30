import { mount } from '@vue/test-utils'
import { NvSearchSelect } from '@nerv-iip/ui'
import { describe, expect, it } from 'vitest'
import WmsOperationalCandidateFilters from './WmsOperationalCandidateFilters.vue'

describe('WmsOperationalCandidateFilters', () => {
  it('clears a stale lot when the selected location changes', async () => {
    const wrapper = mount(WmsOperationalCandidateFilters, {
      props: {
        locationCode: 'A-01',
        lotNo: 'LOT-A',
        locationOptions: [{ value: 'B-02', label: 'B-02' }],
        lotOptions: [{ value: 'LOT-B', label: 'LOT-B' }],
        sourceLabel: '当前范围仓储作业记录候选',
      },
    })

    wrapper.findAllComponents(NvSearchSelect)[0]!.vm.$emit('update:modelValue', 'B-02')
    await wrapper.vm.$nextTick()

    expect(wrapper.emitted('update:locationCode')?.at(-1)).toEqual(['B-02'])
    expect(wrapper.emitted('update:lotNo')?.at(-1)).toEqual([undefined])
  })

  it('shows the declared source and truncation metadata without claiming master data', () => {
    const wrapper = mount(WmsOperationalCandidateFilters, {
      props: {
        locationOptions: [],
        sourceLabel: '当前范围仓储作业记录候选',
        sourceKind: 'warehouse-operational-records',
        asOfUtc: '2026-07-30T01:00:00Z',
        freshnessUtc: '2026-07-30T00:59:00Z',
        truncated: true,
      },
    })

    expect(wrapper.text()).toContain('warehouse-operational-records')
    expect(wrapper.text()).toContain('候选已截断')
    expect(wrapper.text()).not.toContain('主数据')
  })
})
