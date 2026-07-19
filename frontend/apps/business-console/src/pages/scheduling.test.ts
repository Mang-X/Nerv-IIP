import { flushPromises, mount } from '@vue/test-utils'
import { computed, reactive, shallowRef } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import SchedulingPage from './scheduling.vue'

const stub = vi.hoisted(() => ({
  releasePlan: vi
    .fn()
    .mockResolvedValue({ success: true, data: { planId: 'plan-001', status: 'released' } }),
  toastError: vi.fn(),
  toastSuccess: vi.fn(),
}))

const detailSelection = reactive({ planId: '' })
const detailError = shallowRef<unknown>()
const detail = computed(() => {
  if (detailSelection.planId === 'plan-001') {
    return {
      planId: detailSelection.planId,
      status: 'generated',
      generatedAtUtc: '2026-07-01T09:30:00Z',
      metrics: {
        scheduledOperationCount: 6,
        unscheduledOperationCount: 1,
        assignedMinutes: 480,
        makespanMinutes: 720,
      },
      assignments: [
        {
          assignmentId: 'assign-001',
          orderId: 'WO-20260701-001',
          operationId: 'OP-10',
          operationSequence: 10,
          resourceId: 'RES-CNC-01',
          workCenterId: 'WC-CNC',
          startUtc: '2026-07-02T08:00:00Z',
          endUtc: '2026-07-02T10:00:00Z',
        },
        {
          assignmentId: 'assign-002',
          orderId: 'WO-20260701-001',
          operationId: 'OP-20',
          operationSequence: 20,
          resourceId: 'RES-CNC-01',
          workCenterId: 'WC-CNC',
          startUtc: '2026-07-02T10:00:00Z',
          endUtc: '2026-07-02T12:00:00Z',
          isLocked: true,
        },
        {
          assignmentId: 'assign-003',
          orderId: 'WO-20260701-002',
          operationId: 'OP-10',
          operationSequence: 10,
          resourceId: 'RES-ASM-01',
          workCenterId: 'WC-ASSEMBLY',
          startUtc: '2026-07-02T08:30:00Z',
          endUtc: '2026-07-02T11:00:00Z',
        },
        {
          assignmentId: 'assign-004',
          orderId: 'WO-20260701-002',
          operationId: 'OP-20',
          operationSequence: 20,
          resourceId: 'RES-ASM-01',
          workCenterId: 'WC-ASSEMBLY',
          startUtc: '2026-07-02T11:00:00Z',
          endUtc: '2026-07-02T14:00:00Z',
        },
        {
          assignmentId: 'assign-005',
          orderId: 'WO-20260701-003',
          operationId: 'OP-10',
          operationSequence: 10,
          resourceId: 'RES-CNC-01',
          workCenterId: 'WC-CNC',
          startUtc: '2026-07-02T12:30:00Z',
          endUtc: '2026-07-02T15:00:00Z',
        },
      ],
      resourceLoads: [
        {
          resourceId: 'RES-CNC-01',
          assignedMinutes: 480,
          availableMinutes: 600,
          utilization: 0.8,
        },
        {
          resourceId: 'RES-ASM-01',
          assignedMinutes: 330,
          availableMinutes: 600,
          utilization: 0.55,
        },
      ],
      conflicts: [
        {
          conflictId: 'conflict-001',
          reasonCode: 'material',
          severity: 'warning',
          orderId: 'WO-20260701-001',
          operationId: 'OP-10',
          resourceId: 'RES-CNC-01',
          message: '关键物料到货晚于计划开工',
        },
      ],
      unscheduledOperations: [
        {
          orderId: 'WO-20260701-002',
          operationId: 'OP-30',
          reasonCode: 'capacity',
          message: '瓶颈资源产能不足',
        },
      ],
    }
  }
  if (detailSelection.planId === 'plan-invalid') {
    return {
      planId: 'plan-invalid',
      status: 'generated',
      assignments: [
        {
          assignmentId: 'valid-locked',
          orderId: 'WO-20260701-004',
          operationId: 'OP-10',
          operationSequence: 10,
          resourceId: 'RES-CNC-01',
          workCenterId: 'WC-CNC',
          startUtc: '2026-07-03T08:00:00Z',
          endUtc: '2026-07-03T10:00:00Z',
          isLocked: true,
        },
        {
          assignmentId: 'invalid-time',
          orderId: 'WO-20260701-004',
          operationId: 'OP-20',
          operationSequence: 20,
          resourceId: 'RES-CNC-01',
          startUtc: '2026-07-03T12:00:00Z',
          endUtc: '2026-07-03T11:00:00Z',
        },
        {
          assignmentId: 'missing-resource',
          orderId: 'WO-20260701-005',
          operationId: 'OP-10',
          operationSequence: 10,
          startUtc: '2026-07-03T08:00:00Z',
          endUtc: '2026-07-03T09:00:00Z',
        },
      ],
      resourceLoads: [],
      conflicts: [],
      unscheduledOperations: [],
    }
  }
  return undefined
})

vi.mock('@/composables/useBusinessScheduling', () => ({
  useBusinessScheduling: () => ({
    detailSelection,
    page: shallowRef(1),
    pageSize: shallowRef('100'),
    planDetail: detail,
    planDetailError: detailError,
    planDetailPending: shallowRef(false),
    plans: computed(() => [
      {
        planId: 'plan-001',
        status: 'generated',
        generatedAtUtc: '2026-07-01T09:30:00Z',
        assignmentCount: 8,
        conflictCount: 1,
        unscheduledOperationCount: 2,
      },
      {
        planId: 'plan-empty',
        status: 'preview',
        generatedAtUtc: '2026-07-01T10:00:00Z',
        assignmentCount: 0,
        conflictCount: 0,
        unscheduledOperationCount: 0,
      },
      {
        planId: 'plan-invalid',
        status: 'generated',
        generatedAtUtc: '2026-07-01T11:00:00Z',
        releasedAtUtc: '2026-07-01T11:30:00Z',
        assignmentCount: 5,
        conflictCount: 0,
        unscheduledOperationCount: 0,
        isInvalidated: true,
        latestInvalidationReasonCode: 'equipmentUnavailable',
        latestInvalidatedAtUtc: '2026-07-01T12:00:00Z',
      },
    ]),
    plansPending: shallowRef(false),
    releasePlan: stub.releasePlan,
    releasePlanPending: shallowRef(false),
    refreshPlans: vi.fn(),
  }),
}))

vi.mock('@nerv-iip/ui', async (orig) => ({
  ...(await orig<typeof import('@nerv-iip/ui')>()),
  toast: { success: stub.toastSuccess, error: stub.toastError },
}))

const layoutStub = { BusinessLayout: { template: '<main><slot /></main>' } }
const sheetStubs = {
  NvSheet: { template: '<div><slot /></div>' },
  DialogRoot: { template: '<div><slot /></div>' },
  NvSheetContent: { template: '<aside><slot /></aside>' },
  NvSheetHeader: { template: '<div><slot /></div>' },
  NvSheetTitle: { template: '<h2><slot /></h2>' },
  NvSheetDescription: { template: '<p><slot /></p>' },
}

beforeEach(() => {
  detailSelection.planId = ''
  detailError.value = undefined
  stub.releasePlan.mockClear()
  stub.toastError.mockClear()
  stub.toastSuccess.mockClear()
})

describe('APS scheduling workbench page', () => {
  it('renders the official scheduling entry with plan summary columns from facade data', async () => {
    const wrapper = mount(SchedulingPage, { global: { stubs: layoutStub } })
    await flushPromises()

    expect(wrapper.text()).toContain('排产工作台')
    expect(wrapper.text()).toContain('plan-001')
    expect(wrapper.text()).toContain('已生成')
    expect(wrapper.text()).toContain('8')
    expect(wrapper.text()).toContain('1 项冲突')
    expect(wrapper.text()).toContain('2 项未排')
  })

  it('uses a single-page table while the facade does not return a total count', async () => {
    const wrapper = mount(SchedulingPage, { global: { stubs: layoutStub } })
    await flushPromises()

    const table = wrapper.findComponent({ name: 'NvDataTable' })
    expect(table.props('pagination')).toBe(false)
    expect(table.props('manual')).not.toBe(true)
    expect(wrapper.text()).toContain('工序数')
    expect(wrapper.text()).not.toContain('资源 / 工序')
  })

  it('renders the selected APS plan as a read-only resource timeline', async () => {
    const wrapper = mount(SchedulingPage, { global: { stubs: { ...layoutStub } } })
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
    expect(wrapper.text()).toContain('班次级')
    expect(wrapper.text()).toContain('日级')
    expect(wrapper.text()).toContain('冲突')
    expect(wrapper.text()).toContain('锁定')
    expect(wrapper.text()).not.toContain('甘特可视化待接入')
  })

  it('opens plan detail and releases the selected plan through the composable', async () => {
    const wrapper = mount(SchedulingPage, { global: { stubs: { ...layoutStub, ...sheetStubs } } })
    await flushPromises()

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

  it('marks invalidated plans with their reason and blocks release', async () => {
    const wrapper = mount(SchedulingPage, { global: { stubs: layoutStub } })
    await flushPromises()

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

  it('explains invalidated, invalid-time, and missing-resource assignments in the Gantt view', async () => {
    detailSelection.planId = 'plan-invalid'
    const wrapper = mount(SchedulingPage, { global: { stubs: layoutStub } })
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
  })

  it('shows a permission-specific Gantt error state', async () => {
    detailSelection.planId = 'plan-empty'
    detailError.value = { response: { status: 403 } }
    const wrapper = mount(SchedulingPage, { global: { stubs: layoutStub } })
    await flushPromises()

    const ganttTab = wrapper.findAll('[role="tab"]').find((tab) => tab.text().includes('甘特图'))!
    await ganttTab.trigger('focus')
    await ganttTab.trigger('mousedown')
    await flushPromises()

    expect(wrapper.text()).toContain('权限不足，无法查看该排程方案')
  })

  it('shows explicit detail feedback when a plan detail request fails', async () => {
    detailError.value = new Error('network')
    const wrapper = mount(SchedulingPage, { global: { stubs: { ...layoutStub, ...sheetStubs } } })
    await flushPromises()

    await wrapper
      .findAll('button')
      .filter((button) => button.text().includes('明细'))[1]!
      .trigger('click')
    await flushPromises()

    expect(detailSelection.planId).toBe('plan-empty')
    expect(wrapper.text()).toContain('明细加载失败，请稍后重试')
  })

  it('shows explicit detail feedback when the facade returns no detail payload', async () => {
    const wrapper = mount(SchedulingPage, { global: { stubs: { ...layoutStub, ...sheetStubs } } })
    await flushPromises()

    await wrapper
      .findAll('button')
      .filter((button) => button.text().includes('明细'))[1]!
      .trigger('click')
    await flushPromises()

    expect(detailSelection.planId).toBe('plan-empty')
    expect(wrapper.text()).toContain('未返回方案明细')
  })
})
