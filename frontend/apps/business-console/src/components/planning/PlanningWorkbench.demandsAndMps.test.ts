import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it } from 'vitest'
import { nextTick } from 'vue'

import {
  planningSpies,
  routerPush,
  planningRoute,
  resetPlanningWorkbench,
} from './planningWorkbenchTestFixture'
import PlanningWorkbench from './PlanningWorkbench.vue'

describe('PlanningWorkbench', () => {
  beforeEach(resetPlanningWorkbench)

  it('普通无来源入口保留需求池落点', () => {
    const wrapper = mount(PlanningWorkbench)
    expect(wrapper.get('[data-active-tab]').attributes('data-active-tab')).toBe('demands')
    expect(wrapper.find('[data-select-value="latest"]').exists()).toBe(true)
  })

  it('无来源入口仍显示需求池和最近完成运行，重复来源参数不定位', () => {
    planningRoute.query = { suggestionId: ['source-old', 'other'] }
    const wrapper = mount(PlanningWorkbench)
    expect(wrapper.get('[data-active-tab]').attributes('data-active-tab')).toBe('demands')
    expect(wrapper.findAll('.cell-skuCode.bg-primary\\/10')).toHaveLength(0)
    expect(wrapper.find('[data-select-value="latest"]').exists()).toBe(true)
  })

  it('作废手工需求后保留页面追溯入口', async () => {
    const wrapper = mount(PlanningWorkbench)
    await wrapper
      .findAll('.cell-actions button')
      .find((button) => button.text() === '作废')!
      .trigger('click')
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '确认作废')!
      .trigger('click')
    await flushPromises()
    expect(planningSpies.cancelDemand).toHaveBeenCalledWith('demand-002')
  })

  it('窗外需求保留在需求池，但不算本次 MRP 的已覆盖需求', async () => {
    const wrapper = mount(PlanningWorkbench)
    planningSpies.mrpRunsRef!.value = [
      {
        runId: 'run-newer',
        status: 'Completed',
        horizonStart: '2026-07-01',
        horizonEnd: '2026-07-31',
      },
      {
        runId: 'run-001',
        status: 'Completed',
        horizonStart: '2026-06-01',
        horizonEnd: '2026-06-30',
      },
    ]
    planningSpies.demandsRef!.value = [
      { ...planningSpies.demandsRef!.value[0], dueDate: '2026-06-30' },
      { ...planningSpies.demandsRef!.value[1], dueDate: '2026-07-01' },
    ]
    planningSpies.suggestionsRef!.value = [
      {
        runId: 'run-001',
        skuCode: 'SKU-FG-1000',
        suggestionType: 'planned-work-order',
        status: 'Open',
      },
    ]
    await nextTick()

    expect(wrapper.text()).toContain('SO-DEMO-001')
    expect(wrapper.text()).toContain('FC-2026-08-A')
    expect(
      wrapper
        .findAll('.cell-coverage')
        .slice(0, 2)
        .map((cell) => cell.text()),
    ).toEqual(['已生成建议', '窗外'])
    expect(wrapper.get('[label="需求物料覆盖率"]').attributes('value')).toBe('100')

    planningSpies.mrpRunsRef!.value = [
      { runId: 'run-running', status: 'Running', horizonEnd: '2026-07-31' },
      { runId: 'run-001', status: 'Completed', horizonEnd: '2026-06-30' },
    ]
    await nextTick()
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '查看追溯')!
      .trigger('click')
    expect(wrapper.get('[label="需求物料覆盖率"]').attributes('value')).toBe('100')
    expect(wrapper.get('[data-testid="time-phased-panel"]').attributes('data-run-id')).toBe(
      'run-001',
    )
    expect(
      wrapper
        .findAll('.cell-coverage')
        .slice(0, 2)
        .map((cell) => cell.text()),
    ).toEqual(['已生成建议', '窗外'])
  })

  it.each([
    ['pcs', '120 件'],
    ['box', '120 箱'],
    ['unknown-unit', '120 —'],
    ['kg', '120 —'],
  ])('MPS 数量保留数值并按目录显示单位：%s', async (uomCode, expected) => {
    const wrapper = mount(PlanningWorkbench)
    planningSpies.mpsBucketsRef!.value = [
      { mpsId: 'mps-001', skuCode: 'SKU-FG-1000', uomCode, quantity: 120, status: 'Draft' },
    ]
    await flushPromises()
    // 需求池与建议数量不属于本票的 MPS 列，避免误改工作台共用 formatter。
    expect(wrapper.findAll('.cell-quantity').map((cell) => cell.text())).toContain(expected)
    expect(wrapper.findAll('.cell-quantity').map((cell) => cell.text())).toContain('2 pcs')
    wrapper.unmount()
  })

  it('从需求行带入原物料、单位、站点、日期与数量', async () => {
    const wrapper = mount(PlanningWorkbench)
    await wrapper
      .findAll('.cell-actions button')
      .filter((button) => button.text() === '带入 MPS')[1]!
      .trigger('click')
    expect(planningSpies.mpsFormRef).toMatchObject({
      skuCode: 'SKU-FG-2000',
      uomCode: 'pcs',
      siteCode: 'SITE-01',
      bucketDate: '2026-08-20',
      quantity: 8,
    })
  })

  it('新建 MPS 选料预填基本单位，允许覆盖并在换至未知物料时清除', async () => {
    const wrapper = mount(PlanningWorkbench)
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '新建 MPS')!
      .trigger('click')
    await wrapper.get('#mps-sku').setValue('FG-SHOCK')
    expect(planningSpies.mpsFormRef!.uomCode).toBe('pcs')
    const form = wrapper.findAll('form').find((node) => node.find('#mps-uom').exists())!
    await form.get('[data-select-value="kg"]').trigger('click')
    expect(planningSpies.mpsFormRef!.uomCode).toBe('kg')
    await wrapper.get('#mps-sku').setValue('RM-UNKNOWN')
    expect(planningSpies.mpsFormRef!.uomCode).toBe('')
  })

  it('MPS 评审人 / 发布人显示员工姓名，名录里查不到的账号显示「—」', async () => {
    const wrapper = mount(PlanningWorkbench)
    planningSpies.mpsBucketsRef!.value = [
      {
        mpsId: 'mps-001',
        skuCode: 'SKU-FG-1000',
        uomCode: 'pcs',
        quantity: 10,
        status: 'Released',
        reviewedBy: 'user-emp-zhangwei',
        releasedBy: 'user-admin',
      },
    ]
    await flushPromises()

    const text = wrapper.find('.cell-reviewRelease').text()
    expect(text).toContain('评审 张伟')
    expect(text).toContain('发布 —')
    expect(text).not.toContain('user-')
  })

  it('编辑历史草稿仍按已保存单位更新同一行', async () => {
    const wrapper = mount(PlanningWorkbench)
    planningSpies.mpsBucketsRef!.value = [
      {
        mpsId: 'mps-001',
        skuCode: 'FG-SHOCK',
        uomCode: 'box',
        siteCode: 'SITE-01',
        bucketDate: '2026-06-15',
        quantity: 10,
        status: 'Draft',
      },
    ]
    await flushPromises()

    await wrapper.get('[aria-label="编辑主计划行 FG-SHOCK"]').trigger('click')
    expect(wrapper.text()).toContain('编辑主计划行')
    expect(planningSpies.mpsFormRef!.quantity).toBe(10)
    await wrapper.get('#mps-qty').setValue('12')
    expect(planningSpies.mpsFormRef!.quantity).toBe(12)
    const submittedQuantities: number[] = []
    const submittedUnits: string[] = []
    planningSpies.updateMpsBucket.mockImplementationOnce(async () => {
      submittedQuantities.push(planningSpies.mpsFormRef!.quantity)
      submittedUnits.push(planningSpies.mpsFormRef!.uomCode)
    })
    await wrapper
      .findAll('form')
      .find((form) => form.find('#mps-qty').exists())!
      .trigger('submit')

    expect(planningSpies.updateMpsBucket).toHaveBeenCalledWith('mps-001')
    expect(submittedQuantities).toEqual([12])
    expect(submittedUnits).toEqual(['box'])
  })

  it('drills a sales-order demand into the ERP order search without copying order facts', async () => {
    const wrapper = mount(PlanningWorkbench)

    await wrapper.get('[aria-label="查看销售订单 SO-DEMO-001"]').trigger('click')

    expect(routerPush).toHaveBeenCalledWith({
      path: '/erp/sales/orders',
      query: { keyword: 'SO-DEMO-001' },
    })
  })

  // GH#1292 第 5 项：需求池此前没有任何查找手段，几百条需求只能肉眼扫。
  // 读面整表返回、不带关键字参数，所以筛选在前端做——这组用例锁住它真的筛得动。
  describe('需求池搜索与筛选', () => {
    it('关键字命中来源单号 / 物料 / 客户，未命中的行不再渲染', async () => {
      const wrapper = mount(PlanningWorkbench)
      expect(wrapper.text()).toContain('SO-DEMO-001')
      expect(wrapper.text()).toContain('FC-2026-08-A')

      await wrapper.get('[aria-label="需求池关键字"]').setValue('fc-2026')

      expect(wrapper.text()).toContain('FC-2026-08-A')
      expect(wrapper.text()).not.toContain('SO-DEMO-001')
      // 页签同步显「筛出数/总数」，别让人以为需求池整个缩水了。
      expect(wrapper.text()).toContain('需求池 (1/2)')
    })

    it('关键字也能按物料编码命中', async () => {
      const wrapper = mount(PlanningWorkbench)

      await wrapper.get('[aria-label="需求池关键字"]').setValue('SKU-FG-1000')

      expect(wrapper.text()).toContain('SO-DEMO-001')
      expect(wrapper.text()).not.toContain('FC-2026-08-A')
    })

    // 刷新后已选类型整类消失时，下拉不能留一个不在选项里的值（会显示为空白，
    // 表格又按它筛成空，用户看不出发生了什么）。
    it('刷新后已选类型不复存在时回落全部类型并说明原因', async () => {
      const wrapper = mount(PlanningWorkbench)
      // 用「销售订单」这一类：它只出现在需求池筛选下拉里，不会和新建需求表单的类型下拉撞选择器。
      await wrapper.get('[data-select-value="sales-order"]').trigger('click')
      expect(wrapper.text()).toContain('SO-DEMO-001')
      expect(wrapper.text()).not.toContain('FC-2026-08-A')

      // 销售订单类需求被消化完，刷新后整类消失。
      planningSpies.demandsRef!.value = planningSpies.demandsRef!.value.filter(
        (demand) => demand.demandType !== 'sales-order',
      )
      await nextTick()
      await nextTick()

      expect(wrapper.text()).toContain('已不在当前需求池中，已切回全部类型')
      // 回落后剩下的那条照常显示，不是筛成空白。
      expect(wrapper.text()).toContain('FC-2026-08-A')
    })

    it('全都筛没了时给的是「换个条件」而不是「当前范围没有需求」', async () => {
      const wrapper = mount(PlanningWorkbench)

      await wrapper.get('[aria-label="需求池关键字"]').setValue('查无此单')

      expect(wrapper.text()).not.toContain('SO-DEMO-001')
      expect(wrapper.text()).toContain('需求池 (0/2)')
    })
  })

  it('maps the demand source reference into the shared urgency badge', () => {
    const wrapper = mount(PlanningWorkbench)

    const badge = wrapper.find('[data-testid="order-urgency"]')
    expect(badge.exists()).toBe(true)
    expect(badge.attributes('data-ref')).toBe('SO-DEMO-001')
    expect(badge.attributes('data-mode')).toBe('level')
  })
})
