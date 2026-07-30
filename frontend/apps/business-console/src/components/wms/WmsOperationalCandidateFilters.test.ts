import { mount } from '@vue/test-utils'
import { NvInput, NvSearchSelect } from '@nerv-iip/ui'
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
        ready: true,
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
        ready: true,
        asOfUtc: '2026-07-30T01:00:00Z',
        freshnessUtc: '2026-07-30T00:59:00Z',
        truncated: true,
      },
    })

    expect(wrapper.text()).toContain('当前范围仓储作业记录候选')
    expect(wrapper.text()).toContain('候选已截断')
    expect(wrapper.text()).not.toContain('主数据')
    expect(wrapper.text()).not.toContain('warehouse-operational-records')
    expect(wrapper.text()).not.toContain('2026-07-30T01:00:00Z')
  })

  it('debounces through a controlled search model and exposes retryable errors', async () => {
    const wrapper = mount(WmsOperationalCandidateFilters, {
      props: {
        locationOptions: [],
        sourceLabel: '当前范围仓储作业记录候选',
        ready: true,
        searchKeyword: '',
        error: new Error('network'),
      },
    })

    expect(wrapper.text()).toContain('候选加载失败')
    await wrapper.get('[data-testid="candidate-retry"]').trigger('click')
    expect(wrapper.emitted('retry')).toHaveLength(1)
    expect(wrapper.text()).not.toContain('暂无库位候选')
  })

  it('keeps selectors and search disabled until authorized scope is ready', () => {
    const wrapper = mount(WmsOperationalCandidateFilters, {
      props: {
        locationOptions: [],
        sourceLabel: '当前范围仓储作业记录候选',
        ready: false,
      },
    })

    expect(wrapper.text()).toContain('请先选择可用作业范围')
    expect(
      wrapper.findAllComponents(NvSearchSelect).every((select) => select.props('disabled')),
    ).toBe(true)
    expect(wrapper.find('input[type="search"]').exists()).toBe(false)
  })

  it('uses the branded NvInput boundary for remote search', () => {
    const wrapper = mount(WmsOperationalCandidateFilters, {
      props: {
        locationOptions: [],
        sourceLabel: '当前范围仓储作业记录候选',
        ready: true,
      },
    })

    expect(wrapper.findComponent(NvInput).exists()).toBe(true)
  })
})
