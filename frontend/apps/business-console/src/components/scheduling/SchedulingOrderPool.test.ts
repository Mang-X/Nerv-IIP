import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import SchedulingOrderPool from './SchedulingOrderPool.vue'

vi.mock('@/composables/useSkuNames', () => ({
  useSkuNames: () => ({
    resolveSkuName: () => undefined,
  }),
}))

/**
 * #1288 待排池的空态事实：作业范围未就绪时候选查询根本没发（enabled=false），
 * candidates 为空是「没查」而不是「没有」——必须出专门形态，不许下
 * 「当前没有待排产的工单」结论。
 */
describe('SchedulingOrderPool scope gate (#1288)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  function mountPool(props: Record<string, unknown> = {}) {
    return mount(SchedulingOrderPool, {
      props: {
        candidates: [],
        draftOrders: [],
        loading: false,
        ...props,
      },
    })
  }

  it('作业范围未就绪时显示未就绪形态与原因，不显示假空态', () => {
    const wrapper = mountPool({
      scopeReady: false,
      scopeMessage: '当前账号在本组织没有已授权的作业范围，无法读取现场数据。',
    })

    const blocked = wrapper.find('[data-testid="scheduling-order-pool-scope-blocked"]')
    expect(blocked.exists()).toBe(true)
    expect(blocked.text()).toContain('作业范围未就绪')
    expect(blocked.text()).toContain('没有已授权的作业范围')
    expect(wrapper.text()).not.toContain('当前没有待排产的工单')
  })

  it('作业范围就绪且确实没有候选时才允许下「没有待排产的工单」结论', () => {
    const wrapper = mountPool({ scopeReady: true })

    expect(wrapper.find('[data-testid="scheduling-order-pool-scope-blocked"]').exists()).toBe(false)
    expect(wrapper.text()).toContain('当前没有待排产的工单')
  })

  it('不传 scopeReady 时保持既有空态行为（向后兼容）', () => {
    const wrapper = mountPool()

    expect(wrapper.find('[data-testid="scheduling-order-pool-scope-blocked"]').exists()).toBe(false)
    expect(wrapper.text()).toContain('当前没有待排产的工单')
  })
})
