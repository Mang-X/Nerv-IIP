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

/**
 * #1399 M5 待排池搜索。池子一次最多 500 条，此前一个搜索框都没有——排产员找一张急单
 * 只能滚，成本高于浏览器 Ctrl+F。
 */
describe('SchedulingOrderPool 搜索 (#1399 M5)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  const candidates = [
    { workOrderId: 'wo-1', workOrderNo: 'WO-2026-03008', skuCode: 'SKU-ROD-01' },
    { workOrderId: 'wo-2', workOrderNo: 'WO-2026-03007', skuCode: 'SKU-TUB-02' },
    { workOrderId: 'wo-3', workOrderNo: 'WO-2026-04101', skuCode: 'SKU-ROD-09' },
  ]

  function mountPool(props: Record<string, unknown> = {}) {
    return mount(SchedulingOrderPool, {
      props: { candidates, draftOrders: [], loading: false, scopeReady: true, ...props },
    })
  }

  async function typeSearch(wrapper: ReturnType<typeof mountPool>, value: string) {
    const input = wrapper.find('input[aria-label="搜索待排工单"]')
    await input.setValue(value)
    return input
  }

  it('按工单号过滤表体，只留命中行', async () => {
    const wrapper = mountPool()
    expect(wrapper.findAll('tbody tr')).toHaveLength(3)

    await typeSearch(wrapper, '03008')

    const rows = wrapper.findAll('tbody tr')
    expect(rows).toHaveLength(1)
    expect(rows[0].text()).toContain('WO-2026-03008')
  })

  it('按物料编码过滤（不只认工单号）', async () => {
    const wrapper = mountPool()

    await typeSearch(wrapper, 'sku-rod')

    expect(wrapper.findAll('tbody tr')).toHaveLength(2)
  })

  it('搜不到时出「筛没了」形态，而不是复用「当前没有待排产的工单」', async () => {
    const wrapper = mountPool()

    await typeSearch(wrapper, '不存在的关键词')

    const empty = wrapper.find('[data-testid="scheduling-order-pool-no-search-hit"]')
    expect(empty.exists()).toBe(true)
    expect(empty.text()).toContain('不存在的关键词')
    // 关键：两种空是两回事，池子里明明有 3 张单，不许说「没有待排产的工单」。
    expect(wrapper.text()).not.toContain('当前没有待排产的工单')
  })

  it('批量加入只作用于当前筛选结果，不会把整池 500 条都加进去', async () => {
    const wrapper = mountPool()

    await typeSearch(wrapper, '03008')
    const bulk = wrapper.findAll('button').find((b) => b.text().includes('加入筛选结果'))!
    await bulk.trigger('click')

    expect(wrapper.emitted('include')?.at(-1)).toEqual([['wo-1'], true])
  })
})
