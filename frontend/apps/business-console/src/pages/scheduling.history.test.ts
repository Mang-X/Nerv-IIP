import {
  routeStub,
  stub,
  planTimeContextError,
  factoryTimezone,
  assemblyTimezone,
  savedDrafts,
  detailSelection,
  historyEmpty,
  noPlans,
  authState,
  planOneInvalidated,
  historyFilters,
  detailError,
  planOne,
  layoutStub,
  sheetStubs,
  clickConfirmRevoke,
  openPlanTable,
} from './scheduling.fixture'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { describe, expect, it, vi } from 'vitest'
import SchedulingPage from './scheduling.vue'
import type { BusinessConsoleSchedulePlan } from '@nerv-iip/api-client'

describe('APS scheduling workbench page', () => {
  it.each([false, true])(
    '甘特零方案可返回总览，MRP 读权控制生产建议入口（%s）',
    async (canReadMrp) => {
      noPlans.value = true
      if (canReadMrp) authState.permissionCodes.push('business.planning.mrp.read')
      const wrapper = mount(SchedulingPage, {
        global: {
          plugins: [createPinia()],
          stubs: {
            ...layoutStub,
            RouterLink: { name: 'RouterLink', props: ['to'], template: '<a><slot /></a>' },
          },
        },
      })
      await flushPromises()
      const tab = wrapper.findAll('[role="tab"]').find((item) => item.text() === '甘特图')!
      await tab.trigger('focus')
      await tab.trigger('mousedown')
      await flushPromises()
      expect(wrapper.text()).toContain('暂无排程方案')
      const link = wrapper.findComponent({ name: 'RouterLink' })
      if (canReadMrp) {
        expect(link.text()).toBe('查看生产建议')
        expect(link.props('to')).toEqual({
          path: '/planning',
          query: { suggestionType: 'planned-work-order' },
        })
      } else {
        expect(wrapper.text()).not.toContain('查看生产建议')
      }
      await wrapper
        .findAll('button')
        .find((item) => item.text() === '返回排程总览')!
        .trigger('click')
      expect(wrapper.get('[role="tab"][aria-selected="true"]').text()).toBe('排程总览')
      wrapper.unmount()
    },
  )

  it('shows server total, real horizons and controlled pagination in the history table', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()
    await openPlanTable(wrapper)

    const table = wrapper.findComponent({ name: 'NvDataTable' })
    expect(table.props('manual')).toBe(true)
    expect(table.props('totalItems')).toBe(137)
    expect(wrapper.text()).toContain('2026-09-01')
    table.vm.$emit('update:page', 2)
    await flushPromises()
    expect(table.props('page')).toBe(2)
    expect(wrapper.text()).toContain('137 个方案')
    expect(wrapper.text()).toContain('发布日（UTC）')
    expect(wrapper.text()).toContain('发布时间从新到旧')
    expect(wrapper.text()).not.toContain('明细中确认')
    expect(wrapper.text()).toContain('工序数')
    expect(wrapper.text()).not.toContain('资源 / 工序')
  })

  it('distinguishes filtered no-match results and lets the user clear all server filters', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()
    await openPlanTable(wrapper)
    historyFilters.status = 'revoked'
    historyFilters.releasedOn = '2026-09-30'
    historyFilters.isInvalidated = true
    historyEmpty.value = true
    await flushPromises()
    expect(wrapper.text()).toContain('没有符合条件的方案')
    expect(wrapper.text()).not.toContain('还没有排程方案')
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '清空筛选')!
      .trigger('click')
    expect(historyFilters.status).toBeUndefined()
    expect(historyFilters.releasedOn).toBe('')
    expect(historyFilters.isInvalidated).toBeUndefined()
    wrapper.unmount()
  })

  it.each(['page', 'filter'])(
    'keeps selected released plan revocable after its summary leaves the %s window',
    async (change) => {
      const wrapper = mount(SchedulingPage, {
        global: { plugins: [createPinia()], stubs: { ...layoutStub, ...sheetStubs } },
      })
      await flushPromises()
      await openPlanTable(wrapper)
      const row = wrapper.findAll('tbody tr').find((item) => item.text().includes('plan-released'))!
      await row
        .findAll('button')
        .find((button) => button.text().includes('明细'))!
        .trigger('click')
      await flushPromises()
      if (change === 'page') {
        wrapper.findComponent({ name: 'NvDataTable' }).vm.$emit('update:page', 2)
      } else {
        historyFilters.status = 'revoked'
        historyEmpty.value = true
      }
      await flushPromises()
      const tab = wrapper.findAll('[role="tab"]').find((item) => item.text().includes('甘特图'))!
      await tab.trigger('focus')
      await tab.trigger('mousedown')
      await flushPromises()
      expect(detailSelection.planId).toBe('plan-released')
      const revoke = wrapper.findAll('button').find((button) => button.text().includes('撤销发布'))
      expect(revoke).toBeDefined()
      await revoke!.trigger('click')
      await flushPromises()
      expect(stub.revokePlan).not.toHaveBeenCalled()
      expect(document.body.textContent).toContain('确认撤销发布该排程方案？')
      clickConfirmRevoke()
      await flushPromises()
      expect(stub.revokePlan).toHaveBeenCalledWith('plan-released')
      wrapper.unmount()
    },
  )

  it.each(['page', 'filter'])(
    'publishes the selected generated plan after its summary leaves the %s window',
    async (change) => {
      const wrapper = mount(SchedulingPage, {
        global: { plugins: [createPinia()], stubs: { ...layoutStub, ...sheetStubs } },
      })
      await flushPromises()
      await openPlanTable(wrapper)
      const row = wrapper.findAll('tbody tr').find((item) => item.text().includes('plan-001'))!
      await row
        .findAll('button')
        .find((button) => button.text().includes('明细'))!
        .trigger('click')
      await flushPromises()
      if (change === 'page')
        wrapper.findComponent({ name: 'NvDataTable' }).vm.$emit('update:page', 2)
      else {
        historyFilters.status = 'revoked'
        historyEmpty.value = true
      }
      await flushPromises()
      const tab = wrapper.findAll('[role="tab"]').find((item) => item.text().includes('甘特图'))!
      await tab.trigger('focus')
      await tab.trigger('mousedown')
      await flushPromises()
      const publish = wrapper
        .findAll('button')
        .find((button) => button.text().includes('发布当前方案'))!
      expect(publish.attributes('disabled')).toBeUndefined()
      await publish.trigger('click')
      await flushPromises()
      expect(stub.releasePlan).toHaveBeenCalledWith('plan-001')
      wrapper.unmount()
    },
  )

  it.each(['plan-invalid', 'plan-released'])(
    'preserves the selected %s release restriction after filtering away its summary',
    async (planId) => {
      const wrapper = mount(SchedulingPage, {
        global: { plugins: [createPinia()], stubs: { ...layoutStub, ...sheetStubs } },
      })
      await flushPromises()
      await openPlanTable(wrapper)
      const row = wrapper.findAll('tbody tr').find((item) => item.text().includes(planId))!
      await row
        .findAll('button')
        .find((button) => button.text().includes('明细'))!
        .trigger('click')
      await flushPromises()
      historyFilters.status = 'revoked'
      historyEmpty.value = true
      await flushPromises()
      const tab = wrapper.findAll('[role="tab"]').find((item) => item.text().includes('甘特图'))!
      await tab.trigger('focus')
      await tab.trigger('mousedown')
      await flushPromises()
      const publish = wrapper
        .findAll('button')
        .find((button) => button.text().includes('发布当前方案'))!
      expect(publish.attributes('disabled')).toBeDefined()
      expect(stub.releasePlan).not.toHaveBeenCalled()
      if (planId === 'plan-invalid') expect(publish.attributes('title')).toContain('设备不可用')
      wrapper.unmount()
    },
  )

  it('renders the selected APS plan as a read-only resource timeline', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: { ...layoutStub } },
    })
    await flushPromises()

    const ganttTab = wrapper.findAll('[role="tab"]').find((tab) => tab.text().includes('甘特图'))!
    await ganttTab.trigger('focus')
    await ganttTab.trigger('mousedown')
    await flushPromises()

    expect(detailSelection.planId).toBe('plan-001')
    expect(wrapper.find('[data-testid="readonly-schedule-timeline"]').exists()).toBe(true)
    expect(wrapper.findAll('[data-resource-lane]')).toHaveLength(2)
    expect(wrapper.findAll('[data-task-id]')).toHaveLength(5)
    expect(wrapper.findAll('[data-conflict="true"]')).toHaveLength(1)
    expect(wrapper.findAll('[data-locked="true"]')).toHaveLength(1)
    // 时间刻度控件改由包内 SchedulingToolbar 提供（#1399 M4）：此前是页面手搓的
    // 「自动适配 / 班次级 / 日级」三个按钮，只有 3 档；现在挂的是引擎那套完整 5 档下拉。
    // 断言相应改成「刻度控件在、四档真实刻度都可选」——要考的是能不能换时间线，
    // 不是按钮上写的哪两个字。
    expect(wrapper.find('[aria-label="时间刻度"]').exists()).toBe(true)
    expect(wrapper.findAllComponents({ name: 'NvSelectItem' }).map((item) => item.text())).toEqual(
      expect.arrayContaining(['自适应', '小时', '日', '周', '月']),
    )
    expect(wrapper.text()).toContain('冲突')
    expect(wrapper.text()).toContain('锁定')
    expect(wrapper.text()).not.toContain('甘特可视化待接入')
  })

  it('opens the clicked operation detail beside the Gantt instead of the whole-plan drawer', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: { ...layoutStub } },
    })
    await flushPromises()

    const ganttTab = wrapper.findAll('[role="tab"]').find((tab) => tab.text().includes('甘特图'))!
    await ganttTab.trigger('focus')
    await ganttTab.trigger('mousedown')
    await flushPromises()

    expect(wrapper.find('[data-testid="scheduling-task-detail"]').exists()).toBe(false)

    await wrapper.find('[data-task-id="assign-002"]').trigger('click')
    await flushPromises()

    const detailPanel = wrapper.find('[data-testid="scheduling-task-detail"]')
    expect(detailPanel.exists()).toBe(true)
    // 粒度要对：点的是一道工序，标题与字段就走工序形态（工单汇总行/资源时间块另有形态）。
    expect(detailPanel.attributes('data-detail-kind')).toBe('operation')
    expect(detailPanel.text()).toContain('工序详情')
    // 工序级事实：工单、工序、资源、时间、锁定状态、工单级物料。
    expect(detailPanel.text()).toContain('WO-20260701-001')
    expect(detailPanel.text()).toContain('OP-20')
    expect(detailPanel.text()).toContain('RES-CNC-01')
    expect(detailPanel.text()).toContain('已锁定')
    expect(detailPanel.text()).toContain('SKU-PISTON-01')
    // 齐套没有权威来源：说明去哪儿看，不给估算数。
    expect(detailPanel.text()).toContain('齐套情况请到「领料与齐套」页核对')
    // 甘特没有被遮挡，仍然在场且可继续换选。
    expect(wrapper.find('[data-testid="readonly-schedule-timeline"]').exists()).toBe(true)
    expect(wrapper.text()).not.toContain('排程方案明细')

    await wrapper.find('[data-task-id="assign-003"]').trigger('click')
    await flushPromises()
    expect(wrapper.find('[data-testid="scheduling-task-detail"]').text()).toContain(
      'WO-20260701-002',
    )
  })

  it('keeps the whole-plan drawer behind the plan-level entry on the Gantt tab', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: { ...layoutStub, ...sheetStubs } },
    })
    await flushPromises()

    const ganttTab = wrapper.findAll('[role="tab"]').find((tab) => tab.text().includes('甘特图'))!
    await ganttTab.trigger('focus')
    await ganttTab.trigger('mousedown')
    await flushPromises()

    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('方案明细'))!
      .trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('排程方案明细')
    expect(wrapper.text()).toContain('资源分配')
  })

  it('does not render summaries without a plan id as Gantt selector options', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()

    const ganttTab = wrapper.findAll('[role="tab"]').find((tab) => tab.text().includes('甘特图'))!
    await ganttTab.trigger('focus')
    await ganttTab.trigger('mousedown')
    await flushPromises()

    expect(detailSelection.planId).toBe('plan-001')
    // 只数**方案选择器**里的选项。原先数的是整页 NvSelectItem 总数，甘特工具栏一挂上来
    // （#1399 M4，多 5 个刻度选项）这条就红了——它考的其实是「无 planId 的 summary 不该
    // 变成方案选项」，与页面上还有几个别的下拉无关。按选项文本前缀锁定方案选项。
    const planOptions = wrapper
      .findAllComponents({ name: 'NvSelectItem' })
      .filter((item) => item.text().startsWith('plan-'))
    expect(planOptions).toHaveLength(6)
  })

  it('opens plan detail and releases the selected plan through the composable', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: { ...layoutStub, ...sheetStubs } },
    })
    await flushPromises()
    await openPlanTable(wrapper)

    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('明细'))!
      .trigger('click')
    await flushPromises()

    expect(detailSelection.planId).toBe('plan-001')
    expect(wrapper.text()).toContain('资源分配')
    expect(wrapper.text()).toContain('RES-CNC-01')
    expect(wrapper.text()).toContain('关键物料到货晚于计划开工')
    expect(wrapper.text()).toContain('瓶颈资源产能不足')

    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('发布'))!
      .trigger('click')
    await flushPromises()

    expect(stub.releasePlan).toHaveBeenCalledWith('plan-001')
    expect(stub.toastSuccess).toHaveBeenCalled()
  })

  it('maps the assignment order id into the shared urgency badge inside plan detail', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: { ...layoutStub, ...sheetStubs } },
    })
    await flushPromises()
    await openPlanTable(wrapper)

    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('明细'))!
      .trigger('click')
    await flushPromises()

    expect(detailSelection.planId).toBe('plan-001')
    const refs = wrapper
      .findAll('[data-testid="order-urgency"]')
      .map((badge) => badge.attributes('data-ref'))
    // Assignment.orderId is the real reference fed to the shared badge.
    expect(refs).toContain('WO-20260701-001')
    expect(refs).toContain('WO-20260701-002')
    expect(wrapper.get('[data-testid="order-urgency"]').attributes('data-mode')).toBe('level')
  })

  it('consumes the order reference route and opens the matching assignment in plan detail', async () => {
    routeStub.query = { orderReference: 'WO-20260701-001' }
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: { ...layoutStub, ...sheetStubs } },
    })
    await flushPromises()

    expect(detailSelection.planId).toBe('plan-001')
    expect(wrapper.find('[data-targeted-order="true"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('已定位订单 WO-20260701-001')
  })

  it('单单排产落点：带 planId 进入页面直接打开该方案明细（MAN-694 / #1262）', async () => {
    // 不用列表首个方案（plan-001），特意点名一个靠后的方案，证明是路由说了算。
    routeStub.query = { planId: 'plan-invalid', orderReference: 'WO-20260701-004' }
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: { ...layoutStub, ...sheetStubs } },
    })
    await flushPromises()

    // 明细查询锁定路由点名的方案，抽屉已打开（而不是停在列表让用户自己找）。
    expect(detailSelection.planId).toBe('plan-invalid')
    expect(wrapper.text()).toContain('排程方案明细')
    expect(wrapper.text()).toContain('已定位订单 WO-20260701-004')
    expect(wrapper.find('[data-targeted-order="true"]').exists()).toBe(true)
  })

  it('带 planId 时不再走「逐个方案找订单」的兜底，方案选择保持路由点名的那个', async () => {
    // 明细里没有这张工单：没有 planId 时页面会翻下一个方案；点名了就不该改选。
    routeStub.query = { planId: 'plan-invalid', orderReference: 'WO-NOT-IN-PLAN' }
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: { ...layoutStub, ...sheetStubs } },
    })
    await flushPromises()

    expect(detailSelection.planId).toBe('plan-invalid')
    expect(wrapper.text()).toContain('正在定位订单 WO-NOT-IN-PLAN')
  })

  it('refreshes selected Gantt invalidation when the uninvalidated history filter removes it', async () => {
    detailSelection.planId = 'plan-001'
    historyFilters.isInvalidated = false
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()
    const ganttTab = wrapper.findAll('[role="tab"]').find((tab) => tab.text().includes('甘特图'))!
    await ganttTab.trigger('focus')
    await ganttTab.trigger('mousedown')
    await flushPromises()
    const release = wrapper
      .findAll('button')
      .find((button) => button.text().includes('发布当前方案'))!
    expect(release.attributes('disabled')).toBeUndefined()
    planOneInvalidated.value = true
    historyEmpty.value = true
    await flushPromises()
    expect(detailSelection.planId).toBe('plan-001')
    expect(wrapper.text()).toContain('方案已失效，不能从甘特发布')
    expect(release.attributes('disabled')).toBeDefined()
    wrapper.findComponent({ name: 'SchedulingPlanGantt' }).vm.$emit('release')
    await flushPromises()
    expect(stub.releasePlan).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('marks invalidated plans with their reason and blocks release', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()
    await openPlanTable(wrapper)

    // 失效方案:标记 + 失效原因列展示中文原因
    expect(wrapper.text()).toContain('已失效')
    expect(wrapper.text()).toContain('设备不可用')

    // 失效方案那一行的发布按钮被禁用(须重排后再发布)
    const rows = wrapper.findAll('tbody tr')
    const invalidRow = rows.find((row) => row.text().includes('plan-invalid'))!
    const releaseButton = invalidRow
      .findAll('button')
      .find((button) => button.text().includes('发布'))!
    expect(releaseButton.attributes('disabled')).toBeDefined()
  })

  it('localizes terminal plan statuses and explains why they cannot be released', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()
    await openPlanTable(wrapper)

    const rows = wrapper.findAll('tbody tr')
    const supersededRow = rows.find((row) => row.text().includes('plan-superseded'))!
    const revokedRow = rows.find((row) => row.text().includes('plan-revoked'))!
    const supersededRelease = supersededRow
      .findAll('button')
      .find((button) => button.text().includes('发布'))!
    const revokedRelease = revokedRow
      .findAll('button')
      .find((button) => button.text().includes('发布'))!

    expect(supersededRow.text()).toContain('已取代')
    expect(supersededRelease.attributes('disabled')).toBeDefined()
    expect(supersededRelease.attributes('title')).toBe('方案已被后续方案取代')
    expect(revokedRow.text()).toContain('已撤销')
    expect(revokedRelease.attributes('disabled')).toBeDefined()
    expect(revokedRelease.attributes('title')).toBe('方案已撤销')
  })

  it('explains invalidated, invalid-time, and missing-resource assignments in the Gantt view', async () => {
    detailSelection.planId = 'plan-invalid'
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()

    const ganttTab = wrapper.findAll('[role="tab"]').find((tab) => tab.text().includes('甘特图'))!
    await ganttTab.trigger('focus')
    await ganttTab.trigger('mousedown')
    await flushPromises()

    expect(wrapper.text()).toContain('方案已失效')
    expect(wrapper.text()).toContain('设备不可用')
    expect(wrapper.text()).toContain('1 项时间异常')
    expect(wrapper.text()).toContain('1 项缺少资源')
    expect(wrapper.findAll('[data-task-id]')).toHaveLength(1)
    const ganttPublish = wrapper
      .findAll('button')
      .find((button) => button.text().includes('发布当前方案'))!
    expect(ganttPublish.attributes('disabled')).toBeDefined()

    wrapper.findComponent({ name: 'SchedulingPlanGantt' }).vm.$emit('release')
    await flushPromises()
    expect(stub.releasePlan).not.toHaveBeenCalled()
  })

  it('shows a permission-specific Gantt error state', async () => {
    detailSelection.planId = 'plan-empty'
    detailError.value = { response: { status: 403 } }
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()

    const ganttTab = wrapper.findAll('[role="tab"]').find((tab) => tab.text().includes('甘特图'))!
    await ganttTab.trigger('focus')
    await ganttTab.trigger('mousedown')
    await flushPromises()

    expect(wrapper.text()).toContain('权限不足，无法查看该排程方案')
  })

  it('handles cyclic error causes without overflowing the render stack', async () => {
    detailSelection.planId = 'plan-empty'
    const cyclicError: Record<string, unknown> = {}
    cyclicError.cause = cyclicError
    detailError.value = cyclicError

    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()
    const ganttTab = wrapper.findAll('[role="tab"]').find((tab) => tab.text().includes('甘特图'))!
    await ganttTab.trigger('focus')
    await ganttTab.trigger('mousedown')
    await flushPromises()

    expect(wrapper.text()).toContain('排程甘特加载失败')
  })

  it('shows explicit detail feedback when a plan detail request fails', async () => {
    detailError.value = new Error('network')
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: { ...layoutStub, ...sheetStubs } },
    })
    await flushPromises()
    await openPlanTable(wrapper)

    await wrapper
      .findAll('button')
      .filter((button) => button.text().includes('明细'))[1]!
      .trigger('click')
    await flushPromises()

    expect(detailSelection.planId).toBe('plan-empty')
    expect(wrapper.text()).toContain('明细加载失败，请稍后重试')
  })

  it('revokes a released plan only after the confirmation dialog is accepted', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()
    await openPlanTable(wrapper)

    const rows = wrapper.findAll('tbody tr')
    // 撤销发布只对已发布方案开放：其他状态行不得出现该入口。
    expect(
      rows
        .find((row) => row.text().includes('plan-001'))!
        .findAll('button')
        .some((button) => button.text().includes('撤销发布')),
    ).toBe(false)

    const releasedRow = rows.find((row) => row.text().includes('plan-released'))!
    await releasedRow
      .findAll('button')
      .find((button) => button.text().includes('撤销发布'))!
      .trigger('click')
    await flushPromises()

    // 只开了确认框，还没真撤销——误触不能直接改状态。
    expect(stub.revokePlan).not.toHaveBeenCalled()
    expect(document.body.textContent).toContain('确认撤销发布该排程方案？')

    clickConfirmRevoke()
    await flushPromises()

    expect(stub.revokePlan).toHaveBeenCalledWith('plan-released')
    expect(stub.toastSuccess).toHaveBeenCalled()
    expect(stub.toastError).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('surfaces the service message and changes nothing when revoke fails', async () => {
    stub.revokePlan.mockRejectedValueOnce(new Error('方案不处于已发布状态'))
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()
    await openPlanTable(wrapper)

    const releasedRow = wrapper
      .findAll('tbody tr')
      .find((row) => row.text().includes('plan-released'))!
    await releasedRow
      .findAll('button')
      .find((button) => button.text().includes('撤销发布'))!
      .trigger('click')
    await flushPromises()
    clickConfirmRevoke()
    await flushPromises()

    // 诚实失败：透传服务端说法，不冒充成功；方案行仍留在「已发布」，用户可重试。
    expect(stub.toastError).toHaveBeenCalledWith('撤销失败：方案不处于已发布状态')
    expect(stub.toastSuccess).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('plan-released')
    wrapper.unmount()
  })

  it('surfaces the service message when releasing a plan fails', async () => {
    stub.releasePlan.mockRejectedValueOnce({ message: '方案已被后续方案取代，不能发布' })
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()
    await openPlanTable(wrapper)

    const planRow = wrapper.findAll('tbody tr').find((row) => row.text().includes('plan-001'))!
    await planRow
      .findAll('button')
      .find((button) => button.text().includes('发布'))!
      .trigger('click')
    await flushPromises()

    expect(stub.toastError).toHaveBeenCalledWith('发布失败：方案已被后续方案取代，不能发布')
  })

  it('never puts an English 500 body on screen — falls back to the domain hint', async () => {
    // 反馈规范禁止英文错误码 / 5xx 原文上屏：这类通用文案只进 console，界面用领域兜底。
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {})
    stub.releasePlan.mockRejectedValueOnce({ title: 'Internal Server Error', status: 500 })
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()
    await openPlanTable(wrapper)

    const planRow = wrapper.findAll('tbody tr').find((row) => row.text().includes('plan-001'))!
    await planRow
      .findAll('button')
      .find((button) => button.text().includes('发布'))!
      .trigger('click')
    await flushPromises()

    expect(stub.toastError).toHaveBeenCalledWith('发布失败，请稍后重试')
    expect(stub.toastError).not.toHaveBeenCalledWith(expect.stringContaining('Internal Server'))
    expect(consoleError).toHaveBeenCalled()
    consoleError.mockRestore()
  })

  it('states that the history plan table is read-only and routes edits to the draft workbench', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()
    await openPlanTable(wrapper)

    const notice = wrapper.find('[data-testid="plan-table-readonly-notice"]')
    expect(notice.exists()).toBe(true)
    expect(notice.text()).toContain('只读查阅')
    expect(notice.text()).toContain('回草案工作区')

    await notice
      .findAll('button')
      .find((button) => button.text().includes('去草案工作区修改'))!
      .trigger('click')
    await flushPromises()

    // 引导入口要真的把人送回可编辑的地方，不是一句说明。
    expect(wrapper.text()).toContain('批量待排 → 编辑锁定 → 重预览 → 对比发布')
    expect(wrapper.text()).toContain('待排工单池')
  })

  it.each(['other-plan', 'filtered-history'])(
    'keeps draft invalidation and edits independent of %s',
    async (scenario) => {
      // #4072：已有方案失效只刷新读面与操作状态，人工资源修改继续留在草案。
      const wrapper = mount(SchedulingPage, {
        global: { plugins: [createPinia()], stubs: layoutStub },
      })
      await flushPromises()
      wrapper
        .findComponent({ name: 'SchedulingOrderPool' })
        .vm.$emit('include', ['WO-20260701-001'], true)
      await flushPromises()
      await wrapper
        .findAll('button')
        .find((button) => button.text().includes('生成首版'))!
        .trigger('click')
      await flushPromises()
      const publish = wrapper
        .findAll('button')
        .find((button) => button.text().includes('发布新版'))!
      expect(publish.attributes('disabled')).toBeUndefined()
      const board = wrapper.findComponent({ name: 'SchedulingDraftBoard' })
      board.vm.$emit('update', 'assign-001', { resourceId: 'RES-CNC-02' })
      await flushPromises()
      const editedModel = board.props('model')
      expect(publish.attributes('disabled')).toBeDefined()

      if (scenario === 'filtered-history') {
        historyFilters.isInvalidated = false
        historyEmpty.value = true
        await flushPromises()
      }
      planOneInvalidated.value = true
      await flushPromises()
      if (scenario === 'other-plan') {
        detailSelection.planId = 'plan-invalid'
        await flushPromises()
      }
      expect(publish.attributes('disabled')).toBeDefined()
      expect(publish.attributes('title')).toContain('设备不可用')
      expect(wrapper.text()).toContain('方案已失效（设备不可用），请重排后再发布')
      expect(board.props('model')).toBe(editedModel)
      expect(
        board.props('model').tasks.find((task: { id: string }) => task.id === 'assign-001')
          .resourceId,
      ).toBe('RES-CNC-02')
      wrapper.unmount()
    },
  )

  it('shows explicit detail feedback when the facade returns no detail payload', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: { ...layoutStub, ...sheetStubs } },
    })
    await flushPromises()
    await openPlanTable(wrapper)

    await wrapper
      .findAll('button')
      .filter((button) => button.text().includes('明细'))[1]!
      .trigger('click')
    await flushPromises()

    expect(detailSelection.planId).toBe('plan-empty')
    expect(wrapper.text()).toContain('未返回方案明细')
  })
})

it.each(['Failed to fetch', '503 Service Unavailable'])(
  'notifies a historical-plan time-context failure without a current draft: %s',
  async (message) => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: { ...layoutStub, ...sheetStubs } },
    })
    await flushPromises()
    const ganttTab = wrapper.findAll('[role="tab"]').find((tab) => tab.text().includes('甘特图'))!
    await ganttTab.trigger('focus')
    await ganttTab.trigger('mousedown')
    await flushPromises()
    expect(savedDrafts.value).toEqual([])
    expect(detailSelection.planId).toBe('plan-001')
    stub.toastError.mockClear()
    planTimeContextError.value = new Error(message)
    await flushPromises()
    expect(stub.toastError).toHaveBeenCalledWith(
      expect.stringContaining(message.startsWith('503') ? '服务暂时不可用' : '网络异常'),
    )
    expect(detailSelection.planId).toBe('plan-001')
    wrapper.unmount()
  },
)

it.each([false, true])(
  'keeps plan details readable for an invalid factory timezone and recovers when resolved (segmented=%s)',
  async (segmented) => {
    const assignment = planOne.assignments[0]! as NonNullable<
      BusinessConsoleSchedulePlan['assignments']
    >[number]
    const originalSegments = assignment.segments
    factoryTimezone.value = 'Mars/Base'
    if (segmented)
      assignment.segments = [
        { startUtc: planOne.assignments![0]!.startUtc!, endUtc: planOne.assignments![0]!.endUtc! },
      ]
    routeStub.query = { planId: 'plan-001' }
    let wrapper: ReturnType<typeof mount> | undefined
    try {
      expect(() => {
        wrapper = mount(SchedulingPage, {
          global: { plugins: [createPinia()], stubs: { ...layoutStub, ...sheetStubs } },
        })
      }).not.toThrow()
      await flushPromises()
      expect(wrapper!.text()).toContain('排程方案明细')
      expect(wrapper!.text()).toContain('工厂时区未解析')
      const overview = wrapper!
        .findAll('section')
        .find((section) => section.findAll('h3').some((heading) => heading.text() === '计划概览'))!
      expect(overview.text()).toContain('工厂时区未解析')
      if (segmented) expect(wrapper!.text()).toContain('第 1 段')
      expect(detailSelection.planId).toBe('plan-001')
      factoryTimezone.value = 'Asia/Shanghai'
      await flushPromises()
      expect(wrapper!.text()).not.toContain('工厂时区未解析')
      expect(wrapper!.text()).toContain('Asia/Shanghai')
      expect(detailSelection.planId).toBe('plan-001')
    } finally {
      wrapper?.unmount()
      assignment.segments = originalSegments
    }
  },
)

// Regression: PR #4453 R2 / #4405 r1 故事6，概览和工序必须共用已验证工厂时区。
it.each([
  ['Asia/Shanghai', '2026/07/02 16:00 至 2026/07/02 23:00'],
  ['UTC', '2026/07/02 08:00 至 2026/07/02 15:00'],
])(
  'shows the plan overview and assignments in the same factory timezone: %s',
  async (zone, range) => {
    factoryTimezone.value = zone!
    routeStub.query = { planId: 'plan-001' }
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: { ...layoutStub, ...sheetStubs } },
    })
    await flushPromises()
    const overview = wrapper
      .findAll('section')
      .find((section) => section.findAll('h3').some((heading) => heading.text() === '计划概览'))!
    expect(overview.text().replaceAll('-', '/')).toContain(range)
    expect(wrapper.text()).toContain(zone)
    expect(wrapper.text()).toContain(
      zone === 'Asia/Shanghai'
        ? '2026/07/02 16:00 至 2026/07/02 18:00'
        : '2026/07/02 08:00 至 2026/07/02 10:00',
    )
    wrapper.unmount()
  },
)

it('does not collapse a mixed-factory plan overview into one timezone', async () => {
  assemblyTimezone.value = 'UTC'
  routeStub.query = { planId: 'plan-001' }
  const wrapper = mount(SchedulingPage, {
    global: { plugins: [createPinia()], stubs: { ...layoutStub, ...sheetStubs } },
  })
  await flushPromises()
  const overview = wrapper
    .findAll('section')
    .find((section) => section.findAll('h3').some((heading) => heading.text() === '计划概览'))!
  expect(overview.text()).toContain('跨工厂时区，请查看各工序时间')
  expect(wrapper.text()).toContain('Asia/Shanghai')
  expect(wrapper.text()).toContain('UTC')
  wrapper.unmount()
})
