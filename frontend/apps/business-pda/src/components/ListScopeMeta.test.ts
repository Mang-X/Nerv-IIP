import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import ListScopeMeta from './ListScopeMeta.vue'

describe('ListScopeMeta', () => {
  it('shows the real scope, source, loaded/total counts, and read time', () => {
    const wrapper = mount(ListScopeMeta, {
      props: {
        scope: '当前登录组织 / 当前业务环境',
        source: '质检待检任务服务（组织/环境范围，状态：待检）',
        loaded: 2,
        total: 5,
        updatedAt: '2026-07-28T10:20:30.000Z',
      },
    })

    expect(wrapper.text()).toContain('范围：当前登录组织 / 当前业务环境')
    expect(wrapper.text()).toContain('来源：质检待检任务服务（组织/环境范围，状态：待检）')
    expect(wrapper.text()).toContain('已加载 2 / 共 5')
    expect(wrapper.text()).toContain('更新时间（最近成功响应）：2026/7/28 18:20')
  })

  it('explains a missing scope in the empty state without implying a personal list', () => {
    const wrapper = mount(ListScopeMeta, {
      props: {
        scope: '组织/环境未就绪',
        source: '质检待检任务服务（组织/环境范围，状态：待检）',
        loaded: 0,
        total: 0,
        empty: true,
        emptyExplanation: '缺少组织或环境范围，未发起查询。',
      },
    })

    expect(wrapper.text()).toContain('空态说明：缺少组织或环境范围，未发起查询。')
    expect(wrapper.text()).not.toContain('个人待检')
  })
})
