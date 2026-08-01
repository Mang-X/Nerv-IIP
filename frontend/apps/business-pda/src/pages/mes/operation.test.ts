import { RequestTimeoutError } from '@/api/request-timeout'
import type { BusinessConsoleMesOperationTaskRow } from '@nerv-iip/api-client'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, defineComponent, h, nextTick, reactive, ref, shallowRef } from 'vue'

type OperationTaskFixture = Omit<BusinessConsoleMesOperationTaskRow, 'status'> & { status?: string }

const push = vi.fn()
const routeGuardState = vi.hoisted(() => ({
  guard: undefined as (() => boolean) | undefined,
}))
const routeState = vi.hoisted(() => ({
  query: {} as Record<string, string | undefined>,
  replaceQuery: undefined as ((query: Record<string, string | undefined>) => void) | undefined,
}))
vi.mock('vue-router', async () => {
  const { reactive } = await import('vue')
  const route = reactive({ query: routeState.query })
  routeState.replaceQuery = (query) => {
    route.query = query
    routeState.query = query
  }
  return {
    onBeforeRouteLeave: vi.fn((guard: () => boolean) => {
      routeGuardState.guard = guard
    }),
    useRoute: () => route,
    useRouter: () => ({ push }),
  }
})

// --- composable mock: 2 operation tasks with different statuses ---
type ActionOptions = { reasonCode?: string; idempotencyKey: string; contextIdentity?: string }
const completeTask = vi.fn(
  async (_workOrderId: string, _operationTaskId: string, _options: ActionOptions) => {},
)
const startTask = vi.fn(
  async (_workOrderId: string, _operationTaskId: string, _options: ActionOptions) => {},
)
const pauseTask = vi.fn(
  async (_workOrderId: string, _operationTaskId: string, _options: ActionOptions) => {},
)
const resumeTask = vi.fn(
  async (_workOrderId: string, _operationTaskId: string, _options: ActionOptions) => {},
)
const captureOperationActionContextIdentity = vi.fn(
  (action: string, workOrderId: string, operationTaskId: string) =>
    [
      'principal-001',
      'org-001',
      'env-dev',
      'work-center',
      'WC-A',
      workOrderId,
      operationTaskId,
      `mes.operation-task.${action}`,
    ].join('\u0000'),
)
const refresh = vi.fn(async () => {})
const refreshSops = vi.fn()
const createSopFileDownloadGrant = vi.fn()

const filters = reactive({
  organizationId: 'org-001',
  environmentId: 'env-dev',
  keyword: undefined as string | undefined,
  workOrderId: undefined as string | undefined,
  operationTaskId: undefined as string | undefined,
})
const tasksErrorRef = ref<unknown>(null)
const sopsErrorRef = ref<unknown>(null)
const operationScopeMessageRef = ref('')
const operationScopeReadyRef = ref(true)
const operationListScopeRef = ref({
  kind: 'work-center',
  id: 'WC-A',
  displayName: '精加工一线',
})
const operationListContextIdentityRef = ref(
  'principal-001\u0000org-001\u0000env-dev\u0000work-center\u0000WC-A',
)

const defaultTasks: [OperationTaskFixture, OperationTaskFixture] = [
  {
    operationTaskId: 'OP-1',
    workOrderId: 'WO-2026-0001',
    workOrderNo: 'MO-2026-0001',
    operationTaskNo: 'OP-TASK-0010',
    status: 'InProgress',
    operationSequence: 10,
    operationCode: 'OP-CODE-1',
    workCenterId: 'WC-A',
    allowedActions: ['pause', 'complete'],
    blockReasons: [] as string[],
    evaluatedAtUtc: '2026-08-02T08:30:00.000Z',
  },
  {
    operationTaskId: 'OP-2',
    workOrderId: 'WO-2026-0002',
    workOrderNo: 'MO-2026-0002',
    operationTaskNo: 'OP-TASK-0020',
    status: 'Queued',
    operationSequence: 20,
    workCenterId: 'WC-B',
    allowedActions: ['start'],
    blockReasons: [] as string[],
    evaluatedAtUtc: '2026-08-02T08:31:00.000Z',
  },
]
const operationTasksRef = ref<OperationTaskFixture[]>(defaultTasks)
const tasksPendingRef = ref(false)
const tasksRefreshingRef = ref(false)
const tasksSuccessfulRef = ref(true)
const currentSopsRef = ref<Array<Record<string, unknown>>>([])
const workScopeOptionsRef = ref([
  { label: '精加工一线（工作中心）', value: 'work-center:WC-A' },
  { label: '精加工二线（工作中心）', value: 'work-center:WC-B' },
])
const workScopeSelectionRef = ref<string | undefined>('work-center:WC-A')

vi.mock('@/composables/useBusinessMes', () => ({
  useMesOperationTasks: () => ({
    filters,
    operationTasks: computed(() => operationTasksRef.value),
    total: computed(() => operationTasksRef.value.length),
    lastUpdatedAt: ref('2026-07-28T10:20:30.000Z'),
    hasSuccessfulResponse: computed(() => tasksSuccessfulRef.value && !tasksErrorRef.value),
    hasFailedResponse: computed(() => false),
    pending: tasksPendingRef,
    refreshing: tasksRefreshingRef,
    error: tasksErrorRef,
    refresh,
    startTask,
    pauseTask,
    resumeTask,
    completeTask,
    actionPending: ref(false),
    operationListScope: operationListScopeRef,
    operationListContextIdentity: operationListContextIdentityRef,
    operationListScopeMessage: ref(''),
    operationListScopeReady: ref(true),
    operationScopeMessage: operationScopeMessageRef,
    operationScopePending: ref(false),
    operationScopeReady: operationScopeReadyRef,
    captureOperationActionContextIdentity,
  }),
  useMesCurrentOperationSops: () => ({
    filters: {
      organizationId: 'org-001',
      environmentId: 'env-dev',
      operationCode: '',
      workCenterCode: '',
    },
    currentSops: currentSopsRef,
    pending: ref(false),
    error: sopsErrorRef,
    refresh: refreshSops,
    createSopFileDownloadGrant,
  }),
  // 作业范围选择入口自带一个独立实例（#1297）：页面挂载时必须能解析到它。
  useMesWorkScopeSelection: () => ({
    scopeOptions: computed(() => workScopeOptionsRef.value),
    scopeSelectionValue: workScopeSelectionRef,
  }),
}))

import OperationPage from './operation.vue'

describe('PDA MES operation execution page', () => {
  beforeEach(() => {
    completeTask.mockClear()
    startTask.mockClear()
    pauseTask.mockClear()
    resumeTask.mockClear()
    refresh.mockClear()
    refreshSops.mockClear()
    tasksErrorRef.value = null
    tasksRefreshingRef.value = false
    sopsErrorRef.value = null
    operationScopeMessageRef.value = ''
    operationScopeReadyRef.value = true
    operationListScopeRef.value = {
      kind: 'work-center',
      id: 'WC-A',
      displayName: '精加工一线',
    }
    operationListContextIdentityRef.value =
      'principal-001\u0000org-001\u0000env-dev\u0000work-center\u0000WC-A'
    operationTasksRef.value = defaultTasks
    tasksPendingRef.value = false
    tasksSuccessfulRef.value = true
    currentSopsRef.value = []
    createSopFileDownloadGrant.mockClear()
    push.mockClear()
    routeGuardState.guard = undefined
    filters.keyword = undefined
    filters.workOrderId = undefined
    filters.operationTaskId = undefined
    routeState.replaceQuery?.({})
  })

  it('把分页器的真实刷新生命周期绑定给任务列表壳', async () => {
    const wrapper = mount(OperationPage)

    expect(wrapper.getComponent({ name: 'TaskListShell' }).props('refreshing')).toBe(false)
    tasksRefreshingRef.value = true
    await wrapper.vm.$nextTick()
    expect(wrapper.getComponent({ name: 'TaskListShell' }).props('refreshing')).toBe(true)
  })

  function dispatchBeforeUnload() {
    const event = new Event('beforeunload', { cancelable: true })
    window.dispatchEvent(event)
    return event
  }

  it('renders the scan bar and an operation ListRow per task', () => {
    const wrapper = mount(OperationPage)
    expect(wrapper.find('input[placeholder^="扫"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('MO-2026-0001')
    expect(wrapper.text()).toContain('MO-2026-0002')
    expect(wrapper.text()).toContain('当前主体授权作业范围 · 精加工一线（工作中心）')
    // 工序序号可读呈现
    expect(wrapper.text()).toContain('工序 10')
  })

  it('sets filters.keyword when scanning', async () => {
    const wrapper = mount(OperationPage)
    const input = wrapper.get('input[placeholder^="扫"]')
    await input.setValue('WO-2026-0002')
    await input.trigger('keydown.enter')
    expect(filters.keyword).toBe('WO-2026-0002')
  })

  it('opens the action BottomSheet when a row is tapped', async () => {
    const wrapper = mount(OperationPage, { attachTo: document.body })
    const rows = wrapper.findAll('[data-row]')
    await rows[0].trigger('click')
    await flushPromises()
    // BottomSheet 内容 teleport 到 body
    expect(document.body.textContent).toContain('完成')
    wrapper.unmount()
  })

  it('renders the server-evaluated pair, device, gate time, and readable blocker details', async () => {
    operationTasksRef.value = [
      {
        ...defaultTasks[1],
        workOrderId: 'work-order-internal-42',
        operationTaskId: 'operation-task-internal-20',
        workOrderNo: 'MO-2026-0042',
        operationTaskNo: 'OP-TASK-0020',
        operationCode: 'OP-CUT',
        deviceAssetId: 'device-asset-lathe-07',
        deviceAssetCode: 'DEV-LATHE-07',
        deviceAssetName: '七号数控车床',
        allowedActions: [],
        blockReasons: [
          'PREVIOUS_OPERATION_INCOMPLETE: 前序工序尚未完成（OP-1）',
          'MATERIAL_SHORTAGE: 物料 MAT-STEEL 缺口 2',
          'equipment.activeAlarm: 工业遥测存在未解除报警，设备不可用于当前工序。',
          'QUALITY_HOLD_ACTIVE: 工单存在有效质量保留，无法开工',
        ],
        evaluatedAtUtc: '2026-08-02T08:31:00.000Z',
      },
    ]
    const wrapper = mount(OperationPage, { attachTo: document.body })

    await wrapper.get('[data-row]').trigger('click')
    await flushPromises()

    expect(document.body.textContent).toContain('MO-2026-0042')
    expect(document.body.textContent).toContain('OP-TASK-0020')
    expect(document.body.textContent).toContain('七号数控车床（DEV-LATHE-07）')
    expect(document.body.textContent).not.toContain('work-order-internal-42')
    expect(document.body.textContent).not.toContain('operation-task-internal-20')
    expect(document.body.textContent).not.toContain('device-asset-lathe-07')
    expect(document.body.textContent).toContain('2026')
    expect(document.body.textContent).toContain('前序工序')
    expect(document.body.textContent).toContain('物料齐套')
    expect(document.body.textContent).toContain('设备')
    expect(document.body.textContent).toContain('质量')
    expect(document.body.querySelector('[data-testid="action-start"]')).toBeNull()
  })

  it('shows explicit unavailable copy instead of raw identifiers when readable references are absent', async () => {
    operationTasksRef.value = [
      {
        ...defaultTasks[1],
        workOrderId: 'work-order-internal-missing',
        operationTaskId: 'operation-task-internal-missing',
        workOrderNo: undefined,
        operationTaskNo: undefined,
        operationCode: 'OP-STANDARD-20',
        deviceAssetId: 'device-asset-internal-missing',
        allowedActions: [],
      },
    ]
    const wrapper = mount(OperationPage, { attachTo: document.body })

    await wrapper.get('[data-row]').trigger('click')
    await flushPromises()

    expect(document.body.textContent).toContain('工单信息未提供')
    expect(document.body.textContent).toContain('工序任务信息未提供')
    const taskDefinition = [...document.body.querySelectorAll('dt')].find(
      (term) => term.textContent === '工序任务',
    )?.nextElementSibling
    expect(taskDefinition?.textContent).toBe('工序任务信息未提供')
    expect(document.body.textContent).toContain('OP-STANDARD-20')
    expect(document.body.textContent).toContain('设备信息未提供')
    expect(document.body.textContent).not.toContain('work-order-internal-missing')
    expect(document.body.textContent).not.toContain('operation-task-internal-missing')
    expect(document.body.textContent).not.toContain('device-asset-internal-missing')
  })

  it('renders lifecycle buttons only from server allowedActions instead of local status guesses', async () => {
    operationTasksRef.value = [
      {
        ...defaultTasks[0],
        status: 'InProgress',
        allowedActions: ['resume'],
      },
    ]
    const wrapper = mount(OperationPage, { attachTo: document.body })

    await wrapper.get('[data-row]').trigger('click')
    await flushPromises()

    expect(document.body.querySelector('[data-testid="action-resume"]')).not.toBeNull()
    expect(document.body.querySelector('[data-testid="action-pause"]')).toBeNull()
    expect(document.body.querySelector('[data-testid="action-complete"]')).toBeNull()
  })

  it('consumes the workOrderId and operationTaskId deep link and opens that exact task', async () => {
    routeState.replaceQuery?.({
      workOrderId: 'WO-2026-0002',
      operationTaskId: 'OP-2',
    })

    mount(OperationPage, { attachTo: document.body })
    await flushPromises()

    expect(filters.workOrderId).toBe('WO-2026-0002')
    expect(filters.operationTaskId).toBe('OP-2')
    expect(filters.keyword).toBeUndefined()
    expect(document.body.textContent).toContain('MO-2026-0002 · 工序 20')
    expect(document.body.textContent).not.toContain('MO-2026-0001 · 工序 10')
  })

  it('closes the old task and waits for the new pair response when the reused route query changes', async () => {
    routeState.replaceQuery?.({
      workOrderId: 'WO-2026-0001',
      operationTaskId: 'OP-1',
    })
    mount(OperationPage, { attachTo: document.body })
    await flushPromises()
    expect(document.body.textContent).toContain('MO-2026-0001 · 工序 10')

    tasksPendingRef.value = true
    tasksSuccessfulRef.value = false
    operationTasksRef.value = [defaultTasks[0]]
    routeState.replaceQuery?.({
      workOrderId: 'WO-2026-0002',
      operationTaskId: 'OP-2',
    })
    await nextTick()

    expect(filters.workOrderId).toBe('WO-2026-0002')
    expect(filters.operationTaskId).toBe('OP-2')
    expect(filters.keyword).toBeUndefined()
    expect(document.body.querySelector('[data-slot="bottom-sheet"]')).toBeNull()

    operationTasksRef.value = [defaultTasks[1]]
    tasksSuccessfulRef.value = true
    tasksPendingRef.value = false
    await flushPromises()

    expect(document.body.textContent).toContain('MO-2026-0002 · 工序 20')
    expect(document.body.textContent).not.toContain('MO-2026-0001 · 工序 10')
  })

  it('closes a fixed-pair sheet and reopens only from the new scope response', async () => {
    routeState.replaceQuery?.({
      workOrderId: 'WO-2026-0001',
      operationTaskId: 'OP-1',
    })
    mount(OperationPage, { attachTo: document.body })
    await flushPromises()
    expect(document.body.textContent).toContain('MO-2026-0001 · 工序 10')

    tasksPendingRef.value = true
    tasksSuccessfulRef.value = false
    operationListScopeRef.value = {
      kind: 'work-center',
      id: 'WC-B',
      displayName: '精加工二线',
    }
    operationListContextIdentityRef.value =
      'principal-001\u0000org-001\u0000env-dev\u0000work-center\u0000WC-B'
    await nextTick()

    expect(document.body.querySelector('[data-slot="bottom-sheet"]')).toBeNull()

    operationTasksRef.value = [
      {
        ...defaultTasks[0],
        operationSequence: 30,
        workCenterId: 'WC-B',
      },
    ]
    tasksSuccessfulRef.value = true
    tasksPendingRef.value = false
    await flushPromises()

    expect(document.body.textContent).toContain('MO-2026-0001 · 工序 30')
    expect(document.body.textContent).not.toContain('MO-2026-0001 · 工序 10')
  })

  it('closes a fixed-pair sheet and fails closed when the new scope omits the task', async () => {
    routeState.replaceQuery?.({
      workOrderId: 'WO-2026-0001',
      operationTaskId: 'OP-1',
    })
    const wrapper = mount(OperationPage, { attachTo: document.body })
    await flushPromises()
    expect(document.body.textContent).toContain('MO-2026-0001 · 工序 10')

    tasksPendingRef.value = true
    tasksSuccessfulRef.value = false
    operationListScopeRef.value = {
      kind: 'work-center',
      id: 'WC-B',
      displayName: '精加工二线',
    }
    operationListContextIdentityRef.value =
      'principal-001\u0000org-001\u0000env-dev\u0000work-center\u0000WC-B'
    await nextTick()

    expect(document.body.querySelector('[data-slot="bottom-sheet"]')).toBeNull()

    operationTasksRef.value = []
    tasksSuccessfulRef.value = true
    tasksPendingRef.value = false
    await flushPromises()

    expect(wrapper.get('[data-testid="operation-deep-link-message"]').text()).toContain(
      '未在当前主体授权作业范围内找到指定工序任务',
    )
    expect(document.body.querySelector('[data-slot="bottom-sheet"]')).toBeNull()
  })

  it('does not revive a fixed-pair sheet from a stale response after rapid scope changes', async () => {
    routeState.replaceQuery?.({
      workOrderId: 'WO-2026-0001',
      operationTaskId: 'OP-1',
    })
    mount(OperationPage, { attachTo: document.body })
    await flushPromises()
    expect(document.body.textContent).toContain('MO-2026-0001 · 工序 10')

    tasksPendingRef.value = true
    tasksSuccessfulRef.value = false
    operationListContextIdentityRef.value =
      'principal-001\u0000org-001\u0000env-dev\u0000work-center\u0000WC-B'
    await nextTick()
    operationListContextIdentityRef.value =
      'principal-001\u0000org-001\u0000env-dev\u0000work-center\u0000WC-C'
    await nextTick()

    operationTasksRef.value = [defaultTasks[0]]
    await flushPromises()
    expect(document.body.querySelector('[data-slot="bottom-sheet"]')).toBeNull()

    operationTasksRef.value = [
      {
        ...defaultTasks[0],
        operationSequence: 40,
        workCenterId: 'WC-C',
      },
    ]
    tasksSuccessfulRef.value = true
    tasksPendingRef.value = false
    await flushPromises()

    expect(document.body.textContent).toContain('MO-2026-0001 · 工序 40')
    expect(document.body.textContent).not.toContain('MO-2026-0001 · 工序 10')
  })

  it('fails closed when a reused route changes to an incomplete task identity', async () => {
    routeState.replaceQuery?.({ operationTaskId: 'OP-1' })
    const wrapper = mount(OperationPage, { attachTo: document.body })
    await flushPromises()

    expect(wrapper.get('[data-testid="operation-deep-link-message"]').text()).toContain(
      '缺少工单或任务标识',
    )
    expect(wrapper.findAll('[data-row]')).toHaveLength(0)
    expect(document.body.querySelector('[data-slot="bottom-sheet"]')).toBeNull()
  })

  it('fails closed when the exact pair is absent from the authorized response', async () => {
    routeState.replaceQuery?.({
      workOrderId: 'WO-NOT-AUTHORIZED',
      operationTaskId: 'OP-NOT-AUTHORIZED',
    })
    const wrapper = mount(OperationPage, { attachTo: document.body })
    await flushPromises()

    expect(wrapper.get('[data-testid="operation-deep-link-message"]').text()).toContain(
      '未在当前主体授权作业范围内找到指定工序任务',
    )
    expect(wrapper.findAll('[data-row]')).toHaveLength(0)
    expect(document.body.querySelector('[data-slot="bottom-sheet"]')).toBeNull()
  })

  it('shows the missing-scope reason and disables lifecycle actions', async () => {
    operationScopeReadyRef.value = false
    operationScopeMessageRef.value = '尚未选择已授权作业范围，当前操作已禁用。'
    const wrapper = mount(OperationPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    await flushPromises()

    expect(wrapper.get('[data-testid="operation-scope-message"]').text()).toContain(
      '尚未选择已授权作业范围',
    )
    const action = document.body.querySelector<HTMLButtonElement>(
      '[data-testid="action-complete"]',
    )!
    expect(action.disabled).toBe(true)
    action.click()
    await flushPromises()
    expect(completeTask).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('completes a task only after explicit confirmation, calling completeTask with the exact pair', async () => {
    const wrapper = mount(OperationPage, { attachTo: document.body })
    const rows = wrapper.findAll('[data-row]')
    await rows[0].trigger('click')
    await flushPromises()

    // 点"完成"——先进入二次确认，不立即调用
    const completeBtn = document.body.querySelector<HTMLElement>('[data-testid="action-complete"]')!
    completeBtn.click()
    await flushPromises()
    expect(completeTask).not.toHaveBeenCalled()

    // 确认后才调用 —— 携带稳定逐操作幂等键
    const confirmBtn = document.body.querySelector<HTMLElement>('[data-testid="confirm-complete"]')!
    confirmBtn.click()
    await flushPromises()
    expect(completeTask).toHaveBeenCalledWith(
      'WO-2026-0001',
      'OP-1',
      expect.objectContaining({ idempotencyKey: expect.any(String) }),
    )
    expect(completeTask.mock.calls[0][2].idempotencyKey).toBeTruthy()

    // 成功后显示 Result 成功文案
    expect(wrapper.find('[data-result][data-status="success"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('工序已完成')
    expect(wrapper.text()).toContain('MO-2026-0001 · OP-TASK-0010')
    expect(wrapper.text()).not.toContain('WO-2026-0001')
    expect(wrapper.text()).not.toContain('OP-1')
    expect(routeGuardState.guard?.()).toBe(true)
    expect(dispatchBeforeUnload().defaultPrevented).toBe(false)
    wrapper.unmount()
  })

  it('does not show success for an accepted but unconfirmed operation receipt', async () => {
    completeTask.mockRejectedValueOnce(
      Object.assign(new Error('请求已受理，但权威状态尚未确认'), {
        code: 'business-operation-unconfirmed',
      }),
    )
    const wrapper = mount(OperationPage, { attachTo: document.body })
    await wrapper.get('[data-row]').trigger('click')
    await flushPromises()
    document.body.querySelector<HTMLElement>('[data-testid="action-complete"]')!.click()
    await flushPromises()
    document.body.querySelector<HTMLElement>('[data-testid="confirm-complete"]')!.click()
    await flushPromises()

    expect(wrapper.find('[data-result][data-status="success"]').exists()).toBe(false)
    expect(wrapper.find('[data-result][data-status="error"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('MO-2026-0001 · OP-TASK-0010')
    expect(wrapper.text()).not.toContain('WO-2026-0001')
    expect(wrapper.text()).not.toContain('OP-1')
    expect(wrapper.text()).toContain('结果尚未核实')
  })

  it('shows an error Result without locking route/refresh leave after a determinate 4xx', async () => {
    completeTask.mockRejectedValueOnce({ status: 422, message: '数量校验失败' })
    const wrapper = mount(OperationPage, { attachTo: document.body })
    const rows = wrapper.findAll('[data-row]')
    await rows[0].trigger('click')
    await flushPromises()
    document.body.querySelector<HTMLElement>('[data-testid="action-complete"]')!.click()
    await flushPromises()
    document.body.querySelector<HTMLElement>('[data-testid="confirm-complete"]')!.click()
    await flushPromises()

    expect(wrapper.find('[data-result][data-status="error"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('重试')
    expect(routeGuardState.guard?.()).toBe(true)
    expect(dispatchBeforeUnload().defaultPrevented).toBe(false)
    wrapper.unmount()
  })

  it('refreshes and closes stale action context on a typed 409 without offering retry', async () => {
    completeTask.mockRejectedValueOnce({ success: false, message: 'lifecycle-conflict' })
    const wrapper = mount(OperationPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    await flushPromises()
    document.body.querySelector<HTMLElement>('[data-testid="action-complete"]')!.click()
    await flushPromises()
    document.body.querySelector<HTMLElement>('[data-testid="confirm-complete"]')!.click()
    await flushPromises()

    expect(refresh).toHaveBeenCalled()
    expect(wrapper.find('[data-testid="retry-action"]').exists()).toBe(false)
    expect(document.body.textContent).toContain('状态已被其他操作更新')
    expect(document.body.querySelector('[data-testid="confirm-complete"]')).toBeNull()
    expect(routeGuardState.guard?.()).toBe(true)
    expect(dispatchBeforeUnload().defaultPrevented).toBe(false)
    wrapper.unmount()
  })

  it('still closes stale action context and shows the fixed toast when conflict refresh fails', async () => {
    completeTask.mockRejectedValueOnce({ success: false, message: 'lifecycle-conflict' })
    refresh.mockRejectedValueOnce(new Error('refresh unavailable'))
    const wrapper = mount(OperationPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    await flushPromises()
    document.body.querySelector<HTMLElement>('[data-testid="action-complete"]')!.click()
    await flushPromises()
    document.body.querySelector<HTMLElement>('[data-testid="confirm-complete"]')!.click()
    await flushPromises()

    expect(refresh).toHaveBeenCalled()
    expect(wrapper.find('[data-testid="retry-action"]').exists()).toBe(false)
    expect(document.body.textContent).toContain('状态已被其他操作更新')
    expect(document.body.querySelector('[data-testid="confirm-complete"]')).toBeNull()
    wrapper.unmount()
  })

  it('reuses the SAME idempotencyKey on action retry; a different action initiation mints a new key', async () => {
    // 行 0 是 InProgress 工序，可执行「暂停」与「完成」
    completeTask.mockRejectedValueOnce(new Error('lost response'))
    const wrapper = mount(OperationPage, { attachTo: document.body })
    const rows = wrapper.findAll('[data-row]')
    await rows[0].trigger('click')
    await flushPromises()

    // 发起「完成」→ 进入二次确认（铸造稳定键）→ 确认 → 首次失败
    document.body.querySelector<HTMLElement>('[data-testid="action-complete"]')!.click()
    await flushPromises()
    document.body.querySelector<HTMLElement>('[data-testid="confirm-complete"]')!.click()
    await flushPromises()
    expect(wrapper.find('[data-result][data-status="error"]').exists()).toBe(true)

    // 不重新发起，直接重试该动作 → 复用同一 idempotencyKey
    wrapper.get('[data-testid="retry-action"]').trigger('click')
    await flushPromises()

    expect(completeTask).toHaveBeenCalledTimes(2)
    const firstKey = completeTask.mock.calls[0][2].idempotencyKey
    const retryKey = completeTask.mock.calls[1][2].idempotencyKey
    expect(firstKey).toBeTruthy()
    expect(retryKey).toBe(firstKey)

    // 成功后继续 → 重新打开面板并发起新动作（暂停）→ 新键
    wrapper
      .findAll('button')
      .find((b) => b.text() === '继续')!
      .trigger('click')
    await flushPromises()
    const rows2 = wrapper.findAll('[data-row]')
    await rows2[0].trigger('click')
    await flushPromises()
    document.body.querySelector<HTMLElement>('[data-testid="action-pause"]')!.click()
    await flushPromises()

    expect(pauseTask).toHaveBeenCalledTimes(1)
    const pauseKey = pauseTask.mock.calls[0][2].idempotencyKey
    expect(pauseKey).toBeTruthy()
    expect(pauseKey).not.toBe(firstKey)
    wrapper.unmount()
  })

  it('locks route/refresh leave for an unknown result, then unlocks after same-key retry succeeds', async () => {
    completeTask.mockRejectedValueOnce(new RequestTimeoutError()).mockResolvedValueOnce(undefined)
    const wrapper = mount(OperationPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    await flushPromises()
    document.body.querySelector<HTMLElement>('[data-testid="action-complete"]')!.click()
    await flushPromises()
    document.body.querySelector<HTMLElement>('[data-testid="confirm-complete"]')!.click()
    await flushPromises()

    const firstKey = completeTask.mock.calls[0][2].idempotencyKey
    const back = wrapper.get('[data-testid="back-to-list"]')
    expect(back.attributes('disabled')).toBeDefined()
    await back.trigger('click')
    await wrapper.get('[aria-label="返回"]').trigger('click')
    expect(push).not.toHaveBeenCalled()
    expect(routeGuardState.guard?.()).toBe(false)
    expect(dispatchBeforeUnload().defaultPrevented).toBe(true)

    await wrapper.get('[data-testid="retry-action"]').trigger('click')
    await flushPromises()
    expect(completeTask).toHaveBeenCalledTimes(2)
    expect(completeTask.mock.calls[1][2].idempotencyKey).toBe(firstKey)
    expect(routeGuardState.guard?.()).toBe(true)
    expect(dispatchBeforeUnload().defaultPrevented).toBe(false)
    wrapper.unmount()
  })

  it('shows a readable fail-closed result when the frozen retry context has drifted', async () => {
    completeTask
      .mockRejectedValueOnce(new RequestTimeoutError())
      .mockRejectedValueOnce(
        new Error('账号、组织、环境或作业范围已变化，旧操作不能重试。请返回当前列表重新发起。'),
      )
    const wrapper = mount(OperationPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    await flushPromises()
    document.body.querySelector<HTMLElement>('[data-testid="action-complete"]')!.click()
    await flushPromises()
    document.body.querySelector<HTMLElement>('[data-testid="confirm-complete"]')!.click()
    await flushPromises()

    await wrapper.get('[data-testid="retry-action"]').trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('账号、组织、环境或作业范围已变化')
    expect(wrapper.text()).not.toContain('WO-2026-0001')
    expect(wrapper.text()).not.toContain('OP-1')
    expect(completeTask).toHaveBeenCalledTimes(2)
    expect(completeTask.mock.calls[1][2].contextIdentity).toBe(
      completeTask.mock.calls[0][2].contextIdentity,
    )
    expect(wrapper.get('[data-testid="back-to-list"]').attributes('disabled')).toBeUndefined()
  })

  it('removes the beforeunload guard when the operation route component is removed', async () => {
    const addEventListener = vi.spyOn(window, 'addEventListener')
    const removeEventListener = vi.spyOn(window, 'removeEventListener')
    const visible = shallowRef(true)
    const Host = defineComponent({
      setup() {
        return () => (visible.value ? h(OperationPage) : h('div'))
      },
    })

    mount(Host)
    const handler = addEventListener.mock.calls.find(([type]) => type === 'beforeunload')?.[1]
    expect(handler).toBeTypeOf('function')

    visible.value = false
    await nextTick()

    expect(removeEventListener).toHaveBeenCalledWith('beforeunload', handler)
    expect(dispatchBeforeUnload().defaultPrevented).toBe(false)
    addEventListener.mockRestore()
    removeEventListener.mockRestore()
  })

  // P1：SOP 查询失败也要有可操作错误态 + 重试入口（#814 所有 facade）。
  it('SOP 查询超时：SOP 区显示可操作错误面板 + 重试调 refresh', async () => {
    sopsErrorRef.value = new RequestTimeoutError()
    const wrapper = mount(OperationPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click') // OP-1 绑定了标准工序
    await flushPromises()
    const panel = document.body.querySelector<HTMLElement>('[data-testid="sops-error"]')!
    expect(panel).toBeTruthy()
    expect(panel.textContent).toContain('网络超时，请检查连接后重试')
    panel.querySelector<HTMLElement>('[data-testid="retry-list"]')!.click()
    expect(refreshSops).toHaveBeenCalledTimes(1)
    wrapper.unmount()
  })

  // P2：加载失败时空态与错误态互斥，不把网络错误误报成"暂无"。
  it('工序列表加载失败时不显示"暂无工序任务"空态', () => {
    operationTasksRef.value = []
    tasksErrorRef.value = new RequestTimeoutError()
    const wrapper = mount(OperationPage)
    expect(wrapper.find('[data-testid="operation-tasks-error"]').exists()).toBe(true)
    expect(wrapper.text()).not.toContain('暂无工序任务')
    wrapper.unmount()
  })

  // P1：SOP 打开文件失败（超时/离线）时不隐藏 SOP 列表——保留"查看SOP"作为重试入口。
  it('打开 SOP 失败（超时）时保留 SOP 列表与"查看SOP"按钮以便重试', async () => {
    currentSopsRef.value = [
      { fileId: 'F1', fileName: 'SOP-1', documentNumber: 'D1', revision: 'A', effectiveDate: null },
    ]
    createSopFileDownloadGrant.mockRejectedValueOnce(new Error('网络超时，请检查连接后重试'))
    const wrapper = mount(OperationPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    await flushPromises()
    const viewBtn = [...document.body.querySelectorAll<HTMLButtonElement>('button')].find((b) =>
      b.textContent?.includes('查看SOP'),
    )!
    expect(viewBtn).toBeTruthy()
    viewBtn.click()
    await flushPromises()
    // 打开失败文案出现
    expect(document.body.querySelector('[data-testid="sop-file-error"]')?.textContent).toContain(
      '网络超时，请检查连接后重试',
    )
    // 且 SOP 列表与"查看SOP"按钮仍在（可再次点击重试），不被错误文本隐藏
    expect(
      [...document.body.querySelectorAll('button')].some((b) => b.textContent?.includes('查看SOP')),
    ).toBe(true)
    wrapper.unmount()
  })
})
