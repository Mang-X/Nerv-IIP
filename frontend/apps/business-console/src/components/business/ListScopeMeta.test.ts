import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import ListScopeMeta from './ListScopeMeta.vue'

describe('ListScopeMeta', () => {
  it('shows the real scope, source, loaded/total counts, and read time', () => {
    const wrapper = mount(ListScopeMeta, {
      props: {
        scope: '当前登录组织 / 当前业务环境',
        source: '维修工单服务（组织/环境范围）',
        loaded: 3,
        total: 7,
        updatedAt: '2026-07-28T10:20:30.000Z',
      },
    })

    expect(wrapper.text()).toContain('范围：当前登录组织 / 当前业务环境')
    expect(wrapper.text()).toContain('来源：维修工单服务（组织/环境范围）')
    expect(wrapper.text()).toContain('已加载 3 / 共 7')
    expect(wrapper.text()).toContain('更新时间（最近成功响应）：2026/7/28 18:20')
  })

  it('explains an unsupported assignment filter in an empty state', () => {
    const wrapper = mount(ListScopeMeta, {
      props: {
        scope: '当前登录组织 / 当前业务环境',
        source: '维修工单服务（组织/环境范围，暂不支持按维修人员归属筛选）',
        loaded: 0,
        total: 0,
        empty: true,
        emptyExplanation: '暂不支持按维修人员归属筛选，当前空态只代表组织/环境范围无数据。',
      },
    })

    expect(wrapper.text()).toContain('空态说明：暂不支持按维修人员归属筛选')
    expect(wrapper.text()).not.toContain('我的维修工单')
  })

  it('shows a retryable business-response failure instead of an empty explanation', () => {
    const wrapper = mount(ListScopeMeta, {
      props: {
        scope: '当前登录组织 / 当前业务环境',
        source: '维修工单服务（组织/环境范围）',
        loaded: 0,
        total: 0,
        failed: true,
        failureExplanation: '维修工单服务未成功返回，请重试。',
        empty: true,
        emptyExplanation: '当前组织/环境范围没有维修工单。',
      },
    })

    expect(wrapper.text()).toContain('查询失败：维修工单服务未成功返回，请重试。')
    expect(wrapper.text()).not.toContain('空态说明')
  })
})
