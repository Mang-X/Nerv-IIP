import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import { NvSpinner } from './index'

// Regression: #4296；中文读屏名称与既有加载形状、消费属性均是票面验收行为。
describe('NvSpinner', () => {
  it('announces loading in Chinese and preserves the default size and animation', () => {
    const wrapper = mount(NvSpinner)
    expect(wrapper.attributes('role')).toBe('status')
    expect(wrapper.attributes('aria-label')).toBe('加载中')
    expect(wrapper.classes()).toContain('size-4')
    expect(wrapper.classes()).toContain('animate-spin')
  })

  it('keeps consumer size, styling and decorative accessibility attributes', () => {
    const wrapper = mount(NvSpinner, {
      props: { class: 'size-6 text-muted-foreground' },
      attrs: { 'aria-hidden': 'true', 'data-icon': 'inline-start' },
    })
    expect(wrapper.classes()).toContain('size-6')
    expect(wrapper.classes()).not.toContain('size-4')
    expect(wrapper.classes()).toContain('text-muted-foreground')
    expect(wrapper.classes()).toContain('animate-spin')
    expect(wrapper.attributes('aria-hidden')).toBe('true')
    expect(wrapper.attributes('data-icon')).toBe('inline-start')
  })
})
