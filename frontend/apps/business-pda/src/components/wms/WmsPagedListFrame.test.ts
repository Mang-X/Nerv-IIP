import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'

import TaskListShell from '@/components/task-list/TaskListShell.vue'
import WmsPagedListFrame from './WmsPagedListFrame.vue'

const intersectionState = vi.hoisted(() => ({
  callback: undefined as ((entries: Array<{ isIntersecting: boolean }>) => void) | undefined,
}))

vi.mock('@vueuse/core', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@vueuse/core')>()
  return {
    ...actual,
    useIntersectionObserver: vi.fn(
      (_target: unknown, callback: (entries: Array<{ isIntersecting: boolean }>) => void) => {
        intersectionState.callback = callback
        return { stop: vi.fn() }
      },
    ),
  }
})

describe('WmsPagedListFrame', () => {
  it('透传下拉刷新，并在接近底部且仍有数据时触发加载更多', () => {
    const wrapper = mount(WmsPagedListFrame, {
      props: {
        refreshing: false,
        loadingMore: false,
        pending: false,
        loaded: 20,
        total: 45,
      },
      slots: { default: '<div data-row>任务</div>' },
    })

    wrapper.getComponent(TaskListShell).vm.$emit('refresh')
    wrapper.getComponent(TaskListShell).vm.$emit('loadMore')

    expect(wrapper.emitted('refresh')).toHaveLength(1)
    expect(wrapper.emitted('loadMore')).toHaveLength(1)
    expect(wrapper.text()).toContain('上拉加载更多')
  })

  it('终页不再触发加载并明确提示没有更多', () => {
    const wrapper = mount(WmsPagedListFrame, {
      props: {
        refreshing: false,
        loadingMore: false,
        pending: false,
        loaded: 20,
        total: 20,
      },
    })

    intersectionState.callback?.([{ isIntersecting: true }])

    expect(wrapper.emitted('loadMore')).toBeUndefined()
    expect(wrapper.text()).toContain('没有更多了')
  })
})
