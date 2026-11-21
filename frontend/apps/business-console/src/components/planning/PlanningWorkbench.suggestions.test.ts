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
import { useAuthStore } from '@/stores/auth'

describe('PlanningWorkbench', () => {
  beforeEach(resetPlanningWorkbench)

  // Regression / DomainInvariant: #4417 accepted WO continuity and source identity.
  it('已承接建议的真实工单单号可进入同工单详情', async () => {
    useAuthStore().principal = {
      permissionCodes: ['business.planning.mrp.read', 'business.mes.work-orders.read'],
    }
    const wrapper = mount(PlanningWorkbench)
    const link = wrapper
      .findAll('.cell-downstream button')
      .find((button) => button.text() === 'WO-2026-0007')!
    expect(link).toBeDefined()
    await link.trigger('click')
    expect(routerPush).toHaveBeenCalledWith({ path: '/mes/work-orders/WO-2026-0007' })
  })

  it('无工单读权或非工单承接时保留单号文本', async () => {
    const wrapper = mount(PlanningWorkbench)
    expect(wrapper.findAll('.cell-downstream button')).toHaveLength(0)
    expect(wrapper.text()).toContain('WO-2026-0007')
    useAuthStore().principal = {
      permissionCodes: ['business.planning.mrp.read', 'business.mes.work-orders.read'],
    }
    planningSpies.suggestionsRef!.value = [
      {
        suggestionId: 'purchase-1',
        runId: 'run-001',
        status: 'Accepted',
        downstreamService: 'business-erp',
        downstreamDocumentType: 'purchase-order',
        downstreamDocumentId: 'PO-1001',
      },
    ]
    await nextTick()
    expect(wrapper.findAll('.cell-downstream button')).toHaveLength(0)
    expect(wrapper.text()).toContain('PO-1001')
  })

  it('来源建议定位在异步读回后显示并高亮指定历史运行的原建议', async () => {
    planningRoute.query = { suggestionId: 'source-old', runId: 'run-old' }
    const wrapper = mount(PlanningWorkbench)
    planningSpies.mrpRunsRef!.value = [
      { runId: 'run-new', status: 'Completed' },
      { runId: 'run-old', status: 'Completed' },
    ]
    planningSpies.suggestionsRef!.value = [
      { suggestionId: 'new-1', runId: 'run-new', skuCode: 'FG-NEW', status: 'Open' },
      { suggestionId: 'source-old', runId: 'run-old', skuCode: 'FG-ORIGINAL', status: 'Accepted' },
    ]
    await nextTick()
    const cells = wrapper.findAll('.cell-skuCode')
    expect(cells.map((cell) => cell.text())).not.toContain('FG-NEWFG-NEW')
    const source = cells.filter((cell) => cell.classes().includes('bg-primary/10'))
    expect(source).toHaveLength(1)
    expect(source[0]!.text()).toContain('FG-ORIGINAL')
    expect(planningSpies.suggestionFiltersRef!.status).toBe('all')
    expect(wrapper.get('[data-active-tab]').attributes('data-active-tab')).toBe('suggestions')
  })

  it('工单来源仅带建议强ID时解析该建议所属运行，不使用最近运行', async () => {
    planningRoute.query = { suggestionId: 'source-old' }
    const wrapper = mount(PlanningWorkbench)
    planningSpies.mrpRunsRef!.value = [
      { runId: 'run-new', status: 'Completed' },
      { runId: 'run-old', status: 'Completed' },
    ]
    planningSpies.suggestionsRef!.value = [
      { suggestionId: 'source-old', runId: 'run-old', skuCode: 'FG-ORIGINAL', status: 'Accepted' },
    ]
    await nextTick()
    expect(wrapper.findAll('.cell-skuCode.bg-primary\\/10')).toHaveLength(1)
    expect(wrapper.find('[data-select-value="run-old"]').exists()).toBe(true)
  })

  it('来源位于批次第十三行时仍在建议首屏可见', async () => {
    planningRoute.query = { suggestionId: 'source-last' }
    const wrapper = mount(PlanningWorkbench)
    planningSpies.suggestionsRef!.value = [
      ...Array.from({ length: 12 }, (_, index) => ({
        suggestionId: `other-${index}`,
        runId: 'run-001',
        skuCode: `FG-${index}`,
        status: 'Open',
      })),
      { suggestionId: 'source-last', runId: 'run-001', skuCode: 'FG-ORIGINAL', status: 'Accepted' },
    ]
    await nextTick()
    const cells = wrapper.findAll('.cell-skuCode').filter((cell) => cell.text().startsWith('FG-'))
    expect(cells[0]!.text()).toContain('FG-ORIGINAL')
    expect(cells[0]!.classes()).toContain('bg-primary/10')
  })

  it('仅运行来源显示指定历史运行，不高亮任何虚构建议', async () => {
    planningRoute.query = { runId: 'run-old' }
    const wrapper = mount(PlanningWorkbench)
    planningSpies.mrpRunsRef!.value = [
      { runId: 'run-new', status: 'Completed' },
      { runId: 'run-old', status: 'Completed' },
    ]
    planningSpies.suggestionsRef!.value = [
      { suggestionId: 'old-1', runId: 'run-old', skuCode: 'FG-ORIGINAL' },
      { suggestionId: 'new-1', runId: 'run-new', skuCode: 'FG-NEW' },
    ]
    await nextTick()
    expect(wrapper.text()).toContain('FG-ORIGINAL')
    expect(wrapper.text()).not.toContain('FG-NEW')
    expect(wrapper.findAll('.cell-skuCode.bg-primary\\/10')).toHaveLength(0)
    expect(wrapper.get('[data-active-tab]').attributes('data-active-tab')).toBe('suggestions')
  })

  it('建议与运行来源不匹配时不冒认另一运行的原建议', async () => {
    planningRoute.query = { suggestionId: 'source-old', runId: 'run-new' }
    const wrapper = mount(PlanningWorkbench)
    planningSpies.mrpRunsRef!.value = [
      { runId: 'run-new', status: 'Completed' },
      { runId: 'run-old', status: 'Completed' },
    ]
    planningSpies.suggestionsRef!.value = [
      { suggestionId: 'source-old', runId: 'run-old', skuCode: 'FG-ORIGINAL' },
    ]
    await nextTick()
    expect(wrapper.text()).not.toContain('FG-ORIGINAL')
    expect(wrapper.findAll('.cell-skuCode.bg-primary\\/10')).toHaveLength(0)
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
      principal: {
        permissionCodes: ['business.planning.mrp.read', 'business.scheduling.plans.manage'],
      },
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
})
