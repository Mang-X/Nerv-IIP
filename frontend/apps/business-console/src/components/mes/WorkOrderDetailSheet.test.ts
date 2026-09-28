import { mount } from '@vue/test-utils'
import { reactive, ref } from 'vue'
import { describe, expect, it, vi } from 'vitest'

import { formatDateTime } from '@/utils/format'
import WorkOrderDetailSheet from './WorkOrderDetailSheet.vue'

const readFace = vi.hoisted(() => ({
  materialReadiness: undefined as Record<string, unknown> | undefined,
}))

vi.mock('vue-router', () => ({
  RouterLink: { props: ['to'], template: '<a><slot /></a>' },
}))

vi.mock('@/composables/lifecycleAction', () => ({
  useLifecycleWriteIntent: () => ({
    locked: ref(false),
    permits: () => true,
    acquire: vi.fn(),
    clear: vi.fn(),
    recordFailure: vi.fn(),
  }),
  recoverLifecycleAction: vi.fn(),
}))
vi.mock('@/composables/usePendingWriteLeaveGuard', () => ({
  usePendingWriteLeaveGuard: vi.fn(),
}))
vi.mock('@/composables/mes/useMesDisplayNames', () => ({
  useMesDisplayNames: () => ({
    resolveShiftLabel: () => '早班',
    resolveSkuLabel: (value: string) => value,
    resolveWorkCenter: () => '工作中心',
  }),
}))
vi.mock('@/composables/useBusinessMes', () => ({
  describeMesReadinessReasons: () => [],
  useMesWorkOrderDetail: () => ({
    detail: ref({
      workOrderId: 'WO-1',
      status: 'created',
      operationTasks: [],
      blockingReasons: [],
    }),
    detailError: ref(undefined),
    detailPending: ref(false),
    filters: reactive({ workOrderId: '' }),
    materialReadiness: ref(readFace.materialReadiness),
    refreshDetail: vi.fn(),
  }),
  useMesDispatchTasks: () => ({
    assignDispatchTask: vi.fn(),
    assignDispatchTaskPending: ref(false),
  }),
  useMesOperationTasks: () => ({
    completeOperationTask: vi.fn(),
    filters: reactive({ organizationId: 'org', environmentId: 'dev' }),
    operationScopeMessage: ref(''),
    operationScopeReady: ref(false),
    pauseOperationTask: vi.fn(),
    resumeOperationTask: vi.fn(),
    startOperationTask: vi.fn(),
  }),
}))

function mountSheet() {
  const slotStub = { template: '<div><slot /></div>' }
  return mount(WorkOrderDetailSheet, {
    props: { workOrderId: 'WO-1' },
    global: {
      stubs: {
        NvSheet: slotStub,
        NvSheetContent: slotStub,
        NvSheetHeader: slotStub,
        NvSheetTitle: slotStub,
        NvSheetDescription: slotStub,
        NvSheetFooter: slotStub,
        NvDataTable: { props: ['rows'], template: '<div />' },
        NvButton: slotStub,
        DispatchAssignDialog: true,
      },
    },
  })
}

describe('工单行内抽屉的齐套读面', () => {
  it('缺料时显示真实捕获时间和阻塞结论，不把冻结快照误作已齐套', () => {
    const capturedAtUtc = '2026-09-22T08:15:00Z'
    readFace.materialReadiness = {
      readinessStatus: 'Blocked',
      snapshotCapturedAtUtc: capturedAtUtc,
      items: [
        {
          materialId: 'PK-BOX-01',
          requiredQuantity: 5.05,
          availableQuantity: 0,
          shortageQuantity: 5.05,
        },
      ],
    }
    const wrapper = mountSheet()

    expect(wrapper.get('[data-testid="material-readiness-snapshot"]').text()).toContain(
      formatDateTime(capturedAtUtc),
    )
    expect(wrapper.text()).toContain('1 项缺料')
    expect(wrapper.text()).not.toContain('已齐套')
    expect(wrapper.get('[data-testid="material-readiness-scope"]').text()).toContain('发起领料')
    expect(wrapper.get('[data-testid="material-readiness-scope"]').text()).toContain('确认收料')
    wrapper.unmount()
  })

  it('快照缺失且没有用料行时仍显示阻塞，不补造时间', () => {
    readFace.materialReadiness = {
      readinessStatus: 'Blocked',
      snapshotCapturedAtUtc: null,
      items: [],
    }
    const wrapper = mountSheet()

    expect(wrapper.get('[data-testid="material-readiness-snapshot"]').text()).toBe(
      '齐套快照捕获时间：未记录',
    )
    expect(wrapper.text()).toContain('阻塞')
    expect(wrapper.text()).not.toContain('已齐套')
    wrapper.unmount()
  })
})
