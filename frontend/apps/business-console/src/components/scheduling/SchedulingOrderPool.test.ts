import { mount, flushPromises } from '@vue/test-utils'
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

describe('待排池需求变更标记', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('按 MES 工单字段显示变更、取消与无标记', () => {
    const pinia = createPinia()
    const wrapper = mount(SchedulingOrderPool, {
      global: { plugins: [pinia] },
      props: {
        draftOrders: [],
        candidates: [
          {
            workOrderId: 'WO-CHANGED',
            skuId: 'SKU-A',
            hasChangedDemand: true,
            hasCancelledDemand: false,
          },
          {
            workOrderId: 'WO-CANCELLED',
            skuId: 'SKU-A',
            hasChangedDemand: false,
            hasCancelledDemand: true,
          },
          {
            workOrderId: 'WO-PLAIN',
            skuId: 'SKU-A',
            hasChangedDemand: false,
            hasCancelledDemand: false,
          },
        ],
      },
    })
    const rows = wrapper.findAll('tbody tr')
    expect(rows[0].text()).toContain('需求已变更')
    expect(rows[0].text()).not.toContain('需求已取消')
    expect(rows[1].text()).toContain('需求已取消')
    expect(rows[1].text()).not.toContain('需求已变更')
    expect(rows[2].text()).not.toContain('需求已变更')
    expect(rows[2].text()).not.toContain('需求已取消')
  })
})

describe('待排池急单与优先级保存', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('展示 MES 已保存值；页内修改保存后显示权威读回值，重入仍保留', async () => {
    const candidates = [{ workOrderId: 'WO-001', priority: 60, isRush: true }]
    const saveOrder = vi.fn(async () => {
      await wrapper.setProps({
        candidates: [{ workOrderId: 'WO-001', priority: 25, isRush: false }],
      })
    })
    const wrapper = mount(SchedulingOrderPool, {
      global: { plugins: [createPinia()] },
      props: { candidates, draftOrders: [], canEditPriority: true, saveOrder },
    })
    const priority = wrapper.find('input[type="number"]')
    expect((priority.element as HTMLInputElement).value).toBe('60')
    expect(wrapper.find('[aria-label="急单 WO-001"]').attributes('data-state')).toBe('checked')
    await priority.setValue('20')
    await wrapper.find('[aria-label="急单 WO-001"]').trigger('click')
    await wrapper.find('[aria-label="保存工单 WO-001 急单与优先级"]').trigger('click')
    await flushPromises()
    expect(saveOrder).toHaveBeenCalledWith('WO-001', { priority: 20, isRush: false })
    expect((priority.element as HTMLInputElement).value).toBe('25')
    expect(wrapper.find('[aria-label="急单 WO-001"]').attributes('data-state')).toBe('unchecked')
    const reentered = mount(SchedulingOrderPool, {
      global: { plugins: [createPinia()] },
      props: {
        candidates: wrapper.props('candidates'),
        draftOrders: [],
        canEditPriority: true,
        saveOrder,
      },
    })
    expect((reentered.find('input[type="number"]').element as HTMLInputElement).value).toBe('25')
    expect(reentered.find('[aria-label="急单 WO-001"]').attributes('data-state')).toBe('unchecked')
  })
  it('保存失败时保留行内输入以便修正或重试', async () => {
    const wrapper = mount(SchedulingOrderPool, {
      global: { plugins: [createPinia()] },
      props: {
        candidates: [{ workOrderId: 'WO-001', priority: 60, isRush: false }],
        draftOrders: [],
        canEditPriority: true,
        saveOrder: async () => {
          throw new Error('工单已关闭，不能调整')
        },
      },
    })
    await wrapper.find('input[type="number"]').setValue('20')
    await wrapper.find('[aria-label="保存工单 WO-001 急单与优先级"]').trigger('click')
    await flushPromises()
    expect((wrapper.find('input[type="number"]').element as HTMLInputElement).value).toBe('20')
    expect(
      wrapper.find('[aria-label="保存工单 WO-001 急单与优先级"]').attributes('disabled'),
    ).toBeUndefined()
  })
})

describe('异步首版 500 单容量（#4137 DomainInvariant）', () => {
  it('可加入第 500 单，到达容量后阻止第 501 单与超限全部加入，仍能移出', async () => {
    setActivePinia(createPinia())
    const draftOrders = Array.from({ length: 499 }, (_, i) => ({
      workOrderId: `WO-${i}`,
      included: true,
      priority: 100,
      isRush: false,
    }))
    const wrapper = mount(SchedulingOrderPool, {
      props: { candidates: [{ workOrderId: 'WO-499' }, { workOrderId: 'WO-500' }], draftOrders },
    })
    try {
      const check = () => wrapper.find('button[aria-label="加入工单 WO-499"]')
      expect(check().attributes('disabled')).toBeUndefined()
      expect(
        wrapper
          .findAll('button')
          .find((b) => b.text() === '全部加入')!
          .attributes('disabled'),
      ).toBeDefined()
      await check().trigger('click')
      expect(wrapper.emitted('include')?.at(-1)).toEqual([['WO-499'], true])
      await wrapper.setProps({
        draftOrders: [
          ...draftOrders,
          { workOrderId: 'WO-499', included: true, priority: 100, isRush: false },
        ],
      })
      expect(wrapper.text()).toContain('最多选择 500 单')
      expect(
        wrapper.find('button[aria-label="加入工单 WO-500"]').attributes('disabled'),
      ).toBeDefined()
      expect(check().attributes('disabled')).toBeUndefined()
      await check().trigger('click')
      expect(wrapper.emitted('include')?.at(-1)).toEqual([['WO-499'], false])
    } finally {
      wrapper.unmount()
    }
  })
})
