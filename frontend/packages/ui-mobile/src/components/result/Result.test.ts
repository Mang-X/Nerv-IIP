import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import Result from './Result.vue'

describe('Result', () => {
  it('renders success state with title and description', () => {
    const wrapper = mount(Result, {
      props: { status: 'success', title: '过账成功', description: '收货单 RO-1 已完成' },
    })
    expect(wrapper.get('[data-result]').attributes('data-status')).toBe('success')
    expect(wrapper.text()).toContain('过账成功')
    expect(wrapper.text()).toContain('收货单 RO-1 已完成')
  })

  it('renders error state and the actions slot', () => {
    const wrapper = mount(Result, {
      props: { status: 'error', title: '过账失败' },
      slots: { actions: '<button>重试</button>' },
    })
    expect(wrapper.get('[data-result]').attributes('data-status')).toBe('error')
    expect(wrapper.text()).toContain('重试')
  })
})
