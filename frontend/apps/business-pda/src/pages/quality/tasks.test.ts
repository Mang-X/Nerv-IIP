import type { BusinessConsoleQualityInspectionTaskItem } from '@nerv-iip/api-client'
import { InspectionTaskClaimBlockedError } from '@/components/quality/inspectionTaskBlockReasons'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, ref, shallowRef } from 'vue'

const push = vi.fn()
vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
}))

const claimTask = vi.fn()
const submitInspection = vi.fn()
const refresh = vi.fn(async () => {})
const tasks = ref<BusinessConsoleQualityInspectionTaskItem[]>([])

vi.mock('@/composables/useBusinessQualityInspectionTasks', () => ({
  useBusinessQualityInspectionTasks: () => ({
    tasks,
    total: computed(() => tasks.value.length),
    loaded: computed(() => tasks.value.length),
    hasMore: computed(() => false),
    loadMore: vi.fn(),
    ensureAllLoaded: vi.fn(async () => tasks.value),
    pending: shallowRef(false),
    error: shallowRef(null),
    refresh,
    reasonCodes: shallowRef([]),
    submitInspection,
    submitPending: shallowRef(false),
    claimTask,
    lastUpdatedAt: shallowRef('2026-07-31T08:00:00.000Z'),
    hasSuccessfulResponse: shallowRef(true),
    hasFailedResponse: shallowRef(false),
    scopeReady: shallowRef(true),
  }),
  useInspectionPlanCharacteristics: () => ({
    characteristics: shallowRef([]),
    planCode: shallowRef(''),
    pending: shallowRef(false),
    error: shallowRef(null),
    refresh: vi.fn(),
  }),
}))

vi.mock('@/composables/useWorkbenchHome', () => ({
  usePdaIdentity: () => ({
    organizationId: shallowRef('org-001'),
    environmentId: shallowRef('env-dev'),
  }),
}))

import TasksPage from './tasks.vue'

function claimableTask(): BusinessConsoleQualityInspectionTaskItem {
  return {
    inspectionTaskId: 'TASK-1',
    inspectionPlanId: 'PLAN-1',
    sourceType: 'receiving',
    sourceService: 'wms',
    sourceDocumentId: 'RCV-1',
    skuCode: 'SKU-A',
    quantity: 10,
    uomCode: 'pcs',
    status: 'pending',
    version: 2,
    allowedActions: ['claim'],
  }
}

async function triggerClaimFailure(failure: unknown) {
  claimTask.mockRejectedValueOnce(failure)
  const wrapper = mount(TasksPage, { attachTo: document.body })
  await wrapper.get('[data-testid="task-row"]').trigger('click')
  await flushPromises()
  return wrapper
}

describe('PDA quality tasks page claim recovery', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    tasks.value = [claimableTask()]
  })

  it('continues from the claimed authoritative task into the submit mutation', async () => {
    claimTask.mockResolvedValueOnce({
      ...claimableTask(),
      status: 'in-progress',
      assignedInspectorUserId: 'user-admin',
      version: 3,
      allowedActions: ['submit-inspection'],
      blockReasons: [],
    })
    submitInspection.mockResolvedValueOnce({
      inspectionRecordId: 'RECORD-1',
      result: 'passed',
    })
    const wrapper = mount(TasksPage, {
      attachTo: document.body,
      global: {
        stubs: {
          QualityExecuteStep: {
            props: ['task', 'submitInspection'],
            template:
              '<button data-testid="submit-claimed" @click="submitInspection(task.inspectionTaskId, [])">提交</button>',
          },
        },
      },
    })

    await wrapper.get('[data-testid="task-row"]').trigger('click')
    await flushPromises()
    await wrapper.get('[data-testid="submit-claimed"]').trigger('click')
    await flushPromises()

    expect(claimTask).toHaveBeenCalledWith(expect.objectContaining({ inspectionTaskId: 'TASK-1' }))
    expect(submitInspection).toHaveBeenCalledWith('TASK-1', [])
  })

  it.each([
    [
      '403 scope blocker',
      'task-outside-selected-work-scope' as const,
      '任务不在当前工作范围内，无法领取。',
    ],
    ['422 already claimed blocker', 'task-already-claimed' as const, '任务已由其他检验员领取。'],
  ])('shows the mapped %s as a toast and stays on the task list', async (_case, code, message) => {
    const wrapper = await triggerClaimFailure(new InspectionTaskClaimBlockedError(code))

    expect(document.body.textContent).toContain(message)
    expect(wrapper.find('[data-testid="task-row"]').exists()).toBe(true)
    expect(refresh).not.toHaveBeenCalled()
  })

  it('routes a 409 lifecycle conflict through reset, refresh, and the stable recovery toast', async () => {
    const wrapper = await triggerClaimFailure({
      success: false,
      message: 'lifecycle-conflict',
    })

    expect(refresh).toHaveBeenCalledTimes(1)
    expect(document.body.textContent).toContain('状态已被其他操作更新')
    expect(wrapper.find('[data-testid="task-row"]').exists()).toBe(true)
  })

  it('hides an unknown claim code behind the generic toast and stays on the task list', async () => {
    const wrapper = await triggerClaimFailure({
      status: 422,
      message: 'internal-untrusted-detail',
    })

    expect(document.body.textContent).toContain('任务不可执行，请刷新后重试。')
    expect(document.body.textContent).not.toContain('internal-untrusted-detail')
    expect(wrapper.find('[data-testid="task-row"]').exists()).toBe(true)
    expect(refresh).not.toHaveBeenCalled()
  })
})
