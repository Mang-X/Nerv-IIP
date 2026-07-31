import { RequestTimeoutError } from '@/api/request-timeout'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, ref } from 'vue'

const route = reactive<{ query: Record<string, unknown> }>({ query: {} })
async function applyNavigation(to: string | { query?: Record<string, unknown> }) {
  if (typeof to === 'string') {
    if (to === '/mes/report') route.query = {}
    return
  }
  route.query = { ...(to.query ?? {}) }
}
const push = vi.fn(applyNavigation)
const replace = vi.fn(applyNavigation)
vi.mock('vue-router', () => ({
  useRouter: () => ({ push, replace }),
  useRoute: () => route,
}))

// --- composable mock: multiple work orders + operation tasks + recordReport spy ---
type ReportEnvelope = {
  success: boolean
  message?: string
  data?: { productionReportId?: string; reportNo?: string } | null
}
const successfulReceipt: ReportEnvelope = {
  success: true,
  data: {
    productionReportId: '019f-report-default',
    reportNo: 'RPT-DEFAULT',
  },
}
const recordReport = vi.fn(
  async (_input: Record<string, unknown>): Promise<ReportEnvelope> => successfulReceipt,
)
const refreshWorkOrders = vi.fn(async () => {})
const refreshTasks = vi.fn(async () => {})
const refreshExactTask = vi.fn(async () => {})
const cancelPendingTasks = vi.fn()
const workOrdersErrorRef = ref<unknown>(null)
const workOrdersLastUpdatedAtRef = ref<string | null>('2026-07-28T10:20:30Z')
const tasksErrorRef = ref<unknown>(null)
const workOrdersPendingRef = ref(false)
const tasksPendingRef = ref(false)
const reportScopeMessageRef = ref('')
const reportScopePendingRef = ref(false)
const reportScopeReadyRef = ref(true)
let operationTaskDiscoveryCalls = 0

const workOrderFilters = reactive({
  organizationId: 'org-001',
  environmentId: 'env-dev',
  keyword: undefined as string | undefined,
  workOrderId: undefined as string | undefined,
})
const taskFilters = reactive({
  keyword: undefined as string | undefined,
  workOrderId: undefined as string | undefined,
})

const defaultWorkOrders = [
  { workOrderId: 'WO-2026-0001', skuId: 'SKU-A', quantity: 100, status: 'Released' },
  { workOrderId: 'WO-2026-0002', skuId: 'SKU-B', quantity: 50, status: 'Released' },
]
const workOrdersRef = ref<Array<Record<string, unknown>>>(defaultWorkOrders)

const defaultOperationTasks = [
  {
    operationTaskId: 'OP-1',
    workOrderId: 'WO-2026-0001',
    status: 'InProgress',
    operationSequence: 10,
    workCenterId: 'WC-A',
  },
  {
    operationTaskId: 'OP-2',
    workOrderId: 'WO-2026-0001',
    status: 'Queued',
    operationSequence: 20,
    workCenterId: 'WC-B',
  },
  {
    operationTaskId: 'OP-3',
    workOrderId: 'WO-2026-0002',
    status: 'Ready',
    operationSequence: 10,
    workCenterId: 'WC-C',
  },
]
const operationTasksRef = ref<Array<Record<string, unknown>>>(defaultOperationTasks)
const workOrderDetailRef = ref<Record<string, unknown> | null>({
  ...defaultWorkOrders[0],
  operationTasks: defaultOperationTasks.filter(
    (task) => task.workOrderId === defaultWorkOrders[0].workOrderId,
  ),
})
const workOrderDetailPendingRef = ref(false)
const workOrderDetailErrorRef = ref<unknown>(null)
const workOrderDetailLastUpdatedAtRef = ref<string | null>('2026-07-28T10:20:31Z')
const workOrderDetailHasSuccessfulResponseRef = ref(true)
const workOrderDetailHasFailedResponseRef = ref(false)
const refreshWorkOrderDetail = vi.fn(async () => {})
const exactTaskRef = ref<Record<string, unknown> | null | undefined>(undefined)
const exactTaskPendingRef = ref(false)
const exactTaskErrorRef = ref<unknown>(null)
const exactTaskScopeReadyRef = ref(true)
const exactTaskScopeMessageRef = ref('')
const workScopeOptionsRef = ref([
  { label: '精加工一线（工作中心）', value: 'work-center:WC-A' },
  { label: '精加工二线（工作中心）', value: 'work-center:WC-B' },
])
const workScopeSelectionRef = ref<string | undefined>('work-center:WC-A')

vi.mock('@/composables/useBusinessMes', () => ({
  useMesWorkOrders: () => ({
    filters: workOrderFilters,
    workOrders: computed(() => workOrdersRef.value),
    total: computed(() => workOrdersRef.value.length),
    pending: workOrdersPendingRef,
    error: workOrdersErrorRef,
    refresh: refreshWorkOrders,
    lastUpdatedAt: workOrdersLastUpdatedAtRef,
    hasSuccessfulResponse: computed(() => !workOrdersPendingRef.value && !workOrdersErrorRef.value),
    hasFailedResponse: computed(() => false),
    workOrderReadScope: ref({
      kind: 'work-center',
      id: 'WC-A',
      displayName: '精加工一线',
    }),
    workOrderReadScopeMessage: ref(''),
    workOrderReadScopeReady: ref(true),
  }),
  useMesOperationTasks: () => {
    operationTaskDiscoveryCalls += 1
    return {
      filters: taskFilters,
      operationTasks: computed(() => operationTasksRef.value),
      total: computed(() => operationTasksRef.value.length),
      pending: tasksPendingRef,
      error: tasksErrorRef,
      refresh: refreshTasks,
      cancelPendingTasks,
      startTask: vi.fn(),
      pauseTask: vi.fn(),
      resumeTask: vi.fn(),
      completeTask: vi.fn(),
      actionPending: ref(false),
    }
  },
  useMesWorkOrderDetail: (workOrderId: { value: string }) => ({
    workOrder: computed(() => {
      if (workOrderDetailRef.value?.workOrderId === workOrderId.value) {
        return workOrderDetailRef.value
      }
      const workOrder = workOrdersRef.value.find(
        (candidate) => candidate.workOrderId === workOrderId.value,
      )
      return workOrder
        ? {
            ...workOrder,
            operationTasks: operationTasksRef.value.filter(
              (task) => task.workOrderId === workOrderId.value,
            ),
          }
        : null
    }),
    pending: workOrderDetailPendingRef,
    error: workOrderDetailErrorRef,
    refresh: refreshWorkOrderDetail,
    lastUpdatedAt: workOrderDetailLastUpdatedAtRef,
    hasSuccessfulResponse: workOrderDetailHasSuccessfulResponseRef,
    hasFailedResponse: workOrderDetailHasFailedResponseRef,
    workOrderReadScope: ref({
      kind: 'work-center',
      id: 'WC-A',
      displayName: '精加工一线',
    }),
    workOrderReadScopeMessage: ref(''),
  }),
  useMesExactOperationTask: (
    workOrderId: { value: string },
    operationTaskId: { value: string },
    detail: { value?: Record<string, unknown> | null },
  ) => ({
    task: computed(() => {
      if (exactTaskRef.value !== undefined) return exactTaskRef.value
      if (workOrderDetailPendingRef.value || detail.value?.workOrderId !== workOrderId.value) {
        return null
      }
      return (
        operationTasksRef.value.find(
          (task) =>
            task.workOrderId === workOrderId.value &&
            task.operationTaskId === operationTaskId.value,
        ) ?? null
      )
    }),
    pending: exactTaskPendingRef,
    error: exactTaskErrorRef,
    refresh: refreshExactTask,
    reportingReadScopeReady: exactTaskScopeReadyRef,
    reportingReadScopeMessage: exactTaskScopeMessageRef,
  }),
  useMesProductionReports: () => ({
    filters: reactive({}),
    productionReports: computed(() => []),
    total: computed(() => 0),
    pending: ref(false),
    error: ref(null),
    refresh: vi.fn(),
    recordReport,
    reportScopeMessage: reportScopeMessageRef,
    reportScopePending: reportScopePendingRef,
    reportScopeReady: reportScopeReadyRef,
  }),
  useMesTelemetryProductionReportCandidates: () => ({
    candidates: computed(() => []),
    total: computed(() => 0),
    pending: ref(false),
    promote: vi.fn(),
    dismiss: vi.fn(),
  }),
  // 作业范围选择入口自带一个独立实例（#1297）：页面挂载时必须能解析到它。
  useMesWorkScopeSelection: () => ({
    scopeOptions: computed(() => workScopeOptionsRef.value),
    scopeSelectionValue: workScopeSelectionRef,
  }),
}))

import ReportPage from './report.vue'

async function selectWorkOrder(wrapper: ReturnType<typeof mount>, index = 0) {
  const rows = wrapper.findAll('[data-row]')
  await rows[index].trigger('click')
  await flushPromises()
}

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise
    reject = rejectPromise
  })
  return { promise, resolve, reject }
}

describe('PDA MES production reporting page', () => {
  beforeEach(() => {
    recordReport.mockClear()
    recordReport.mockResolvedValue(successfulReceipt)
    push.mockClear()
    replace.mockClear()
    refreshWorkOrders.mockClear()
    refreshTasks.mockClear()
    refreshWorkOrderDetail.mockClear()
    refreshExactTask.mockClear()
    cancelPendingTasks.mockClear()
    workOrdersErrorRef.value = null
    tasksErrorRef.value = null
    workOrdersPendingRef.value = false
    tasksPendingRef.value = false
    reportScopeMessageRef.value = ''
    reportScopePendingRef.value = false
    reportScopeReadyRef.value = true
    workOrdersRef.value = defaultWorkOrders
    operationTasksRef.value = defaultOperationTasks
    workOrderDetailRef.value = {
      ...defaultWorkOrders[0],
      operationTasks: defaultOperationTasks.filter(
        (task) => task.workOrderId === defaultWorkOrders[0].workOrderId,
      ),
    }
    workOrderDetailPendingRef.value = false
    workOrderDetailErrorRef.value = null
    workOrderDetailHasSuccessfulResponseRef.value = true
    workOrderDetailHasFailedResponseRef.value = false
    refreshWorkOrderDetail.mockClear()
    exactTaskRef.value = undefined
    exactTaskPendingRef.value = false
    exactTaskErrorRef.value = null
    exactTaskScopeReadyRef.value = true
    exactTaskScopeMessageRef.value = ''
    operationTaskDiscoveryCalls = 0
    workOrderFilters.keyword = undefined
    taskFilters.workOrderId = undefined
    route.query = {}
  })

  it('starts on the select-work-order step listing work orders', () => {
    const wrapper = mount(ReportPage)
    // ScanBar 可见用于扫工单
    expect(wrapper.find('input[placeholder^="扫"]').exists()).toBe(true)
    // 工单号可读呈现
    expect(wrapper.text()).toContain('WO-2026-0001')
    expect(wrapper.text()).toContain('WO-2026-0002')
    // 尚未到选工序，列表里不应出现工序序号
    expect(wrapper.text()).not.toContain('工序 10')
  })

  it('shows the missing-scope reason and keeps production reporting disabled', async () => {
    reportScopeReadyRef.value = false
    reportScopeMessageRef.value = '尚未选择已授权作业范围，当前操作已禁用。'
    route.query = { workOrderId: 'WO-2026-0001', operationTaskId: 'OP-1' }
    const wrapper = mount(ReportPage, { attachTo: document.body })
    await flushPromises()

    expect(wrapper.get('[data-testid="report-scope-message"]').text()).toContain(
      '尚未选择已授权作业范围',
    )
    const input = document.body.querySelector<HTMLInputElement>('[data-testid="good-quantity"]')!
    input.value = '1'
    input.dispatchEvent(new Event('input'))
    await flushPromises()
    const submit = document.body.querySelector<HTMLButtonElement>('[data-testid="submit-report"]')!
    expect(submit.disabled).toBe(true)
    submit.click()
    await flushPromises()
    expect(recordReport).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it.each([
    ['未选择', '尚未选择已授权作业范围，当前操作已禁用。'],
    ['核验失败', '作业范围核验失败，当前操作已禁用。请刷新后重试。'],
  ])(
    'shows the reporting-read scope %s reason instead of claiming the exact task is absent',
    async (_state, scopeMessage) => {
      workOrderDetailRef.value = {
        ...defaultWorkOrders[0],
        operationTasks: [],
      }
      exactTaskRef.value = null
      exactTaskScopeReadyRef.value = false
      exactTaskScopeMessageRef.value = scopeMessage
      route.query = {
        workOrderId: 'WO-2026-0001',
        operationTaskId: 'OP-READ-SCOPE',
      }

      const wrapper = mount(ReportPage)
      await flushPromises()

      const issue = wrapper.get('[data-testid="report-route-issue"]').text()
      expect(issue).toContain('报工任务读取范围未就绪')
      expect(issue).toContain(scopeMessage)
      expect(issue).not.toContain('未找到工单')
      expect(issue).not.toContain('未找到工单 WO-2026-0001 下的工序任务')
      expect(recordReport).not.toHaveBeenCalled()
      expect(refreshExactTask).not.toHaveBeenCalled()
    },
  )

  it('shows scope, source, count, and successful-response time for both report lists', async () => {
    const wrapper = mount(ReportPage)

    expect(wrapper.text()).toContain('范围：当前主体授权工单范围 · 精加工一线（工作中心）')
    expect(wrapper.text()).toContain('来源：生产工单服务（服务端按当前主体与所选授权工单范围过滤）')
    expect(wrapper.text()).toContain('已加载 2 / 共 2')
    expect(wrapper.text()).toContain('最近成功响应')

    await selectWorkOrder(wrapper, 0)

    expect(wrapper.text()).toContain('来源：生产工序服务（当前主体授权工单详情返回集合')
    expect(wrapper.text()).toContain('已加载 2 / 共 2')
  })

  it('explains that an empty operation collection is not a personal-task empty state', async () => {
    workOrderDetailRef.value = {
      ...defaultWorkOrders[0],
      operationTasks: [],
    }
    const wrapper = mount(ReportPage)
    await selectWorkOrder(wrapper, 0)

    expect(wrapper.text()).toContain('当前空态只代表所选授权工单返回的工序集合为空')
  })

  it('detail failure blocks route identity and shows failed metadata plus a retry action', async () => {
    route.query = { workOrderId: 'WO-2026-0001' }
    workOrderDetailRef.value = null
    workOrderDetailErrorRef.value = new Error('工单详情查询失败')
    workOrderDetailHasSuccessfulResponseRef.value = false
    workOrderDetailHasFailedResponseRef.value = true

    const wrapper = mount(ReportPage)
    await flushPromises()

    expect(wrapper.get('[data-testid="report-route-issue"]').text()).toContain('详情加载失败')
    expect(wrapper.get('[data-testid="list-failure-explanation"]').text()).toContain(
      '工单详情服务未成功返回',
    )
    expect(wrapper.get('[data-testid="work-order-detail-error"]').text()).toContain(
      '工单详情查询失败',
    )
    expect(wrapper.text()).not.toContain('未找到工单 WO-2026-0001')
    expect(wrapper.text()).not.toContain('该工单暂无工序')

    await wrapper.get('[data-testid="work-order-detail-error"] button').trigger('click')
    expect(refreshWorkOrderDetail).toHaveBeenCalledTimes(1)
  })

  it('scanning sets the work-order keyword filter', async () => {
    const wrapper = mount(ReportPage)
    const input = wrapper.get('input[placeholder^="扫"]')
    await input.setValue('WO-2026-0002')
    await input.trigger('keydown.enter')
    expect(workOrderFilters.keyword).toBe('WO-2026-0002')
  })

  it('shows detail operations after a work order is selected without list discovery', async () => {
    const wrapper = mount(ReportPage)
    await selectWorkOrder(wrapper, 0)
    // 选工序步出现工序序号
    expect(wrapper.text()).toContain('工序 10')
    expect(wrapper.text()).toContain('工序 20')
    expect(operationTaskDiscoveryCalls).toBe(0)
  })

  it('detail 已成功时立即展示任务且 report 页面不创建旧 operation-task list discovery', async () => {
    tasksPendingRef.value = true
    tasksErrorRef.value = new Error('non-authority list failed')
    route.query = { workOrderId: 'WO-2026-0001' }
    const wrapper = mount(ReportPage)
    await flushPromises()

    expect(wrapper.text()).toContain('WO-2026-0001 · 工序 10')
    expect(wrapper.text()).not.toContain('加载工序失败')
    expect(operationTaskDiscoveryCalls).toBe(0)
  })

  it('切换工单后旧工序行不能打开或提交', async () => {
    const wrapper = mount(ReportPage, { attachTo: document.body })
    await selectWorkOrder(wrapper, 0)
    expect(wrapper.text()).toContain('WO-2026-0001 · 工序 10')
    const staleTaskRow = wrapper
      .findAll('[data-row]')
      .find((row) => row.text().includes('WO-2026-0001 · 工序 10'))
    expect(staleTaskRow, '切换前必须真实存在旧工单任务行').toBeDefined()

    await wrapper.get('[data-testid="change-work-order"]').trigger('click')
    await selectWorkOrder(wrapper, 1)
    expect(wrapper.text()).toContain('当前工单')
    expect(wrapper.text()).toContain('WO-2026-0002')

    // 模拟 reactive query 已切 key、但旧 key 的任务行在新请求期间仍留在 data 中。
    await staleTaskRow!.trigger('click')
    await flushPromises()

    const goodInput = document.body.querySelector<HTMLInputElement>('[data-testid="good-quantity"]')
    expect(goodInput).toBeNull()
    expect(recordReport).not.toHaveBeenCalled()
  })

  it('可从同时包含工单和工序任务 ID 的 URL 直达同一报工实体', async () => {
    route.query = {
      workOrderId: 'WO-2026-0002',
      operationTaskId: 'OP-3',
    }

    const wrapper = mount(ReportPage, { attachTo: document.body })
    await flushPromises()

    expect(wrapper.text()).toContain('当前工单')
    expect(wrapper.text()).toContain('WO-2026-0002')
    expect(document.body.textContent).toContain('WO-2026-0002 · 工序 10')
    expect(document.body.querySelector('[data-testid="good-quantity"]')).not.toBeNull()
  })

  it('快速切换时隐藏 pending/乱序任务，只绑定最新完成的 query pair', async () => {
    route.query = {
      workOrderId: 'WO-2026-0001',
      operationTaskId: 'OP-1',
    }
    const wrapper = mount(ReportPage, { attachTo: document.body })
    await flushPromises()
    expect(document.body.querySelector('[data-testid="good-quantity"]')).not.toBeNull()

    workOrderDetailPendingRef.value = true
    route.query = {
      workOrderId: 'WO-2026-0002',
      operationTaskId: 'OP-3',
    }
    await flushPromises()

    expect(wrapper.text()).not.toContain('WO-2026-0001 · 工序 10')
    expect(wrapper.text()).not.toContain('该工单暂无工序')
    expect(document.body.querySelector('[data-testid="good-quantity"]')).toBeNull()

    // 旧请求迟到时仍处于新 key 的 pending 阶段，不得重新启用任何实体。
    workOrderDetailRef.value = {
      ...defaultWorkOrders[0],
      operationTasks: [defaultOperationTasks[0]],
    }
    await flushPromises()
    expect(document.body.querySelector('[data-testid="good-quantity"]')).toBeNull()

    // 只有新 key 的响应完成后，才绑定 URL 请求的精确 pair。
    workOrderDetailRef.value = {
      ...defaultWorkOrders[1],
      operationTasks: [defaultOperationTasks[2]],
    }
    workOrderDetailPendingRef.value = false
    await flushPromises()
    expect(document.body.textContent).toContain('WO-2026-0002 · 工序 10')
    expect(document.body.querySelector('[data-testid="good-quantity"]')).not.toBeNull()
  })

  it('旧提交迟到不能覆盖或清除新 pair 的提交状态', async () => {
    const firstRequest = deferred<ReportEnvelope>()
    const secondRequest = deferred<ReportEnvelope>()
    recordReport
      .mockImplementationOnce(() => firstRequest.promise)
      .mockImplementationOnce(() => secondRequest.promise)

    route.query = {
      workOrderId: 'WO-2026-0001',
      operationTaskId: 'OP-1',
    }
    const wrapper = mount(ReportPage, { attachTo: document.body })
    await flushPromises()

    const firstGood = document.body.querySelector<HTMLInputElement>(
      '[data-testid="good-quantity"]',
    )!
    firstGood.value = '2'
    firstGood.dispatchEvent(new Event('input'))
    await flushPromises()
    document.body.querySelector<HTMLButtonElement>('[data-testid="submit-report"]')!.click()
    await flushPromises()
    expect(recordReport).toHaveBeenCalledTimes(1)

    route.query = {
      workOrderId: 'WO-2026-0002',
      operationTaskId: 'OP-3',
    }
    await flushPromises()
    const secondGood = document.body.querySelector<HTMLInputElement>(
      '[data-testid="good-quantity"]',
    )!
    secondGood.value = '3'
    secondGood.dispatchEvent(new Event('input'))
    await flushPromises()
    document.body.querySelector<HTMLButtonElement>('[data-testid="submit-report"]')!.click()
    await flushPromises()
    expect(recordReport).toHaveBeenCalledTimes(2)

    firstRequest.resolve(successfulReceipt)
    await flushPromises()
    expect(wrapper.find('[data-result][data-status="success"]').exists()).toBe(false)
    expect(
      document.body.querySelector<HTMLButtonElement>('[data-testid="submit-report"]')!.disabled,
    ).toBe(true)

    secondRequest.resolve(successfulReceipt)
    await flushPromises()
    expect(wrapper.find('[data-result][data-status="success"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('WO-2026-0002 · OP-3')
    expect(wrapper.text()).not.toContain('WO-2026-0001 · OP-1')
  })

  it('A pending → B → A 重新绑定同一 intent，不得铸新 key 或重复写，A 成功可恢复', async () => {
    const firstRequest = deferred<ReportEnvelope>()
    recordReport.mockImplementationOnce(() => firstRequest.promise)
    route.query = { workOrderId: 'WO-2026-0001', operationTaskId: 'OP-1' }
    const wrapper = mount(ReportPage, { attachTo: document.body })
    await flushPromises()

    const input = document.body.querySelector<HTMLInputElement>('[data-testid="good-quantity"]')!
    input.value = '2'
    input.dispatchEvent(new Event('input'))
    await flushPromises()
    document.body.querySelector<HTMLButtonElement>('[data-testid="submit-report"]')!.click()
    await flushPromises()
    const firstKey = recordReport.mock.calls[0][0].idempotencyKey

    route.query = { workOrderId: 'WO-2026-0002', operationTaskId: 'OP-3' }
    await flushPromises()
    route.query = { workOrderId: 'WO-2026-0001', operationTaskId: 'OP-1' }
    await flushPromises()

    expect(recordReport).toHaveBeenCalledTimes(1)
    expect(
      document.body.querySelector<HTMLButtonElement>('[data-testid="submit-report"]')!.disabled,
    ).toBe(true)
    firstRequest.resolve(successfulReceipt)
    await flushPromises()
    expect(wrapper.find('[data-result][data-status="success"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('RPT-DEFAULT')
    expect(recordReport.mock.calls[0][0].idempotencyKey).toBe(firstKey)
  })

  it('A pending → B → A 的 reject 恢复同一 pair 错误，并以同 key retry', async () => {
    const firstRequest = deferred<ReportEnvelope>()
    recordReport.mockImplementationOnce(() => firstRequest.promise)
    route.query = { workOrderId: 'WO-2026-0001', operationTaskId: 'OP-1' }
    const wrapper = mount(ReportPage, { attachTo: document.body })
    await flushPromises()
    const input = document.body.querySelector<HTMLInputElement>('[data-testid="good-quantity"]')!
    input.value = '2'
    input.dispatchEvent(new Event('input'))
    await flushPromises()
    document.body.querySelector<HTMLButtonElement>('[data-testid="submit-report"]')!.click()
    await flushPromises()
    const firstKey = recordReport.mock.calls[0][0].idempotencyKey

    route.query = { workOrderId: 'WO-2026-0002', operationTaskId: 'OP-3' }
    await flushPromises()
    route.query = { workOrderId: 'WO-2026-0001', operationTaskId: 'OP-1' }
    await flushPromises()
    firstRequest.reject(new Error('A lost response'))
    await flushPromises()

    expect(wrapper.find('[data-result][data-status="error"]').exists()).toBe(true)
    await wrapper.get('[data-testid="retry-report"]').trigger('click')
    await flushPromises()
    expect(recordReport).toHaveBeenCalledTimes(2)
    expect(recordReport.mock.calls[1][0].idempotencyKey).toBe(firstKey)
  })

  it('第 101+ 工单及其第 101+ 工序任务可从 URL 直达，不依赖首屏 bounded list', async () => {
    const targetWorkOrder = {
      workOrderId: 'WO-OUTSIDE-101',
      skuId: 'SKU-OUTSIDE',
      quantity: 1,
      status: 'Released',
    }
    const targetTask = {
      operationTaskId: 'OP-OUTSIDE-101',
      workOrderId: targetWorkOrder.workOrderId,
      status: 'Ready',
      operationSequence: 1010,
      workCenterId: 'WC-OUTSIDE',
    }
    workOrdersRef.value = Array.from({ length: 100 }, (_, index) => ({
      workOrderId: `WO-FIRST-${index + 1}`,
      skuId: `SKU-${index + 1}`,
      quantity: 1,
      status: 'Released',
    }))
    operationTasksRef.value = Array.from({ length: 100 }, (_, index) => ({
      operationTaskId: `OP-FIRST-${index + 1}`,
      workOrderId: targetWorkOrder.workOrderId,
      status: 'Ready',
      operationSequence: index + 1,
      workCenterId: 'WC-FIRST',
    }))
    workOrderDetailRef.value = {
      ...targetWorkOrder,
      operationTasks: [...operationTasksRef.value, targetTask],
    }
    route.query = {
      workOrderId: targetWorkOrder.workOrderId,
      operationTaskId: targetTask.operationTaskId,
    }

    const wrapper = mount(ReportPage, { attachTo: document.body })
    await flushPromises()
    expect(wrapper.text()).toContain(targetWorkOrder.workOrderId)
    expect(document.body.textContent).toContain('工序 1010')
    expect(document.body.querySelector('[data-testid="good-quantity"]')).not.toBeNull()
  })

  it('完整 pair 的第 501+ 工序任务通过窄 exact resolver 直达', async () => {
    const workOrderId = 'WO-MANY-TASKS'
    const operationTaskId = 'OP-501'
    workOrderDetailRef.value = {
      workOrderId,
      skuId: 'SKU-MANY',
      quantity: 1,
      status: 'Released',
      operationTasks: Array.from({ length: 500 }, (_, index) => ({
        operationTaskId: `OP-${index + 1}`,
        workOrderId,
        status: 'Ready',
        operationSequence: index + 1,
        workCenterId: 'WC-MANY',
      })),
    }
    exactTaskRef.value = {
      operationTaskId,
      workOrderId,
      status: 'Ready',
      operationSequence: 501,
      workCenterId: 'WC-MANY',
    }
    route.query = { workOrderId, operationTaskId }

    const wrapper = mount(ReportPage, { attachTo: document.body })
    await flushPromises()
    expect(document.body.textContent).toContain('工序 501')
    expect(document.body.querySelector('[data-testid="good-quantity"]')).not.toBeNull()
  })

  it('工单 detail 网络或授权失败时显示加载失败并 fail closed，不误报不存在', async () => {
    workOrderDetailErrorRef.value = new Error('403')
    route.query = { workOrderId: 'WO-2026-0001', operationTaskId: 'OP-1' }
    const wrapper = mount(ReportPage, { attachTo: document.body })
    await flushPromises()
    expect(wrapper.get('[data-testid="report-route-issue"]').text()).toContain('详情加载失败')
    expect(wrapper.text()).not.toContain('未找到工单')
    expect(document.body.querySelector('[data-testid="good-quantity"]')).toBeNull()
    expect(recordReport).not.toHaveBeenCalled()
  })

  it('HTTP 成功但回执 envelope 失败时必须 fail closed', async () => {
    recordReport.mockResolvedValueOnce({
      success: false,
      message: '回执实体校验失败',
      data: null,
    })
    route.query = {
      workOrderId: 'WO-2026-0002',
      operationTaskId: 'OP-3',
    }
    const wrapper = mount(ReportPage, { attachTo: document.body })
    await flushPromises()

    const goodInput = document.body.querySelector<HTMLInputElement>(
      '[data-testid="good-quantity"]',
    )!
    goodInput.value = '3'
    goodInput.dispatchEvent(new Event('input'))
    await flushPromises()
    document.body.querySelector<HTMLButtonElement>('[data-testid="submit-report"]')!.click()
    await flushPromises()

    expect(wrapper.find('[data-result][data-status="error"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('回执实体校验失败')
    expect(wrapper.text()).not.toContain('报工成功')
  })

  it.each([
    ['data null', null],
    ['data empty', {}],
    ['productionReportId missing', { reportNo: 'RPT-ONLY' }],
    ['reportNo missing', { productionReportId: '019f-only-id' }],
    ['productionReportId blank', { productionReportId: '   ', reportNo: 'RPT-1' }],
    ['reportNo blank', { productionReportId: '019f-id-1', reportNo: '   ' }],
  ])('success true 但 %s 时必须 fail closed', async (_name, data) => {
    recordReport.mockResolvedValueOnce({ success: true, data })
    route.query = { workOrderId: 'WO-2026-0001', operationTaskId: 'OP-1' }
    const wrapper = mount(ReportPage, { attachTo: document.body })
    await flushPromises()
    const input = document.body.querySelector<HTMLInputElement>('[data-testid="good-quantity"]')!
    input.value = '1'
    input.dispatchEvent(new Event('input'))
    await flushPromises()
    document.body.querySelector<HTMLButtonElement>('[data-testid="submit-report"]')!.click()
    await flushPromises()
    expect(wrapper.find('[data-result][data-status="error"]').exists()).toBe(true)
    expect(wrapper.text()).not.toContain('报工成功')
  })

  it('同一路由 query 变化及浏览器前进后退会重新绑定 pair 并清空数量', async () => {
    route.query = {
      workOrderId: 'WO-2026-0001',
      operationTaskId: 'OP-1',
    }
    const wrapper = mount(ReportPage, { attachTo: document.body })
    await flushPromises()

    const firstGood = document.body.querySelector<HTMLInputElement>(
      '[data-testid="good-quantity"]',
    )!
    firstGood.value = '7'
    firstGood.dispatchEvent(new Event('input'))
    await flushPromises()

    // 模拟同 route 的 query push / 浏览器 forward。
    route.query = {
      workOrderId: 'WO-2026-0002',
      operationTaskId: 'OP-3',
    }
    await flushPromises()
    expect(document.body.textContent).toContain('WO-2026-0002 · 工序 10')
    expect(
      document.body.querySelector<HTMLInputElement>('[data-testid="good-quantity"]')!.value,
    ).toBe('0')

    // 模拟浏览器 back。
    route.query = {
      workOrderId: 'WO-2026-0001',
      operationTaskId: 'OP-1',
    }
    await flushPromises()
    expect(document.body.textContent).toContain('WO-2026-0001 · 工序 10')
    expect(document.body.textContent).not.toContain('WO-2026-0002 · 工序 10')
    expect(
      document.body.querySelector<HTMLInputElement>('[data-testid="good-quantity"]')!.value,
    ).toBe('0')
    expect(recordReport).not.toHaveBeenCalled()
    expect(operationTaskDiscoveryCalls).toBe(0)
  })

  it.each([
    ['缺少工单 ID', { operationTaskId: 'OP-1' }, '报工链接缺少工单 ID'],
    // Not "工单不存在": with no error and no bound response the client holds no
    // verdict on the work order at all. The gateway returns the same 403
    // `work-scope-not-authorized` whether the work order is missing or merely
    // out of scope, so a nonexistent id can only ever reach the page as an
    // error — never as this state.
    [
      '工单详情未取到',
      { workOrderId: 'WO-MISSING', operationTaskId: 'OP-404' },
      '工单 WO-MISSING 的详情尚未取到',
    ],
    [
      '工序任务不存在',
      { workOrderId: 'WO-2026-0002', operationTaskId: 'OP-MISSING' },
      '未找到工单 WO-2026-0002 下的工序任务 OP-MISSING',
    ],
    [
      'pair 不匹配',
      { workOrderId: 'WO-2026-0002', operationTaskId: 'OP-1' },
      '未找到工单 WO-2026-0002 下的工序任务 OP-1',
    ],
  ])('%s 时显示明确安全状态且不写入', async (_name, query, message) => {
    route.query = query
    const wrapper = mount(ReportPage, { attachTo: document.body })
    await flushPromises()

    expect(wrapper.get('[data-testid="report-route-issue"]').text()).toContain(message)
    expect(document.body.querySelector('[data-testid="good-quantity"]')).toBeNull()
    expect(recordReport).not.toHaveBeenCalled()
  })

  it('「详情未取到」与「详情被拒」是两种可区分的事实，且都不谎称工单不存在', async () => {
    // Fact 1: nothing failed, we just hold no detail — retryable, and explicitly
    // agnostic about whether the work order exists.
    route.query = { workOrderId: 'WO-MISSING', operationTaskId: 'OP-404' }
    const unavailable = mount(ReportPage, { attachTo: document.body })
    await flushPromises()

    const unavailablePanel = unavailable.get('[data-testid="work-order-detail-unavailable"]')
    expect(unavailablePanel.text()).toContain('尚未取到')
    expect(unavailablePanel.text()).toContain('无法判断该工单是否存在')
    expect(unavailable.get('[data-testid="report-route-issue"]').text()).toContain('尚未取到')
    expect(document.body.querySelector('[data-testid="work-order-detail-error"]')).toBeNull()
    expect(unavailable.text()).not.toContain('未找到工单')
    unavailable.unmount()

    // Fact 2: the gateway refused the work order (403 covers both "missing" and
    // "out of scope"). Different panel, different copy — and still no fabricated
    // not-found verdict.
    workOrderDetailErrorRef.value = Object.assign(new Error('work-scope-not-authorized'), {
      status: 403,
    })
    workOrderDetailHasFailedResponseRef.value = true
    const refused = mount(ReportPage, { attachTo: document.body })
    await flushPromises()

    expect(refused.get('[data-testid="report-route-issue"]').text()).toContain('详情加载失败')
    expect(refused.get('[data-testid="work-order-detail-error"]').text()).toContain(
      '当前账号无此操作权限',
    )
    expect(document.body.querySelector('[data-testid="work-order-detail-unavailable"]')).toBeNull()
    expect(refused.text()).not.toContain('未找到工单')
    expect(recordReport).not.toHaveBeenCalled()
  })

  it('命令与可见 pair 精确一致，并只显示契约实际返回的回执字段', async () => {
    recordReport.mockResolvedValueOnce({
      success: true,
      data: {
        productionReportId: '019f-report-0003',
        reportNo: 'RPT-2026-0003',
      },
    })
    route.query = {
      workOrderId: 'WO-2026-0002',
      operationTaskId: 'OP-3',
    }
    const wrapper = mount(ReportPage, { attachTo: document.body })
    await flushPromises()

    const goodInput = document.body.querySelector<HTMLInputElement>(
      '[data-testid="good-quantity"]',
    )!
    goodInput.value = '4'
    goodInput.dispatchEvent(new Event('input'))
    await flushPromises()
    document.body.querySelector<HTMLButtonElement>('[data-testid="submit-report"]')!.click()
    await flushPromises()

    expect(recordReport).toHaveBeenCalledTimes(1)
    expect(recordReport.mock.calls[0][0]).toMatchObject({
      workOrderId: 'WO-2026-0002',
      operationTaskId: 'OP-3',
      goodQuantity: 4,
    })
    expect(wrapper.text()).toContain('WO-2026-0002 · OP-3')
    expect(wrapper.text()).toContain('报工单号 RPT-2026-0003')
    expect(wrapper.text()).toContain('回执 ID 019f-report-0003')
  })

  it('records a report with the bound fields after entering quantity', async () => {
    const wrapper = mount(ReportPage, { attachTo: document.body })
    await selectWorkOrder(wrapper, 0)
    // 选工序
    const taskRows = wrapper.findAll('[data-row]')
    await taskRows[0].trigger('click')
    await flushPromises()

    // 录入良品数（量录入区 teleport 到 body）
    const goodInput = document.body.querySelector<HTMLInputElement>(
      '[data-testid="good-quantity"]',
    )!
    goodInput.value = '8'
    goodInput.dispatchEvent(new Event('input'))
    await flushPromises()

    const submitBtn = document.body.querySelector<HTMLElement>('[data-testid="submit-report"]')!
    submitBtn.click()
    await flushPromises()

    expect(recordReport).toHaveBeenCalledTimes(1)
    const body = recordReport.mock.calls[0][0]
    expect(body.workOrderId).toBe('WO-2026-0001')
    expect(body.operationTaskId).toBe('OP-1')
    expect(body.goodQuantity).toBe(8)
    expect(body.scrapQuantity).toBe(0)
    expect(body).toHaveProperty('completesOperation')
    // org/env/timestamp 仍由 composable 注入，页面不传
    expect(body).not.toHaveProperty('organizationId')
    expect(body).not.toHaveProperty('environmentId')
    expect(body).not.toHaveProperty('reportedAtUtc')
    // idempotencyKey 现由页面提供（稳定逐操作键）
    expect(body.idempotencyKey).toBeTruthy()

    // 成功后 Result 成功态
    expect(wrapper.find('[data-result][data-status="success"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('报工成功')
    wrapper.unmount()
  })

  it('keeps ordinary reporting available for a completed task without allowing completion again', async () => {
    operationTasksRef.value = [
      {
        operationTaskId: 'OP-DONE',
        workOrderId: 'WO-2026-0001',
        status: 'Completed',
        operationSequence: 10,
      },
    ]
    workOrderDetailRef.value = {
      ...defaultWorkOrders[0],
      operationTasks: operationTasksRef.value,
    }
    const wrapper = mount(ReportPage, { attachTo: document.body })
    await selectWorkOrder(wrapper, 0)
    await wrapper.findAll('[data-row]')[0].trigger('click')
    await flushPromises()

    const complete = document.body.querySelector<HTMLInputElement>(
      '[data-testid="completes-operation"]',
    )!
    expect(complete.disabled).toBe(true)

    const goodInput = document.body.querySelector<HTMLInputElement>(
      '[data-testid="good-quantity"]',
    )!
    goodInput.value = '1'
    goodInput.dispatchEvent(new Event('input'))
    await flushPromises()
    document.body.querySelector<HTMLElement>('[data-testid="submit-report"]')!.click()
    await flushPromises()

    expect(recordReport).toHaveBeenCalledWith(
      expect.objectContaining({
        operationTaskId: 'OP-DONE',
        completesOperation: false,
      }),
    )
    wrapper.unmount()
  })

  it('reuses the SAME idempotencyKey on retry; a new operation mints a different key', async () => {
    const wrapper = mount(ReportPage, { attachTo: document.body })
    await selectWorkOrder(wrapper, 0)
    const taskRows = wrapper.findAll('[data-row]')
    await taskRows[0].trigger('click')
    await flushPromises()

    const goodInput = document.body.querySelector<HTMLInputElement>(
      '[data-testid="good-quantity"]',
    )!
    goodInput.value = '8'
    goodInput.dispatchEvent(new Event('input'))
    await flushPromises()

    // 首次提交失败
    recordReport.mockRejectedValueOnce(new Error('lost response'))
    document.body.querySelector<HTMLElement>('[data-testid="submit-report"]')!.click()
    await flushPromises()
    expect(wrapper.find('[data-result][data-status="error"]').exists()).toBe(true)

    // 不重新发起，直接点重试 → 复用同一 idempotencyKey
    wrapper.get('[data-testid="retry-report"]').trigger('click')
    await flushPromises()

    expect(recordReport).toHaveBeenCalledTimes(2)
    const firstKey = recordReport.mock.calls[0][0].idempotencyKey
    const retryKey = recordReport.mock.calls[1][0].idempotencyKey
    expect(firstKey).toBeTruthy()
    expect(retryKey).toBe(firstKey)

    // 成功后回到起点，发起新报工 → 新键
    wrapper.get('[data-testid="continue-report"]').trigger('click')
    await flushPromises()
    await selectWorkOrder(wrapper, 0)
    const newTaskRows = wrapper.findAll('[data-row]')
    await newTaskRows[0].trigger('click')
    await flushPromises()
    const good2 = document.body.querySelector<HTMLInputElement>('[data-testid="good-quantity"]')!
    good2.value = '3'
    good2.dispatchEvent(new Event('input'))
    await flushPromises()
    document.body.querySelector<HTMLElement>('[data-testid="submit-report"]')!.click()
    await flushPromises()

    expect(recordReport).toHaveBeenCalledTimes(3)
    const newOpKey = recordReport.mock.calls[2][0].idempotencyKey
    expect(newOpKey).toBeTruthy()
    expect(newOpKey).not.toBe(firstKey)
    wrapper.unmount()
  })

  it('does not submit when no quantity was entered (good+scrap must be > 0)', async () => {
    const wrapper = mount(ReportPage, { attachTo: document.body })
    await selectWorkOrder(wrapper, 0)
    const taskRows = wrapper.findAll('[data-row]')
    await taskRows[0].trigger('click')
    await flushPromises()

    const submitBtn = document.body.querySelector<HTMLButtonElement>(
      '[data-testid="submit-report"]',
    )!
    submitBtn.click()
    await flushPromises()
    expect(recordReport).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('工单列表超时：显示可操作错误文案 + 重试调 refresh（GET 安全）', async () => {
    workOrdersErrorRef.value = new RequestTimeoutError()
    const wrapper = mount(ReportPage)
    const banner = wrapper.get('[data-testid="work-orders-error"]')
    expect(banner.text()).toContain('网络超时，请检查连接后重试')
    await wrapper.get('[data-testid="retry-list"]').trigger('click')
    expect(refreshWorkOrders).toHaveBeenCalledTimes(1)
  })

  // P2：加载失败时空态与错误态互斥，不把网络错误误报成"暂无"。
  it('工单加载失败时不显示"暂无可报工的工单"空态', () => {
    workOrdersRef.value = []
    workOrdersErrorRef.value = new RequestTimeoutError()
    const wrapper = mount(ReportPage)
    expect(wrapper.find('[data-testid="work-orders-error"]').exists()).toBe(true)
    expect(wrapper.text()).not.toContain('暂无可报工的工单')
  })
})
