import { RequestTimeoutError } from '@/api/request-timeout'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, ref } from 'vue'

const push = vi.fn()
vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
}))

// --- composable mock: 2 work orders + 2 operation tasks + recordReport spy ---
const recordReport = vi.fn(async (_input: Record<string, unknown>) => {})
const refreshWorkOrders = vi.fn(async () => {})
const refreshTasks = vi.fn(async () => {})
const workOrdersErrorRef = ref<unknown>(null)

const workOrderFilters = reactive({
  keyword: undefined as string | undefined,
  workOrderId: undefined as string | undefined,
})
const taskFilters = reactive({
  keyword: undefined as string | undefined,
  workOrderId: undefined as string | undefined,
})

const workOrders = [
  { workOrderId: 'WO-2026-0001', skuId: 'SKU-A', quantity: 100, status: 'Released' },
  { workOrderId: 'WO-2026-0002', skuId: 'SKU-B', quantity: 50, status: 'Released' },
]

const operationTasks = [
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
]

vi.mock('@/composables/useBusinessMes', () => ({
  useMesWorkOrders: () => ({
    filters: workOrderFilters,
    workOrders: computed(() => workOrders),
    total: computed(() => workOrders.length),
    pending: ref(false),
    error: workOrdersErrorRef,
    refresh: refreshWorkOrders,
  }),
  useMesOperationTasks: () => ({
    filters: taskFilters,
    operationTasks: computed(() => operationTasks),
    total: computed(() => operationTasks.length),
    pending: ref(false),
    error: ref(null),
    refresh: refreshTasks,
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
}))

import ReportPage from './report.vue'

async function selectWorkOrder(wrapper: ReturnType<typeof mount>, index = 0) {
  const rows = wrapper.findAll('[data-row]')
  await rows[index].trigger('click')
  await flushPromises()
}

describe('PDA MES production reporting page', () => {
  beforeEach(() => {
    recordReport.mockClear()
    recordReport.mockResolvedValue(undefined)
    push.mockClear()
    refreshWorkOrders.mockClear()
    workOrdersErrorRef.value = null
    workOrderFilters.keyword = undefined
    taskFilters.workOrderId = undefined
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
})
