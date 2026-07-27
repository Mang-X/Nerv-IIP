import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import NvGroupPanel from './NvGroupPanel.vue'

describe('NvGroupPanel', () => {
  it('renders the group heading, subtitle and count, and shows content when open', () => {
    const wrapper = mount(NvGroupPanel, {
      props: { title: 'WO-2026-0431', subtitle: '前减振器总成 · 一号装配线', count: '4 道工序' },
      slots: { default: '<p>工序明细</p>' },
    })

    expect(wrapper.text()).toContain('WO-2026-0431')
    expect(wrapper.text()).toContain('前减振器总成 · 一号装配线')
    expect(wrapper.text()).toContain('4 道工序')
    expect(wrapper.text()).toContain('工序明细')
  })

  it('is expanded by default and toggles collapsed state on the heading button', async () => {
    const wrapper = mount(NvGroupPanel, {
      props: { title: 'WO-2026-0431' },
      slots: { default: '<p>工序明细</p>' },
    })
    const toggle = wrapper.get('button[aria-expanded]')
    expect(toggle.attributes('aria-expanded')).toBe('true')

    await toggle.trigger('click')
    expect(toggle.attributes('aria-expanded')).toBe('false')
    // v-show keeps the node mounted; collapsed content must not be visible.
    expect(wrapper.get('[id]').isVisible()).toBe(false)
  })

  it('links the toggle to the content region for assistive tech', () => {
    const wrapper = mount(NvGroupPanel, {
      props: { title: 'WO-2026-0431' },
      slots: { default: '<p>工序明细</p>' },
    })
    const controls = wrapper.get('button[aria-expanded]').attributes('aria-controls')
    expect(controls).toBeTruthy()
    expect(wrapper.find(`#${controls}`).exists()).toBe(true)
  })

  it('reveals the collapsed summary only while collapsed', async () => {
    const wrapper = mount(NvGroupPanel, {
      props: { title: 'WO-2026-0431', collapsedSummary: '2 道待派工 · 1 道有阻塞' },
      slots: { default: '<p>工序明细</p>' },
    })
    expect(wrapper.text()).not.toContain('2 道待派工 · 1 道有阻塞')

    await wrapper.get('button[aria-expanded]').trigger('click')
    expect(wrapper.text()).toContain('2 道待派工 · 1 道有阻塞')
  })

  it('supports a controlled open state through v-model:open', async () => {
    const wrapper = mount(NvGroupPanel, {
      props: { title: 'WO-2026-0431', open: false },
      slots: { default: '<p>工序明细</p>' },
    })
    expect(wrapper.get('button[aria-expanded]').attributes('aria-expanded')).toBe('false')

    await wrapper.get('button[aria-expanded]').trigger('click')
    expect(wrapper.emitted('update:open')?.at(-1)).toEqual([true])
  })
})
