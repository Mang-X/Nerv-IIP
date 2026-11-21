import {
  authState,
  stub,
  localCandidateCallback,
  downtimeImpact,
  downtimeBaseline,
  savedDrafts,
  savedPlans,
  pagePersistence,
  associatedOrders,
  associatedError,
  candidatesEmpty,
  generatedPlan,
  releasePending,
  insertionJob,
  detailSelection,
  historyFilters,
  planOne,
  layoutStub,
  sheetStubs,
  openPlanTable,
} from './scheduling.fixture'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { describe, expect, it, vi } from 'vitest'
import SchedulingPage from './scheduling.vue'
import SchedulingMaterialShortageSummary from '@/components/scheduling/SchedulingMaterialShortageSummary.vue'
import type {
  SchedulingCandidateSelection,
  BusinessConsoleSchedulePlan,
} from '@nerv-iip/api-client'

describe('APS scheduling workbench page', () => {
  it.each(['confirm', 'switch', 'restore', 'cancel'])(
    '选定保存实际插单候选并绑定确认身份：%s',
    async (action) => {
      const candidate = {
        ...planOne,
        planId: 'insertion-current',
        status: 'generated' as const,
        conflicts: [],
        unscheduledOperations: [],
      }
      insertionJob.value = {
        jobId: 'job-current',
        status: 'completed',
        input: {
          ...historyFilters,
          planId: 'plan-001',
          workOrderId: 'WO-12',
          workOrderIds: ['WO-12'],
        },
        result: {
          baselinePlanId: 'plan-001',
          candidatePlanId: candidate.planId,
          candidate,
          snapshot: {
            baseline: { ...candidate, planId: 'plan-001' },
            problem: {
              organizationId: 'org-001',
              environmentId: 'env-dev',
              orders: [{ orderId: 'WO-11' }, { orderId: 'WO-12' }],
            },
          },
        },
      }
      let finishRevision!: (revision: { candidate: BusinessConsoleSchedulePlan }) => void
      if (action === 'restore') {
        authState.principalId = 'user-1'
        savedPlans.set('plan-001', { ...candidate, planId: 'plan-001' })
        savedPlans.set('plan-other', { ...candidate, planId: 'plan-other' })
        savedDrafts.value = ['plan-001', 'plan-other'].map((planId, i) => ({
          planId,
          savedAtUtc: `2026-10-08T0${2 - i}:00:00Z`,
          state: { orders: [], tasks: [], pendingOperations: [] },
        }))
        stub.revisePlan.mockReturnValueOnce(
          new Promise((resolve) => {
            finishRevision = resolve
          }),
        )
      } else stub.revisePlan.mockResolvedValueOnce({ candidate })
      const wrapper = mount(SchedulingPage, {
        global: { plugins: [createPinia()], stubs: layoutStub },
      })
      await flushPromises()
      expect(stub.releasePlan).not.toHaveBeenCalled()
      expect(
        wrapper
          .findAll('button')
          .find((b) => b.text().includes('发布新版'))!
          .attributes('disabled'),
      ).toBeDefined()
      await wrapper
        .findAll('button')
        .find((b) => b.text().includes('选定候选并保存修订'))!
        .trigger('click')
      await flushPromises()
      expect(stub.revisePlan).toHaveBeenCalledWith(
        'insertion-current',
        expect.objectContaining({ includedOrderIds: ['WO-11', 'WO-12'], lockedAssignments: [] }),
      )
      expect(stub.releasePlan).not.toHaveBeenCalled()
      if (action === 'restore') {
        await pagePersistence.select('plan-other')
        await flushPromises()
        expect(
          wrapper.findComponent({ name: 'SchedulingDraftBoard' }).props('model').meta.planId,
        ).toBe('plan-other')
        finishRevision({ candidate })
        await flushPromises()
        expect(
          wrapper.findComponent({ name: 'SchedulingDraftBoard' }).props('model').meta.planId,
        ).toBe('plan-other')
        expect(stub.toastSuccess).not.toHaveBeenCalledWith(
          '所选插单候选已保存为修订，请核对后确认发布',
        )
        wrapper.unmount()
        return
      }
      await wrapper
        .findAll('button')
        .find((b) => b.text().includes('发布新版'))!
        .trigger('click')
      await flushPromises()
      expect(stub.releasePlan).not.toHaveBeenCalled()
      const confirm = [...document.body.querySelectorAll('button')].find(
        (b) => b.textContent?.trim() === '确认发布',
      )!
      expect(confirm).toBeTruthy()
      if (action === 'switch') {
        generatedPlan.value = { ...candidate, planId: 'plan-other' }
        await flushPromises()
        expect(confirm.disabled).toBe(true)
        confirm.click()
        await flushPromises()
        expect(stub.releasePlan).not.toHaveBeenCalled()
        wrapper.unmount()
        return
      }
      let finishRelease!: () => void
      if (action === 'cancel')
        stub.releasePlan.mockReturnValueOnce(
          new Promise<void>((resolve) => {
            finishRelease = resolve
          }),
        )
      confirm.click()
      await flushPromises()
      expect(stub.releasePlan).toHaveBeenCalledWith('insertion-current')
      if (action === 'cancel') {
        releasePending.value = true
        await flushPromises()
        const cancel = [...document.body.querySelectorAll('button')].find(
          (b) => b.textContent?.trim() === '取消',
        )!
        expect(cancel.disabled).toBe(false)
        cancel.click()
        await flushPromises()
        expect(document.body.textContent).not.toContain('确认发布所选方案？')
        finishRelease()
        await flushPromises()
      }
      wrapper.unmount()
    },
  )

  it.each([true, false])(
    'shows the same batch shortages in draft and saved plan (shortage=%s)',
    async (hasShortage) => {
      // #3996：后端已完成共享库存分配，前端只呈现方案级缺口 3，不能按两个工单重复累加。
      planOne.materialShortageSummary = hasShortage
        ? [
            {
              materialId: 'MAT-SHARED',
              materialLotId: 'LOT-1',
              uomCode: 'kg',
              shortageQuantity: 3,
              affectedOperations: [
                { orderId: 'WO-20260701-001', operationId: 'OP-10' },
                { orderId: 'WO-20260701-002', operationId: 'OP-20' },
              ],
            },
          ]
        : []
      const wrapper = mount(SchedulingPage, {
        global: { plugins: [createPinia()], stubs: { ...layoutStub, ...sheetStubs } },
      })
      await flushPromises()
      expect(wrapper.findComponent(SchedulingMaterialShortageSummary).exists()).toBe(false)
      wrapper
        .findComponent({ name: 'SchedulingOrderPool' })
        .vm.$emit('include', ['WO-20260701-001'], true)
      await flushPromises()
      await wrapper
        .findAll('button')
        .find((button) => button.text().includes('生成首版'))!
        .trigger('click')
      await flushPromises()
      const draftSummary = wrapper.find(
        '[data-testid="scheduling-draft-board"] [data-testid="scheduling-material-shortage-summary"]',
      )
      if (hasShortage) {
        expect(draftSummary.findAll('tbody tr')).toHaveLength(1)
        expect(draftSummary.findAll('tbody td')[0]!.text()).toContain('MAT-SHARED')
        expect(draftSummary.findAll('tbody td')[1]!.text()).toBe('3 kg')
        expect(draftSummary.text()).toContain('LOT-1')
        expect(draftSummary.text()).toContain('WO-20260701-001 · OP-10')
        expect(draftSummary.text()).toContain('WO-20260701-002 · OP-20')
      } else {
        expect(draftSummary.text()).toContain('方案快照未记录缺料')
      }
      const expected = draftSummary.text()
      const ganttTab = wrapper.findAll('[role="tab"]').find((tab) => tab.text().includes('甘特'))!
      await ganttTab.trigger('focus')
      await ganttTab.trigger('mousedown')
      await flushPromises()
      const savedSummary = wrapper.find(
        '[data-testid="scheduling-plan-gantt"] [data-testid="scheduling-material-shortage-summary"]',
      )
      expect(savedSummary.text()).toBe(expected)
      expect(
        wrapper.find('aside [data-testid="scheduling-material-shortage-summary"]').text(),
      ).toBe(expected)
      wrapper.unmount()
    },
  )

  it('preserves completed work-order commercial facts after it leaves the candidate pool', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: { ...layoutStub, ...sheetStubs } },
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
    candidatesEmpty.value = true
    associatedOrders.value = [
      {
        workOrderId: 'WO-20260701-001',
        commercialSourceFacts: {
          status: 'available',
          salesOrders: [{ salesOrderNo: 'SO-COMPLETED', customerCode: 'CUSTOMER-COMPLETED' }],
        },
      },
    ]
    await flushPromises()
    const draftBoard = wrapper.findComponent({ name: 'SchedulingDraftBoard' })
    const tableTab = draftBoard
      .findAll('[role=tab]')
      .find((tab) => tab.text().includes('表格编辑'))!
    await tableTab.trigger('focus')
    await tableTab.trigger('mousedown')
    await flushPromises()
    expect(draftBoard.text()).toContain('SO-COMPLETED')
    expect(draftBoard.text()).toContain('CUSTOMER-COMPLETED')
    await openPlanTable(wrapper)
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '明细')!
      .trigger('click')
    await flushPromises()
    expect(wrapper.text()).toContain('SO-COMPLETED')
    expect(wrapper.text()).toContain('CUSTOMER-COMPLETED')
    wrapper.unmount()
  })

  it('notifies actual commercial fact read failure and distinguishes unavailable from no source', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: { ...layoutStub, ...sheetStubs } },
    })
    await flushPromises()
    await openPlanTable(wrapper)
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '明细')!
      .trigger('click')
    await flushPromises()
    expect(wrapper.text()).not.toContain('暂不可读取')
    associatedError.value = { status: 503, detail: 'Service Unavailable' }
    await flushPromises()
    expect(stub.toastError).toHaveBeenCalledWith(expect.stringContaining('服务暂时不可用'))
    expect(wrapper.text()).toContain('商业关联')
    expect(wrapper.text()).toContain('暂不可读取')
    associatedError.value = undefined
    await flushPromises()
    expect(wrapper.text()).not.toContain('暂不可读取')
    wrapper.unmount()
  })

  it('renders the official scheduling entry with plan summary columns from facade data', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()
    await openPlanTable(wrapper)

    expect(wrapper.text()).toContain('排产工作台')
    expect(wrapper.text()).toContain('plan-001')
    expect(wrapper.text()).toContain('已生成')
    expect(wrapper.text()).toContain('8')
    expect(wrapper.text()).toContain('1 项约束（冲突/预警）')
    expect(wrapper.text()).not.toContain('1 项冲突')
    expect(wrapper.text()).toContain('2 项未排')
  })

  it('exposes the complete scheduling workbench loop from one route', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()

    const workbenchTab = wrapper
      .findAll('[role="tab"]')
      .find((tab) => tab.text().includes('排程总览'))!
    await workbenchTab.trigger('focus')
    await workbenchTab.trigger('mousedown')
    await flushPromises()

    expect(wrapper.text()).toContain('批量待排 → 编辑锁定 → 重预览 → 对比发布')
    expect(wrapper.text()).toContain('待排工单池')
    expect(wrapper.text()).toContain('排程草案工作区')
    expect(wrapper.text()).toContain('工序待排池')
    expect(wrapper.text()).toContain('锁定重预览')
    expect(wrapper.text()).toContain('发布新版')
    // 排程窗口不再写死「现在起 7 天」，工作台上必须有可改的窗口控件（MAN-694 / #1262）。
    expect(wrapper.find('[data-testid="scheduling-horizon-fields"]').exists()).toBe(true)
  })

  it('explains why the workbench actions are disabled instead of leaving grey buttons', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()

    const generate = wrapper.findAll('button').find((button) => button.text().includes('生成首版'))!
    const repreview = wrapper
      .findAll('button')
      .find((button) => button.text().includes('锁定重预览'))!
    const publish = wrapper.findAll('button').find((button) => button.text().includes('发布新版'))!

    // 还没勾工单、还没有草案：三个主操作都灰着，hover 必须说得清缺什么。
    expect(generate.attributes('disabled')).toBeDefined()
    expect(generate.attributes('title')).toBe('还没有选中工单：先在待排工单池里勾选要排的工单')
    expect(repreview.attributes('disabled')).toBeDefined()
    expect(repreview.attributes('title')).toBe('还没有草案方案：先生成首版方案，再做锁定重预览')
    expect(publish.attributes('disabled')).toBeDefined()
    expect(publish.attributes('title')).toBe('还没有可发布的版本：先生成首版或重预览出一版方案')

    // 勾上工单后按钮可用，提示改成"这一步会做什么"。
    wrapper
      .findComponent({ name: 'SchedulingOrderPool' })
      .vm.$emit('include', ['WO-20260701-001'], true)
    await flushPromises()

    const enabledGenerate = wrapper
      .findAll('button')
      .find((button) => button.text().includes('生成首版'))!
    expect(enabledGenerate.attributes('disabled')).toBeUndefined()
    // 「这一步会做什么」现在还带上当前排程窗口（MAN-694 / #1262）：窗口可改之后，
    // 只说"生成首版方案"不足以让人确认排到哪一天。
    expect(enabledGenerate.attributes('title')).toContain('按当前勾选的工单生成首版排程方案')
    expect(enabledGenerate.attributes('title')).toContain('至')
  })

  it('排程窗口非法时并入禁用原因表：按钮直接灰掉并说清改哪里（MAN-694 / #1262）', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()

    wrapper
      .findComponent({ name: 'SchedulingOrderPool' })
      .vm.$emit('include', ['WO-20260701-001'], true)
    await flushPromises()

    // 自定义窗口但起止倒置：#1278 的 firstBlockingReason 要认这条新原因。
    const horizon = wrapper.findComponent({ name: 'SchedulingHorizonFields' })
    horizon.vm.$emit('update:modelValue', {
      ...(horizon.props('modelValue') as Record<string, unknown>),
      mode: 'custom',
      startLocal: '2026-08-05T08:00',
      endLocal: '2026-08-04T08:00',
    })
    await flushPromises()

    const generate = wrapper.findAll('button').find((button) => button.text().includes('生成首版'))!
    expect(generate.attributes('disabled')).toBeDefined()
    expect(generate.attributes('title')).toBe('排程窗口不可用：排程窗口结束时间必须晚于开始时间。')

    // 灰按钮点下去也不能发请求（disabled 与动作函数同一处事实）。
    await generate.trigger('click')
    await flushPromises()
    expect(stub.generatePlan).not.toHaveBeenCalled()
  })

  it('surfaces the service message when generating the first plan fails', async () => {
    // generated client 在 throwOnError 下抛的是响应体对象（不是 Error）：以前这里被吞成猜测文案。
    stub.generatePlan.mockRejectedValueOnce({
      title: 'Bad Request',
      detail: '工单 WO-20260701-001 缺少生产版本，无法排程',
      status: 400,
    })
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

    expect(stub.toastError).toHaveBeenCalledWith(
      '生成失败：工单 WO-20260701-001 缺少生产版本，无法排程',
    )
    expect(stub.toastSuccess).not.toHaveBeenCalled()
  })

  it('选定右移候选后清除旧插单展示并允许确认当前所选方案', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()
    insertionJob.value = {
      jobId: 'old-insertion',
      status: 'completed',
      result: { candidatePlanId: 'old-insertion-plan' },
    }
    await flushPromises()
    expect(
      wrapper
        .findAll('button')
        .find((b) => b.text().includes('发布新版'))!
        .attributes('disabled'),
    ).toBeDefined()
    const plan = {
      ...planOne,
      planId: 'selected-shift',
      status: 'generated' as const,
      conflicts: [],
      unscheduledOperations: [],
    }
    savedPlans.set(plan.planId, plan)
    await localCandidateCallback.onSelected!({
      plan,
      workingDraft: {
        planId: plan.planId,
        savedAtUtc: '2026-10-08T08:00:00Z',
        state: { contractVersion: 1, orders: [], tasks: [], pendingOperations: [] },
      },
      comparison: { basePlanId: 'plan-001', candidatePlanId: plan.planId, movedOperationCount: 1 },
    })
    await flushPromises()
    expect(wrapper.findComponent({ name: 'SchedulingDraftBoard' }).props('model').meta.planId).toBe(
      plan.planId,
    )
    expect(
      wrapper
        .findAll('button')
        .find((b) => b.text().includes('发布新版'))!
        .attributes('disabled'),
    ).toBeUndefined()
    expect(stub.releasePlan).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('does not apply a selected candidate or report success when restoring its plan fails', async () => {
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
    const board = wrapper.findComponent({ name: 'SchedulingDraftBoard' })
    expect(board.props('model').meta.planId).toBe('plan-001')
    const previousDetail = detailSelection.planId
    stub.toastSuccess.mockClear()
    localCandidateCallback.readSelectedPlan.mockRejectedValueOnce(
      new Error('GET selected plan failed: 500'),
    )
    await localCandidateCallback.onSelected!({
      plan: { ...planOne, planId: 'selected-plan', status: 'generated' },
      workingDraft: {
        planId: 'selected-plan',
        savedAtUtc: '2026-10-08T08:00:00Z',
        state: { contractVersion: 1, orders: [], tasks: [] },
      },
      comparison: {
        basePlanId: 'plan-001',
        candidatePlanId: 'selected-plan',
        movedOperationCount: 1,
      },
    } as SchedulingCandidateSelection)
    await flushPromises()
    expect(board.props('model').meta.planId).toBe('plan-001')
    expect(detailSelection.planId).toBe(previousDetail)
    expect(
      wrapper.findComponent({ name: 'ScheduleRevisionReview' }).props('revision'),
    ).toBeUndefined()
    expect(stub.toastSuccess).not.toHaveBeenCalled()
  })

  it('compares each revision against its persisted base rather than the edited draft', async () => {
    // Regression / DomainInvariant: #3625 原值来自持久化版本，连续修订以刚生成的候选为下一次基线。
    const candidate = {
      ...planOne,
      planId: 'plan-002',
      assignments: planOne.assignments.map((assignment) =>
        assignment.assignmentId === 'assign-001'
          ? { ...assignment, resourceId: 'RES-CNC-02' }
          : assignment,
      ),
      changeSummary: [
        {
          orderId: 'WO-20260701-001',
          operationId: 'OP-10',
          changeType: 'moved',
          message: '改派备用设备',
        },
      ],
    }
    stub.revisePlan.mockResolvedValueOnce({ candidate }).mockResolvedValueOnce({
      candidate: {
        ...candidate,
        planId: 'plan-003',
        assignments: candidate.assignments.map((assignment) =>
          assignment.assignmentId === 'assign-001'
            ? { ...assignment, resourceId: 'RES-CNC-03' }
            : assignment,
        ),
      },
    })
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
    const board = wrapper.findComponent({ name: 'SchedulingDraftBoard' })
    board.vm.$emit('update', 'assign-001', { resourceId: 'RES-CNC-02' })
    await flushPromises()
    const editActions = wrapper.get('[aria-label="草稿编辑操作"]')
    await editActions
      .findAll('button')
      .find((button) => button.text() === '锁定全部修改')!
      .trigger('click')
    await flushPromises()
    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('锁定重预览'))!
      .trigger('click')
    await flushPromises()
    expect(wrapper.get('[data-change-row]').text()).toContain('RES-CNC-01 → RES-CNC-02')
    // #4463：本次变化须先于长风险明细和图表，已有草案仍能展开调整工单。
    const review = wrapper.get('[data-testid="schedule-revision-review"]')
    expect(
      review.element.compareDocumentPosition(board.element) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy()
    const poolGroup = wrapper
      .findComponent({ name: 'SchedulingOrderPool' })
      .element.closest('details')!
    expect(poolGroup.open).toBe(false)
    poolGroup.querySelector('summary')!.click()
    expect(poolGroup.open).toBe(true)

    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('锁定重预览'))!
      .trigger('click')
    await flushPromises()
    expect(wrapper.get('[data-change-row]').text()).toContain('RES-CNC-02 → RES-CNC-03')
  })

  it('persists a draft operation override with the plan id and the operation behind the task', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()

    // 挑工单 → 生成首版 → 草案工作区才有工序可持久锁定。
    wrapper
      .findComponent({ name: 'SchedulingOrderPool' })
      .vm.$emit('include', ['WO-20260701-001'], true)
    await flushPromises()
    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('生成首版'))!
      .trigger('click')
    await flushPromises()

    expect(stub.generatePlan).toHaveBeenCalled()

    // 组件回的是甘特 taskId（= assignmentId）；页面必须把它映回 operationId 再落库。
    wrapper
      .findComponent({ name: 'SchedulingDraftBoard' })
      .vm.$emit('persistOverride', 'assign-001')
    await flushPromises()

    expect(stub.upsertOperationOverride).toHaveBeenCalledWith({
      planId: 'plan-001',
      operationId: 'OP-10',
      resourceId: 'RES-CNC-01',
      startUtc: '2026-07-02T08:00:00Z',
      endUtc: '2026-07-02T10:00:00Z',
    })
    expect(stub.toastSuccess).toHaveBeenCalledWith('工序已持久锁定，之后重新排程也保持不变')
  })

  it('refuses to persist an override for a task the draft does not know', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()

    // 草案还没生成方案：不能静默吞掉这次点击，也绝不能发请求。
    wrapper
      .findComponent({ name: 'SchedulingDraftBoard' })
      .vm.$emit('persistOverride', 'assign-001')
    await flushPromises()

    expect(stub.upsertOperationOverride).not.toHaveBeenCalled()
  })
})

it('refreshes downtime on the persisted draft baseline while preserving manual assignments', async () => {
  const wrapper = mount(SchedulingPage, { global: { plugins: [createPinia()], stubs: layoutStub } })
  generatedPlan.value = planOne
  await flushPromises()
  expect(downtimeBaseline()).toBe('plan-001')
  const board = wrapper.findComponent({ name: 'SchedulingDraftBoard' })
  const operation =
    board.props('model').tasks.find((t: { id: string }) => t.id === 'a1') ??
    board.props('model').tasks.find((t: { type: string }) => t.type === 'operation')
  const before = structuredClone(operation)
  board.vm.$emit('update', operation.id, { startUtc: '2026-10-09T07:00:00.000Z' })
  await flushPromises()
  const edited = structuredClone(
    board.props('model').tasks.find((t: { id: string }) => t.id === operation.id),
  )
  downtimeImpact.value = {
    baselinePlanId: 'plan-001',
    affectedOperations: [{ workOrderId: operation.orderId, operationId: operation.operationId }],
    items: [
      {
        fact: {
          source: 'mes',
          sourceReferenceId: 'DT-01',
          deviceAssetId: operation.resourceId,
          workCenterId: operation.workCenterId,
          startedAtUtc: '2026-10-09T06:00:00.000Z',
        },
        operationsWithAlternativesCount: 3,
      },
    ],
  }
  await flushPromises()
  expect(wrapper.get('[data-testid="scheduling-downtime"]').text()).toContain(
    '3 道受影响工序有合格可用备选',
  )
  expect(wrapper.get('[data-testid="scheduling-downtime"]').text()).toContain('2 小时')
  const refreshed = board.props('model').tasks.find((t: { id: string }) => t.id === operation.id)
  expect(refreshed).toEqual({ ...edited, downtimeRisk: expect.stringContaining('设备停机') })
  expect(refreshed.startUtc).not.toBe(before.startUtc)
  expect(
    wrapper.get('[data-testid="scheduling-downtime"] button').attributes('disabled'),
  ).toBeDefined()
  expect(wrapper.get('[data-testid="scheduling-downtime"]').text()).toContain('草稿有人工编辑')
  expect(stub.releasePlan).not.toHaveBeenCalled()
  wrapper.unmount()
})

it('generates from the downtime banner, selects the actual candidate and confirms that plan for publishing', async () => {
  const plan = {
    ...planOne,
    planId: 'selected-downtime-plan',
    status: 'generated' as const,
    conflicts: [],
    unscheduledOperations: [],
  }
  savedPlans.set(plan.planId, plan)
  localCandidateCallback.preview.mockResolvedValueOnce({
    data: {
      success: true,
      data: {
        baselinePlanId: 'plan-001',
        asOfUtc: '2026-10-09T08:00:00Z',
        inputFingerprint: 'same-facts',
        candidates: [{ strategy: 'rightShift', comparison: {} }],
      },
    },
  })
  localCandidateCallback.select.mockResolvedValueOnce({
    data: {
      success: true,
      data: {
        plan,
        workingDraft: {
          planId: plan.planId,
          savedAtUtc: '2026-10-09T08:00:00Z',
          state: { contractVersion: 1, orders: [], tasks: [], pendingOperations: [] },
        },
        comparison: { basePlanId: 'plan-001', candidatePlanId: plan.planId },
      },
    },
  })
  const wrapper = mount(SchedulingPage, { global: { plugins: [createPinia()], stubs: layoutStub } })
  generatedPlan.value = { ...planOne, conflicts: [], unscheduledOperations: [] }
  await flushPromises()
  downtimeImpact.value = {
    baselinePlanId: 'plan-001',
    items: [
      {
        fact: {
          source: 'mes',
          sourceReferenceId: 'DT-01',
          deviceAssetId: 'CNC-01',
          startedAtUtc: '2026-10-09T06:00:00Z',
        },
        operationsWithAlternativesCount: 2,
      },
    ],
  }
  await flushPromises()
  const scroll = vi.fn()
  wrapper.get('[data-testid="scheduling-candidates"]').element.scrollIntoView = scroll
  await wrapper.get('[data-testid="scheduling-downtime"] button').trigger('click')
  await flushPromises()
  expect(wrapper.get('[data-testid="scheduling-candidates"]').text()).toContain('原资源右移')
  expect(scroll).toHaveBeenCalled()
  expect(localCandidateCallback.preview).toHaveBeenCalledWith(
    expect.objectContaining({ body: expect.objectContaining({ baselinePlanId: 'plan-001' }) }),
  )
  await wrapper.get('[data-testid="select-candidate"]').trigger('click')
  await flushPromises()
  expect(wrapper.findComponent({ name: 'SchedulingDraftBoard' }).props('model').meta.planId).toBe(
    plan.planId,
  )
  expect(stub.releasePlan).not.toHaveBeenCalled()
  await wrapper
    .findAll('button')
    .find((b) => b.text().includes('发布新版'))!
    .trigger('click')
  await flushPromises()
  const confirm = [...document.body.querySelectorAll('button')].find(
    (b) => b.textContent?.trim() === '确认发布',
  )!
  expect(confirm).toBeTruthy()
  confirm.click()
  await flushPromises()
  expect(stub.releasePlan).toHaveBeenCalledWith(plan.planId)
  wrapper.unmount()
})

// Regression: PR #4453 R1 / #4405 r1。无当前草案的历史方案请求也必须显示失败 toast。
