import { flushPromises, mount } from '@vue/test-utils'
import { computed, reactive, shallowRef } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import SchedulingPage from './scheduling.vue'

const stub = vi.hoisted(() => ({
  releasePlan: vi.fn().mockResolvedValue({ success: true, data: { planId: 'plan-001', status: 'released' } }),
  toastError: vi.fn(),
  toastSuccess: vi.fn(),
}))

const detailSelection = reactive({ planId: '' })
const detail = computed(() => detailSelection.planId
  ? {
      planId: detailSelection.planId,
      status: 'generated',
      generatedAtUtc: '2026-07-01T09:30:00Z',
      metrics: {
        scheduledOperationCount: 6,
        unscheduledOperationCount: 1,
        assignedMinutes: 480,
        makespanMinutes: 720,
      },
      assignments: [{
        assignmentId: 'assign-001',
        orderId: 'WO-20260701-001',
        operationSequence: 20,
        resourceId: 'RES-CNC-01',
        workCenterId: 'WC-CNC',
        startUtc: '2026-07-02T08:00:00Z',
        endUtc: '2026-07-02T10:00:00Z',
      }],
      resourceLoads: [{
        resourceId: 'RES-CNC-01',
        assignedMinutes: 480,
        availableMinutes: 600,
        utilization: 0.8,
      }],
      conflicts: [{
        conflictId: 'conflict-001',
        reasonCode: 'material',
        severity: 'warning',
        orderId: 'WO-20260701-001',
        message: '关键物料到货晚于计划开工',
      }],
      unscheduledOperations: [{
        orderId: 'WO-20260701-002',
        operationId: 'OP-30',
        reasonCode: 'capacity',
        message: '瓶颈资源产能不足',
      }],
    }
  : undefined)

vi.mock('@/composables/useBusinessScheduling', () => ({
  useBusinessScheduling: () => ({
    detailSelection,
    page: shallowRef(1),
    pageSize: shallowRef('20'),
    planDetail: detail,
    planDetailPending: shallowRef(false),
    plans: computed(() => [{
      planId: 'plan-001',
      status: 'generated',
      generatedAtUtc: '2026-07-01T09:30:00Z',
      assignmentCount: 8,
      conflictCount: 1,
      unscheduledOperationCount: 2,
    }]),
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
  SheetPro: { template: '<div><slot /></div>' },
  DialogRoot: { template: '<div><slot /></div>' },
  SheetProContent: { template: '<aside><slot /></aside>' },
  SheetProHeader: { template: '<div><slot /></div>' },
  SheetProTitle: { template: '<h2><slot /></h2>' },
  SheetProDescription: { template: '<p><slot /></p>' },
}

beforeEach(() => {
  detailSelection.planId = ''
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

  it('shows a clear Gantt placeholder without fabricated schedule blocks', async () => {
    const wrapper = mount(SchedulingPage, { global: { stubs: { ...layoutStub } } })
    await flushPromises()

    const ganttTab = wrapper.findAll('[role="tab"]').find((tab) => tab.text().includes('甘特图'))!
    await ganttTab.trigger('focus')
    await ganttTab.trigger('mousedown')
    await flushPromises()

    expect(wrapper.text()).toContain('甘特可视化待接入')
    expect(wrapper.text()).not.toContain('样例')
    expect(wrapper.text()).not.toContain('测试')
  })

  it('opens plan detail and releases the selected plan through the composable', async () => {
    const wrapper = mount(SchedulingPage, { global: { stubs: { ...layoutStub, ...sheetStubs } } })
    await flushPromises()

    await wrapper.findAll('button').find((button) => button.text().includes('明细'))!.trigger('click')
    await flushPromises()

    expect(detailSelection.planId).toBe('plan-001')
    expect(wrapper.text()).toContain('资源分配')
    expect(wrapper.text()).toContain('RES-CNC-01')
    expect(wrapper.text()).toContain('关键物料到货晚于计划开工')
    expect(wrapper.text()).toContain('瓶颈资源产能不足')

    await wrapper.findAll('button').find((button) => button.text().includes('发布'))!.trigger('click')
    await flushPromises()

    expect(stub.releasePlan).toHaveBeenCalledWith('plan-001')
    expect(stub.toastSuccess).toHaveBeenCalled()
  })
})
