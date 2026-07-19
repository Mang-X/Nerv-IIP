import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import ScreenFreshness from './ScreenFreshness.vue'

describe('NvScreenFreshness', () => {
  it('exposes the freshness state with text instead of color alone', async () => {
    const wrapper = mount(ScreenFreshness, {
      props: { tone: 'live', label: '实时 · 最后更新 10:24:08' },
    })

    const status = wrapper.get('[role="status"]')
    expect(status.text()).toBe('实时 · 最后更新 10:24:08')
    expect(status.classes()).toContain('live')
    expect(status.get('[aria-hidden="true"]').attributes('aria-hidden')).toBe('true')

    await wrapper.setProps({ tone: 'stale', label: '数据滞留 · 最后更新 10:18:01' })
    expect(status.text()).toBe('数据滞留 · 最后更新 10:18:01')
    expect(status.classes()).toContain('stale')
  })

  it('supports the waiting state without claiming live data', () => {
    const wrapper = mount(ScreenFreshness, {
      props: { tone: 'wait', label: '等待首次数据' },
    })

    expect(wrapper.get('[role="status"]').classes()).toContain('wait')
    expect(wrapper.text()).toBe('等待首次数据')
  })
})
