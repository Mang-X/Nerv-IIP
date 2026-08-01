import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import PullRefresh from './PullRefresh.vue'

describe('PullRefresh', () => {
  it('恢复内部滚动位置并向外报告滚动', async () => {
    const wrapper = mount(PullRefresh, {
      props: { scrollTop: 184 },
      slots: { default: '<div style="height: 1000px">任务</div>' },
    })
    await wrapper.vm.$nextTick()

    const scroller = wrapper.get('.nv-m-pr-scroll').element as HTMLElement
    expect(scroller.scrollTop).toBe(184)

    scroller.scrollTop = 216
    await wrapper.get('.nv-m-pr-scroll').trigger('scroll')
    expect(wrapper.emitted('scroll')?.at(-1)).toEqual([216])
  })

  it('回报浏览器实际应用的滚动位置，并在列表高度代次变化时重试', async () => {
    const wrapper = mount(PullRefresh, {
      props: { scrollTop: 900, scrollRestoreKey: '20:false' },
      slots: { default: '<div style="height: 1000px">任务</div>' },
    })
    await wrapper.vm.$nextTick()
    await wrapper.vm.$nextTick()

    expect(wrapper.emitted('scrollRestored')?.at(-1)?.[0]).toMatchObject({ requested: 900 })

    const count = wrapper.emitted('scrollRestored')?.length ?? 0
    await wrapper.setProps({ scrollRestoreKey: '40:false' })
    await wrapper.vm.$nextTick()
    await wrapper.vm.$nextTick()

    expect(wrapper.emitted('scrollRestored')).toHaveLength(count + 1)
  })
})
