import { mount } from '@vue/test-utils'
import { NvInfiniteList, NvPullRefresh } from '@nerv-iip/ui-mobile'
import { afterEach, describe, expect, it } from 'vitest'

import TaskListShell from './TaskListShell.vue'

afterEach(() => sessionStorage.clear())

describe('TaskListShell', () => {
  it('统一呈现范围、计数、更新时间并透传刷新与加载', async () => {
    const wrapper = mount(TaskListShell, {
      props: {
        stateKey: 'quality-tasks',
        scope: '当前账号 Self',
        source: '质检待检任务服务',
        loaded: 20,
        total: 45,
        updatedAt: '2026-08-01T01:02:03Z',
        pending: false,
        refreshing: false,
        loadingMore: false,
      },
      slots: {
        filters: '<div data-testid="filters">状态：待检</div>',
        default: '<div data-testid="task-row">任务</div>',
      },
    })

    expect(wrapper.get('[data-testid="task-list-meta"]').text()).toContain('已加载 20 / 共 45')
    expect(wrapper.get('[data-testid="filters"]').text()).toContain('状态：待检')

    wrapper.getComponent(NvPullRefresh).vm.$emit('refresh')
    wrapper.getComponent(NvInfiniteList).vm.$emit('load')
    await wrapper.vm.$nextTick()

    expect(wrapper.emitted('refresh')).toHaveLength(1)
    expect(wrapper.emitted('loadMore')).toHaveLength(1)
  })

  it('次页失败保留列表并显示局部重试，初次失败显示主错误态', async () => {
    const partial = mount(TaskListShell, {
      props: {
        stateKey: 'mes-tasks',
        scope: '当前工作中心',
        source: 'MES 工序任务服务',
        loaded: 20,
        total: 45,
        pending: false,
        refreshing: false,
        loadingMore: false,
        loadMoreError: new Error('page-2 failed'),
      },
      slots: { default: '<div data-testid="kept-row">已加载任务</div>' },
    })

    expect(partial.find('[data-testid="kept-row"]').exists()).toBe(true)
    expect(partial.get('[data-testid="task-list-load-error"]').text()).toContain('已加载数据保留')

    const initial = mount(TaskListShell, {
      props: {
        stateKey: 'alarm-list',
        scope: '当前组织',
        source: '设备报警服务',
        loaded: 0,
        total: 0,
        pending: false,
        refreshing: false,
        loadingMore: false,
        error: new Error('initial failed'),
      },
    })

    expect(initial.find('[data-testid="task-list-initial-error"]').exists()).toBe(true)
  })

  it('已有数据时把查询失败呈现为刷新错误，绝不冒充下一页失败', () => {
    const wrapper = mount(TaskListShell, {
      props: {
        stateKey: 'wms-picking-tasks',
        scope: '当前授权 WMS 作业范围',
        source: 'WMS 仓储任务服务',
        loaded: 20,
        total: 45,
        pending: false,
        refreshing: false,
        loadingMore: false,
        error: { message: '任务刷新被网关拒绝' },
      },
      slots: { default: '<div data-testid="kept-row">已加载任务</div>' },
    })

    expect(wrapper.find('[data-testid="kept-row"]').exists()).toBe(true)
    expect(wrapper.get('[data-testid="task-list-retained-error"]').text()).toContain(
      '任务刷新被网关拒绝',
    )
    expect(wrapper.text()).not.toContain('下一页加载失败')
  })

  it('离开后恢复筛选状态和列表滚动位置', async () => {
    sessionStorage.setItem(
      'nerv-iip.business-pda.task-list.quality-tasks',
      JSON.stringify({ filters: { status: 'inProgress', keyword: 'WO-9' }, scrollTop: 286 }),
    )
    const wrapper = mount(TaskListShell, {
      props: {
        stateKey: 'quality-tasks',
        scope: '当前账号 Self',
        source: '质检待检任务服务',
        loaded: 20,
        total: 45,
        pending: false,
        refreshing: false,
        loadingMore: false,
        filterState: { status: 'pending', keyword: '' },
      },
    })

    await wrapper.vm.$nextTick()

    expect(wrapper.emitted('restore')?.[0]).toEqual([
      { filters: { status: 'inProgress', keyword: 'WO-9' }, scrollTop: 286 },
    ])
  })

  it('先恢复筛选，首屏数据和列表高度就绪后只应用一次真实滚动位置', async () => {
    sessionStorage.setItem(
      'nerv-iip.business-pda.task-list.mes-operation-tasks',
      JSON.stringify({ filters: { status: 'inProgress' }, scrollTop: 286 }),
    )
    const wrapper = mount(TaskListShell, {
      props: {
        stateKey: 'mes-operation-tasks',
        scope: '当前工作中心',
        source: 'MES 工序任务服务',
        loaded: 0,
        total: 45,
        pending: true,
        refreshing: false,
        loadingMore: false,
        filterState: { status: '' },
      },
      slots: { default: '<div style="height: 1600px">任务列表</div>' },
    })
    await wrapper.vm.$nextTick()
    await wrapper.vm.$nextTick()

    const scroller = wrapper.get('.nv-m-pr-scroll').element as HTMLElement
    expect(wrapper.emitted('restore')?.[0]).toEqual([
      { filters: { status: 'inProgress' }, scrollTop: 286 },
    ])
    expect(scroller.scrollTop).toBe(0)

    await wrapper.setProps({ pending: false, loaded: 0 })
    await wrapper.vm.$nextTick()
    expect(scroller.scrollTop).toBe(0)

    await wrapper.setProps({ loaded: 20 })
    await wrapper.vm.$nextTick()
    await wrapper.vm.$nextTick()
    await wrapper.vm.$nextTick()
    await wrapper.vm.$nextTick()
    expect(scroller.scrollTop).toBe(286)

    scroller.scrollTop = 144
    await wrapper.get('.nv-m-pr-scroll').trigger('scroll')
    await wrapper.setProps({ loaded: 40 })
    await wrapper.vm.$nextTick()
    await wrapper.vm.$nextTick()
    expect(scroller.scrollTop).toBe(144)
  })
})
