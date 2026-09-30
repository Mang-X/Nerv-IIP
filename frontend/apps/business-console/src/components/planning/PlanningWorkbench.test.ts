import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { nextTick } from 'vue'

import { planningSpies, routerPush } from './planningWorkbenchTestFixture'
import PlanningWorkbench from './PlanningWorkbench.vue'
import { useAuthStore } from '@/stores/auth'

describe('PlanningWorkbench', () => {
  // 计划建议行的「对该单排产」按权限码显隐，组件因此要读 auth store（MAN-694 / #1262）。
  beforeEach(() => {
    setActivePinia(createPinia())
    planningSpies.runMrp = vi.fn(async () => undefined)
    planningSpies.acceptSuggestion.mockReset()
    planningSpies.cancelDemand.mockReset()
    routerPush.mockReset()
    planningSpies.toastError.mockReset()
    planningSpies.toastSuccess.mockReset()
    planningSpies.toastWarning.mockReset()
    planningSpies.activeMrpRun.runId = ''
    planningSpies.activeMrpRun.status = ''
    planningSpies.activeMrpRun.failureReason = ''
    planningSpies.activeMrpRun.suggestionCount = null
    planningSpies.resetDemands()
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

  it('pegging 与建议行页内互定位并显示承接单据状态', async () => {
    const wrapper = mount(PlanningWorkbench)
    planningSpies.peggingRef!.value = [
      ...planningSpies.peggingRef!.value,
      { suggestionId: 'suggestion-002', demandSourceReference: 'SO-OTHER', peggingType: 'demand' },
    ]
    await nextTick()
    expect(wrapper.text()).toContain('已下达')
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '定位建议')!
      .trigger('click')
    const focusedSuggestion = wrapper.findAll('.cell-skuCode.bg-primary\\/10')
    expect(focusedSuggestion).toHaveLength(1)
    expect(focusedSuggestion[0]!.text()).toContain('FG-SHOCK')
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '定位追溯')!
      .trigger('click')
    expect(
      wrapper.findAll('.cell-demandSourceReference.bg-primary\\/10').map((cell) => cell.text()),
    ).toEqual(expect.arrayContaining(['SO-1001']))
    expect(wrapper.findAll('.cell-demandSourceReference.bg-primary\\/10')).toHaveLength(1)
  })

  it('建议被状态筛选暂时隐藏时从 pegging 仍可定位', async () => {
    const wrapper = mount(PlanningWorkbench)
    planningSpies.suggestionsRef!.value = planningSpies.suggestionsRef!.value.filter(
      (row) => row.suggestionId !== 'suggestion-001',
    )
    planningSpies.suggestionFiltersRef!.status = 'open'
    await nextTick()

    await wrapper
      .findAll('button')
      .find((button) => button.text() === '定位建议')!
      .trigger('click')
    expect(planningSpies.suggestionFiltersRef!.status).toBe('all')
    planningSpies.suggestionsRef!.value = [
      ...planningSpies.suggestionsRef!.value,
      { suggestionId: 'suggestion-001', runId: 'run-001', skuCode: 'FG-SHOCK', status: 'Accepted' },
    ]
    await nextTick()
    const focusedSuggestion = wrapper.findAll('.cell-skuCode.bg-primary\\/10')
    expect(focusedSuggestion).toHaveLength(1)
    expect(focusedSuggestion[0]!.text()).toContain('FG-SHOCK')
  })

  it('只在最近完成的 MRP 有后续需求变更时显示过期横幅', async () => {
    const wrapper = mount(PlanningWorkbench)
    planningSpies.mrpRunsRef!.value = [
      { runId: 'retrying', status: 'Running', demandChangeCount: 9 },
      { runId: 'completed', status: 'Completed', demandChangeCount: 3 },
      { runId: 'older', status: 'Completed', demandChangeCount: 7 },
    ]
    await nextTick()
    expect(wrapper.get('[role="alert"]').text()).toContain('MRP 结果已过期（3 条需求变更）')

    planningSpies.mrpRunsRef!.value = [
      { runId: 'completed', status: 'Completed', demandChangeCount: 0 },
    ]
    await nextTick()
    expect(wrapper.text()).not.toContain('MRP 结果已过期')

    planningSpies.mrpRunsRef!.value = [{ runId: 'running', status: 'Running' }]
    await nextTick()
    expect(wrapper.text()).not.toContain('MRP 结果已过期')
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
    expect(wrapper.get('[label="需求覆盖率"]').attributes('value')).toBe('100')

    planningSpies.mrpRunsRef!.value = [
      { runId: 'run-running', status: 'Running', horizonEnd: '2026-07-31' },
      { runId: 'run-001', status: 'Completed', horizonEnd: '2026-06-30' },
    ]
    await nextTick()
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '查看追溯')!
      .trigger('click')
    expect(wrapper.get('[label="需求覆盖率"]').attributes('value')).toBe('100')
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

  it('建议页默认按运行顺序显示最近完成批次，切换历史后显示作废与继任关系', async () => {
    const wrapper = mount(PlanningWorkbench)
    planningSpies.mrpRunsRef!.value = [
      {
        runId: 'run-running',
        status: 'Running',
        horizonStart: '2026-05-01',
        horizonEnd: '2026-05-31',
      },
      {
        runId: 'run-new',
        status: 'Completed',
        horizonStart: '2026-04-01',
        horizonEnd: '2026-04-30',
      },
      {
        runId: 'run-old',
        status: 'Completed',
        horizonStart: '2026-06-01',
        horizonEnd: '2026-06-30',
      },
    ]
    planningSpies.suggestionsRef!.value = [
      {
        suggestionId: 'new-1',
        runId: 'run-new',
        suggestionType: 'planned-work-order',
        skuCode: 'SKU-NEW',
        status: 'Open',
      },
      {
        suggestionId: 'old-1',
        runId: 'run-old',
        suggestionType: 'planned-work-order',
        skuCode: 'SKU-OLD',
        status: 'Superseded',
        supersededByRunId: 'run-new',
      },
      {
        suggestionId: 'old-2',
        runId: 'run-old',
        suggestionType: 'planned-purchase',
        skuCode: 'SKU-ACCEPTED',
        status: 'Accepted',
      },
      {
        suggestionId: 'old-3',
        runId: 'run-old',
        suggestionType: 'planned-purchase',
        skuCode: 'SKU-REJECTED',
        status: 'Rejected',
      },
    ]
    await nextTick()

    expect(wrapper.get('[data-select-value="run-new"]').text()).toContain('2026-04-01')
    expect(
      wrapper
        .findAll('.cell-skuCode')
        .map((cell) => cell.text())
        .join(' '),
    ).toContain('SKU-NEW')
    expect(
      wrapper
        .findAll('.cell-skuCode')
        .map((cell) => cell.text())
        .join(' '),
    ).not.toContain('SKU-OLD')

    await wrapper.get('[data-select-value="run-old"]').trigger('click')
    expect(
      wrapper
        .findAll('.cell-skuCode')
        .map((cell) => cell.text())
        .join(' '),
    ).toContain('SKU-OLD')
    expect(
      wrapper
        .findAll('.cell-skuCode')
        .map((cell) => cell.text())
        .join(' '),
    ).not.toContain('SKU-NEW')
    expect(wrapper.text()).toContain('已被替代')
    expect(wrapper.text()).toContain('2026-04-01')
    const supersededRow = wrapper
      .findAll('.cell-skuCode')
      .find((cell) => cell.text().includes('SKU-OLD'))!.element.parentElement!
    const supersededActions = supersededRow.querySelector('.cell-actions')!
    expect(supersededActions.textContent).not.toContain('接受')
    expect(supersededActions.textContent).not.toContain('拒绝')
    expect(supersededActions.querySelectorAll('button')).toHaveLength(1)
    expect(supersededActions.textContent).toContain('定位追溯')
    expect(wrapper.text()).toContain('SKU-ACCEPTED')
    expect(wrapper.text()).toContain('SKU-REJECTED')
  })

  it('建议先于运行列表刷新时不显示错误的继任运行标签', async () => {
    const wrapper = mount(PlanningWorkbench)
    planningSpies.suggestionsRef!.value = [
      {
        suggestionId: 'old-1',
        runId: 'run-001',
        suggestionType: 'planned-work-order',
        skuCode: 'SKU-OLD',
        status: 'Superseded',
        supersededByRunId: 'run-new',
      },
    ]
    await nextTick()

    const row = wrapper.findAll('.cell-skuCode').find((cell) => cell.text().includes('SKU-OLD'))!
      .element.parentElement!
    expect(row.querySelector('.cell-status')?.textContent).toContain('后续 MRP 运行')
    expect(row.querySelector('.cell-status')?.textContent).not.toContain('选择一次运行')

    planningSpies.mrpRunsRef!.value = [
      {
        runId: 'run-new',
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
    await nextTick()
    await wrapper.get('[data-select-value="run-001"]').trigger('click')
    const refreshedRow = wrapper
      .findAll('.cell-skuCode')
      .find((cell) => cell.text().includes('SKU-OLD'))!.element.parentElement!
    expect(refreshedRow.querySelector('.cell-status')?.textContent).toContain('2026-07-01')
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

  it('编辑草稿主计划行后保存更新同一行', async () => {
    const wrapper = mount(PlanningWorkbench)
    planningSpies.mpsBucketsRef!.value = [
      {
        mpsId: 'mps-001',
        skuCode: 'SKU-FG-1000',
        uomCode: 'pcs',
        siteCode: 'SITE-01',
        bucketDate: '2026-06-15',
        quantity: 10,
        status: 'Draft',
      },
    ]
    await flushPromises()

    await wrapper.get('[aria-label="编辑主计划行 SKU-FG-1000"]').trigger('click')
    expect(wrapper.text()).toContain('编辑主计划行')
    expect(planningSpies.mpsFormRef!.quantity).toBe(10)
    await wrapper.get('#mps-qty').setValue('12')
    expect(planningSpies.mpsFormRef!.quantity).toBe(12)
    const submittedQuantities: number[] = []
    planningSpies.updateMpsBucket.mockImplementationOnce(async () => {
      submittedQuantities.push(planningSpies.mpsFormRef!.quantity)
    })
    await wrapper
      .findAll('form')
      .find((form) => form.find('#mps-qty').exists())!
      .trigger('submit')

    expect(planningSpies.updateMpsBucket).toHaveBeenCalledWith('mps-001')
    expect(submittedQuantities).toEqual([12])
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

  it('renders backend net requirement explanation instead of recalculating MRP in the browser', () => {
    const wrapper = mount(PlanningWorkbench)

    expect(wrapper.text()).toContain('净需求公式')
    expect(wrapper.text()).toContain('10 - 6 - 0 = 4')
    expect(wrapper.text()).toContain('需求来源')
    expect(wrapper.text()).toContain('组件毛需求')
    // #1418 顺带项：scrap/yield 英文码说人话——公式只保留算式，比率以中文百分比呈现。
    expect(wrapper.text()).toContain('废品率 10%')
    expect(wrapper.text()).toContain('良率 80%')
    expect(wrapper.text()).toContain('废品率 / 良率已计入组件毛需求')
    expect(wrapper.text()).not.toContain('scrap/yield')
    expect(wrapper.text()).toContain('27.5 - 0 - 0 = 27.5')
    expect(wrapper.text()).toContain('SO-1001')
  })

  it('maps the demand source reference into the shared urgency badge', () => {
    const wrapper = mount(PlanningWorkbench)

    const badge = wrapper.find('[data-testid="order-urgency"]')
    expect(badge.exists()).toBe(true)
    expect(badge.attributes('data-ref')).toBe('SO-DEMO-001')
    expect(badge.attributes('data-mode')).toBe('level')
  })

  it('mounts the time-phased panel and run distribution chart scoped to a single run', () => {
    const wrapper = mount(PlanningWorkbench)

    // 时段视图建议序列锁定选中的运行（跨运行求和会重复计数）。
    expect(wrapper.get('[data-testid="time-phased-panel"]').attributes('data-run-id')).toBe(
      'run-001',
    )
    expect(wrapper.get('[data-testid="run-suggestion-chart"]').attributes('data-run-id')).toBe(
      'run-001',
    )
  })

  it('allows accepting scheduled receipt changes while keeping unrelated exceptions pending', () => {
    const wrapper = mount(PlanningWorkbench)

    expect(wrapper.text()).toContain('延期调整')
    expect(wrapper.findAll('button').filter((button) => button.text() === '接受')).toHaveLength(3)
    // 拒绝对所有 Open 建议可用（含异常类），3 条 Open 行各一个。
    expect(wrapper.findAll('button').filter((button) => button.text() === '拒绝')).toHaveLength(3)
  })

  it('requires explicit confirmation before cancelling a scheduled receipt', async () => {
    const wrapper = mount(PlanningWorkbench)
    planningSpies.suggestionsRef!.value = [
      {
        suggestionId: 'cancel-001',
        runId: 'run-001',
        suggestionType: 'cancel',
        skuCode: 'SKU-001',
        uomCode: 'pcs',
        siteCode: 'SITE-01',
        quantity: 2,
        requiredDate: '2026-06-20',
        status: 'Open',
        reasonCode: 'scheduled-receipt-unneeded',
        netRequirementExplanation: null,
      },
    ]
    await nextTick()

    await wrapper
      .findAll('button')
      .find((button) => button.text() === '接受')!
      .trigger('click')
    expect(planningSpies.acceptSuggestion).not.toHaveBeenCalled()

    await wrapper
      .findAll('button')
      .find((button) => button.text() === '返回')!
      .trigger('click')
    expect(planningSpies.acceptSuggestion).not.toHaveBeenCalled()

    await wrapper
      .findAll('button')
      .find((button) => button.text() === '接受')!
      .trigger('click')
    planningSpies.acceptSuggestion.mockResolvedValue({
      data: {
        downstreamService: 'BusinessMes',
        downstreamDocumentType: 'WorkOrder',
        downstreamDocumentId: 'WO-001',
      },
    })
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '确认取消并接受')!
      .trigger('click')
    await flushPromises()
    expect(planningSpies.acceptSuggestion).toHaveBeenCalledOnce()
    expect(routerPush).not.toHaveBeenCalled()
  })

  it('shows release, availability and overdue receipt exceptions in business language', async () => {
    const wrapper = mount(PlanningWorkbench)
    planningSpies.suggestionsRef!.value = [
      {
        suggestionId: 'release',
        runId: 'run-001',
        suggestionType: 'release-date-past',
        skuCode: 'FG-SHOCK',
        uomCode: 'pcs',
        siteCode: 'SITE-01',
        quantity: 4,
        requiredDate: '2026-06-01',
        status: 'Open',
        reasonCode: 'lead-time-insufficient',
      },
      {
        suggestionId: 'negative',
        runId: 'run-001',
        suggestionType: 'negative-availability',
        skuCode: 'RM-SHOCK',
        uomCode: 'pcs',
        siteCode: 'SITE-01',
        quantity: 3,
        requiredDate: '2026-06-01',
        status: 'Open',
        reasonCode: 'negative-availability',
        netRequirementExplanation: {
          formula: '可用量 -3 低于 0',
          primarySourceType: 'negative-availability',
        },
      },
      {
        suggestionId: 'overdue',
        runId: 'run-001',
        suggestionType: 'overdue-receipt',
        skuCode: 'RM-SHOCK',
        uomCode: 'pcs',
        siteCode: 'SITE-01',
        quantity: 5,
        requiredDate: '2026-06-01',
        status: 'Open',
        reasonCode: 'scheduled-receipt-overdue',
        netRequirementExplanation: {
          formula: '在途 5 应于 2026-05-20 到货，已早于计划开始日 2026-05-25',
          primarySourceType: 'scheduled-receipt',
        },
      },
      {
        suggestionId: 'safety',
        runId: 'run-001',
        suggestionType: 'planned-purchase',
        skuCode: 'RM-SHOCK',
        uomCode: 'pcs',
        siteCode: 'SITE-01',
        quantity: 2,
        requiredDate: '2026-06-30',
        status: 'Open',
        reasonCode: 'safety-stock-replenishment',
      },
    ]
    await nextTick()

    const typeLabels = wrapper.findAll('.cell-suggestionType').map((cell) => cell.text())
    expect(typeLabels).toContain('释放日已过')
    expect(typeLabels).toContain('负可用')
    expect(typeLabels).toContain('超期在途')
    expect(wrapper.text()).toContain('提前期不足')
    expect(wrapper.text()).toContain('可用量 -3 低于 0')
    expect(wrapper.text()).toContain('负可用来源')
    expect(wrapper.text()).toContain('在途 5 应于 2026-05-20 到货')
    expect(wrapper.text()).toContain('例外说明')
    expect(wrapper.text()).toContain('安全库存低于下限，建议补货')
    expect(wrapper.findAll('button').filter((button) => button.text() === '接受')).toHaveLength(1)
  })

  it('已承接成 MES 工单的建议行给出「对该单排产」入口（MAN-694 / #1262）', () => {
    useAuthStore().$patch({
      principal: { permissionCodes: ['business.scheduling.plans.manage'] },
    } as never)
    const wrapper = mount(PlanningWorkbench)

    const entries = wrapper.findAll('[data-testid="planning-suggestion-schedule-single"]')
    expect(entries).toHaveLength(1)
    expect(entries[0]!.attributes('title')).toContain('WO-2026-0007')
    expect(entries[0]!.attributes('disabled')).toBeUndefined()
  })

  it('没有排产管理权限时入口禁用并说明原因，而不是直接消失', () => {
    const wrapper = mount(PlanningWorkbench)

    const entry = wrapper.get('[data-testid="planning-suggestion-schedule-single"]')
    expect(entry.attributes('disabled')).toBeDefined()
    expect(entry.attributes('title')).toContain('没有排产管理权限')
  })

  it('未承接工单的建议行不给排产入口，Open 行仍只是「接受 / 拒绝」', () => {
    const wrapper = mount(PlanningWorkbench)

    // 3 条 Open 行走接受/拒绝分支，不出现排产入口，也不出现「未承接工单」说明。
    expect(wrapper.findAll('[data-testid="planning-suggestion-schedule-single"]')).toHaveLength(1)
    expect(wrapper.text()).not.toContain('未承接工单，暂不能排产')
  })

  // MAN-700 / #1289：RunMrp 500 曾把英文 `Internal Server Error` 常驻在页面上，
  // 且 submitMrpRun 没有 try/catch，异常逃逸后弹框永远关不掉。
  it('RunMrp 500 只走 toast 人话，英文原文既不上屏也不留常驻错误条', async () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {})
    planningSpies.runMrp = vi.fn(async () => {
      throw { title: 'Internal Server Error', status: 500 }
    })
    const wrapper = mount(PlanningWorkbench)

    // 未捕获就会变成 unhandled rejection（vitest 直接判错），本用例同时守住「弹框不卡死」。
    await wrapper.findAll('form')[0]!.trigger('submit')
    await Promise.resolve()

    expect(planningSpies.toastError).toHaveBeenCalledWith('运行 MRP 失败，请稍后重试。')
    expect(planningSpies.toastError).not.toHaveBeenCalledWith(
      expect.stringContaining('Internal Server'),
    )
    expect(wrapper.text()).not.toContain('Internal Server Error')
    consoleError.mockRestore()
  })

  it('RunMrp 被后端按领域理由拒绝时原样透传那句中文', async () => {
    planningSpies.runMrp = vi.fn(async () => {
      throw { status: 400, detail: '计划期内没有已发布的主计划行' }
    })
    const wrapper = mount(PlanningWorkbench)

    await wrapper.findAll('form')[0]!.trigger('submit')
    await Promise.resolve()

    expect(planningSpies.toastError).toHaveBeenCalledWith(
      '运行 MRP 失败：计划期内没有已发布的主计划行',
    )
  })

  // #1306 异步任务模式：提交=受理，弹框全程可关闭，后台跑完由 watch 统一 toast。
  it('RunMrp 提交即受理并提示后台计算中', async () => {
    const wrapper = mount(PlanningWorkbench)

    await wrapper.findAll('form')[0]!.trigger('submit')
    await Promise.resolve()

    expect(planningSpies.runMrp).toHaveBeenCalled()
    expect(planningSpies.toastSuccess).toHaveBeenCalledWith(
      'MRP 已受理，正在后台计算，完成后自动刷新。',
    )
    expect(planningSpies.toastError).not.toHaveBeenCalled()
  })

  it('轮询到完成态时 toast 建议数并收起弹框', async () => {
    const wrapper = mount(PlanningWorkbench)

    planningSpies.activeMrpRun.runId = 'run-async-1'
    planningSpies.activeMrpRun.status = 'completed'
    planningSpies.activeMrpRun.suggestionCount = 5
    await wrapper.vm.$nextTick()

    expect(planningSpies.toastSuccess).toHaveBeenCalledWith('MRP 计算完成，共生成 5 条计划建议。')
  })

  it('MRP 自动重试期间不报最终失败，重试成功只通知完成', async () => {
    const wrapper = mount(PlanningWorkbench)
    planningSpies.activeMrpRun.runId = 'run-retrying'
    planningSpies.activeMrpRun.status = 'running'
    await wrapper.vm.$nextTick()
    expect(planningSpies.toastError).not.toHaveBeenCalled()

    planningSpies.activeMrpRun.status = 'completed'
    planningSpies.activeMrpRun.suggestionCount = 2
    await wrapper.vm.$nextTick()
    expect(planningSpies.toastSuccess).toHaveBeenCalledWith('MRP 计算完成，共生成 2 条计划建议。')
    expect(planningSpies.toastError).not.toHaveBeenCalled()
  })

  it('轮询到失败态时把 failureReason 走分层透传上屏', async () => {
    const wrapper = mount(PlanningWorkbench)

    planningSpies.activeMrpRun.runId = 'run-async-1'
    planningSpies.activeMrpRun.failureReason = 'MRP 计算失败：上游库存快照不可用。'
    planningSpies.mrpRunsRef!.value = [
      {
        runId: 'run-async-1',
        status: 'Failed',
        failureReason: planningSpies.activeMrpRun.failureReason,
      },
    ]
    planningSpies.activeMrpRun.status = 'failed'
    await wrapper.vm.$nextTick()

    // 后端前缀被去重：不出现「MRP 计算失败：MRP 计算失败：…」的叠层。
    expect(planningSpies.toastError).toHaveBeenCalledWith('MRP 计算失败：上游库存快照不可用。')
    expect(wrapper.find('.cell-status').text()).toContain('MRP 计算失败：上游库存快照不可用。')
  })

  it('轮询超时只提醒去运行列表回看，不按失败处理', async () => {
    const wrapper = mount(PlanningWorkbench)

    planningSpies.activeMrpRun.runId = 'run-async-1'
    planningSpies.activeMrpRun.status = 'polling-timeout'
    await wrapper.vm.$nextTick()

    expect(planningSpies.toastWarning).toHaveBeenCalledWith(
      'MRP 仍在后台计算，可稍后在「MRP 运行」列表查看结果。',
    )
    expect(planningSpies.toastError).not.toHaveBeenCalled()
  })
})
