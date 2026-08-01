import { mount } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'

import InfiniteList from './InfiniteList.vue'

const observers: MockIntersectionObserver[] = []

class MockIntersectionObserver {
  readonly observe = vi.fn()
  readonly disconnect = vi.fn()

  constructor(private readonly callback: IntersectionObserverCallback) {
    observers.push(this)
  }

  trigger(isIntersecting: boolean) {
    this.callback([{ isIntersecting } as IntersectionObserverEntry], this as never)
  }
}

afterEach(() => {
  observers.splice(0)
  vi.unstubAllGlobals()
})

describe('InfiniteList', () => {
  it('在父滚动区模式以底部哨兵触发加载且不创建第二个滚动容器', async () => {
    vi.stubGlobal('IntersectionObserver', MockIntersectionObserver)
    const wrapper = mount(InfiniteList, {
      props: {
        parentScroll: true,
        modelValue: false,
      },
      slots: { default: '<div>任务</div>' },
    })

    expect(wrapper.classes()).not.toContain('overflow-y-auto')
    expect(observers).toHaveLength(1)

    observers[0]?.trigger(true)
    await wrapper.vm.$nextTick()

    expect(wrapper.emitted('load')).toHaveLength(1)
    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual([true])
  })

  it('父滚动区模式在终页或加载中不重复触发', async () => {
    vi.stubGlobal('IntersectionObserver', MockIntersectionObserver)
    const wrapper = mount(InfiniteList, {
      props: {
        parentScroll: true,
        finished: true,
        modelValue: false,
      },
    })

    observers[0]?.trigger(true)
    await wrapper.vm.$nextTick()
    expect(wrapper.emitted('load')).toBeUndefined()
  })
})
