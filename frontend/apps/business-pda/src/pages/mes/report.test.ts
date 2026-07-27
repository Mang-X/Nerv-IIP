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
const successfulReceipt: ReportEnvelope = { success: true, data: {} }
const recordReport = vi.fn(
  async (_input: Record<string, unknown>): Promise<ReportEnvelope> => successfulReceipt,
)
const refreshWorkOrders = vi.fn(async () => {})
const refreshTasks = vi.fn(async () => {})
const cancelPendingTasks = vi.fn()
const workOrdersErrorRef = ref<unknown>(null)
const tasksErrorRef = ref<unknown>(null)
const workOrdersPendingRef = ref(false)
const tasksPendingRef = ref(false)

const workOrderFilters = reactive({
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
    status: 'Running',
    operationSequence: 10,
    workCenterId: 'WC-A',
  },
  {
    operationTaskId: 'OP-2',
    workOrderId: 'WO-2026-0001',
    status: 'Ready',
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

vi.mock('@/composables/useBusinessMes', () => ({
  useMesWorkOrders: () => ({
    filters: workOrderFilters,
    workOrders: computed(() => workOrdersRef.value),
    total: computed(() => workOrdersRef.value.length),
    pending: workOrdersPendingRef,
    error: workOrdersErrorRef,
    refresh: refreshWorkOrders,
  }),
  useMesOperationTasks: () => ({
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
  }),
  useMesProductionReports: () => ({
    filters: reactive({}),
    productionReports: computed(() => []),
    total: computed(() => 0),
    pending: ref(false),
    error: ref(null),
    refresh: vi.fn(),
    recordReport,
  }),
  useMesTelemetryProductionReportCandidates: () => ({
    candidates: computed(() => []),
    total: computed(() => 0),
    pending: ref(false),
    promote: vi.fn(),
    dismiss: vi.fn(),
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
    cancelPendingTasks.mockClear()
    workOrdersErrorRef.value = null
    tasksErrorRef.value = null
    workOrdersPendingRef.value = false
    tasksPendingRef.value = false
    workOrdersRef.value = defaultWorkOrders
    operationTasksRef.value = defaultOperationTasks
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

  it('scanning sets the work-order keyword filter', async () => {
    const wrapper = mount(ReportPage)
    const input = wrapper.get('input[placeholder^="扫"]')
    await input.setValue('WO-2026-0002')
    await input.trigger('keydown.enter')
    expect(workOrderFilters.keyword).toBe('WO-2026-0002')
  })

  it('shows operations after a work order is selected and filters tasks by it', async () => {
    const wrapper = mount(ReportPage)
    await selectWorkOrder(wrapper, 0)
    // 选工序步出现工序序号
    expect(wrapper.text()).toContain('工序 10')
    expect(wrapper.text()).toContain('工序 20')
    // 工序查询按选中工单过滤
    expect(taskFilters.workOrderId).toBe('WO-2026-0001')
  })

  it('切换工单后旧工序行不能打开或提交', async () => {
    const wrapper = mount(ReportPage, { attachTo: document.body })
    await selectWorkOrder(wrapper, 0)
    expect(wrapper.text()).toContain('WO-2026-0001 · 工序 10')

    await wrapper.get('[data-testid="change-work-order"]').trigger('click')
    await selectWorkOrder(wrapper, 1)
    expect(wrapper.text()).toContain('当前工单')
    expect(wrapper.text()).toContain('WO-2026-0002')

    // 模拟 reactive query 已切 key、但旧 key 的任务行在新请求期间仍留在 data 中。
    const staleTaskRow = wrapper
      .findAll('[data-row]')
      .find((row) => row.text().includes('WO-2026-0001 · 工序 10'))
    if (staleTaskRow) {
      await staleTaskRow.trigger('click')
      await flushPromises()
    }

    const goodInput = document.body.querySelector<HTMLInputElement>('[data-testid="good-quantity"]')
    if (goodInput) {
      goodInput.value = '8'
      goodInput.dispatchEvent(new Event('input'))
      await flushPromises()
      document.body.querySelector<HTMLElement>('[data-testid="submit-report"]')?.click()
      await flushPromises()
    }

    expect(document.body.querySelector('[data-testid="good-quantity"]')).toBeNull()
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

    tasksPendingRef.value = true
    route.query = {
      workOrderId: 'WO-2026-0002',
      operationTaskId: 'OP-3',
    }
    await flushPromises()

    expect(cancelPendingTasks).toHaveBeenCalled()
    expect(wrapper.text()).not.toContain('WO-2026-0001 · 工序 10')
    expect(document.body.querySelector('[data-testid="good-quantity"]')).toBeNull()

    // 旧请求迟到时仍处于新 key 的 pending 阶段，不得重新启用任何实体。
    operationTasksRef.value = [defaultOperationTasks[0]]
    await flushPromises()
    expect(document.body.querySelector('[data-testid="good-quantity"]')).toBeNull()

    // 只有新 key 的响应完成后，才绑定 URL 请求的精确 pair。
    operationTasksRef.value = [defaultOperationTasks[2]]
    tasksPendingRef.value = false
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
    expect(cancelPendingTasks.mock.calls.length).toBeGreaterThanOrEqual(2)
  })

  it.each([
    ['缺少工单 ID', { operationTaskId: 'OP-1' }, '报工链接缺少工单 ID'],
    [
      '工单不存在',
      { workOrderId: 'WO-MISSING', operationTaskId: 'OP-404' },
      '未找到工单 WO-MISSING',
    ],
    [
      '工序任务不存在',
      { workOrderId: 'WO-2026-0002', operationTaskId: 'OP-MISSING' },
      '未找到工单 WO-2026-0002 下的工序任务 OP-MISSING',
    ],
    [
      'pair 不匹配',
      { workOrderId: 'WO-2026-0002', operationTaskId: 'OP-1' },
      '工序任务 OP-1 不属于工单 WO-2026-0002',
    ],
  ])('%s 时显示明确安全状态且不写入', async (_name, query, message) => {
    route.query = query
    const wrapper = mount(ReportPage, { attachTo: document.body })
    await flushPromises()

    expect(wrapper.get('[data-testid="report-route-issue"]').text()).toContain(message)
    expect(document.body.querySelector('[data-testid="good-quantity"]')).toBeNull()
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

  it('工序加载失败时不显示"该工单暂无工序"空态', async () => {
    operationTasksRef.value = []
    tasksErrorRef.value = new RequestTimeoutError()
    const wrapper = mount(ReportPage)
    await selectWorkOrder(wrapper, 0) // 进入选工序步
    expect(wrapper.find('[data-testid="tasks-error"]').exists()).toBe(true)
    expect(wrapper.text()).not.toContain('该工单暂无工序')
  })
})
