import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, ref } from 'vue'

const push = vi.fn()
vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
}))

// --- composable mock: 2 issue requests + 2 work orders + create/confirm spies ---
const createIssue = vi.fn(async (_workOrderId: string, _body: Record<string, unknown>) => {})
const confirmLineSideReceipt = vi.fn(
  async (_requestId: string, _body: Record<string, unknown>) => {},
)
const returnLineSideMaterial = vi.fn(
  async (_requestId: string, _body: Record<string, unknown>) => {},
)
const refreshRequests = vi.fn(async () => {})
const refreshWorkOrders = vi.fn(async () => {})

const issueFilters = reactive({
  organizationId: 'org-001',
  environmentId: 'env-dev',
  keyword: undefined as string | undefined,
  workOrderId: undefined as string | undefined,
  status: undefined as string | undefined,
})
const workOrderFilters = reactive({
  keyword: undefined as string | undefined,
})

const requests = [
  {
    requestId: 'REQ-1',
    workOrderId: 'WO-2026-0001',
    materialId: 'MAT-A',
    requestedQuantity: 100,
    receivedQuantity: 0,
    status: 'Requested',
  },
  {
    requestId: 'REQ-2',
    workOrderId: 'WO-2026-0002',
    materialId: 'MAT-B',
    requestedQuantity: 50,
    receivedQuantity: 50,
    status: 'Received',
    materialLotId: 'LOT-B',
  },
]

const workOrders = [
  { workOrderId: 'WO-2026-0001', skuId: 'SKU-A', quantity: 100, status: 'Released' },
  { workOrderId: 'WO-2026-0002', skuId: 'SKU-B', quantity: 50, status: 'Released' },
]

// 可变的列表加载态，让用例切换 loading/error 与正常态。
const issuePending = ref(false)
const issueError = ref<unknown>(null)
const issueRequests = ref(requests)
const issueLastUpdatedAt = ref('2026-07-28T10:20:30.000Z')
const issueHasSuccessfulResponse = ref(true)
const issueHasFailedResponse = ref(false)
const lineSideInventoryPending = ref(false)
const lineSideInventoryError = ref<unknown>(null)
const lineSideInventoryReady = ref(true)
const lineSideInventoryBalances = ref([
  {
    siteCode: 'SITE-SH',
    locationCode: 'LINE-A01',
    skuCode: 'SKU-DAMPER-001',
    uomCode: 'pcs',
    onHandQuantity: 120,
    reservedQuantity: 20,
    availableQuantity: 100,
    lotCount: 3,
    oldestProductionDate: '2026-08-20',
    ageDays: 6,
    ageCompleteness: 'complete' as const,
  },
  {
    siteCode: 'SITE-SH',
    locationCode: 'LINE-A02',
    skuCode: 'SKU-SEAL-008',
    uomCode: 'pcs',
    onHandQuantity: 45,
    reservedQuantity: 5,
    availableQuantity: 40,
    lotCount: 2,
    oldestProductionDate: '2026-08-22',
    ageDays: 4,
    ageCompleteness: 'partial' as const,
  },
  {
    siteCode: 'SITE-SH',
    locationCode: 'LINE-A03',
    skuCode: 'SKU-OIL-012',
    uomCode: 'l',
    onHandQuantity: 18,
    reservedQuantity: 0,
    availableQuantity: 18,
    lotCount: 1,
    oldestProductionDate: null,
    ageDays: null,
    ageCompleteness: 'unavailable' as const,
  },
])
const initialLineSideInventoryBalances = lineSideInventoryBalances.value
const refreshLineSideInventory = vi.fn(async () => {})

vi.mock('@/composables/useBusinessMes', () => ({
  useMesMaterialIssue: () => ({
    filters: issueFilters,
    requests: computed(() => issueRequests.value),
    total: computed(() => issueRequests.value.length),
    pending: issuePending,
    error: issueError,
    lastUpdatedAt: issueLastUpdatedAt,
    hasSuccessfulResponse: issueHasSuccessfulResponse,
    hasFailedResponse: issueHasFailedResponse,
    refresh: refreshRequests,
    createIssue,
    confirmLineSideReceipt,
    returnLineSideMaterial,
  }),
  useMesWorkOrders: () => ({
    filters: workOrderFilters,
    workOrders: computed(() => workOrders),
    total: computed(() => workOrders.length),
    pending: ref(false),
    error: ref(null),
    refresh: refreshWorkOrders,
  }),
  useMesLineSideInventoryBalances: () => ({
    balances: computed(() => lineSideInventoryBalances.value),
    total: computed(() => lineSideInventoryBalances.value.length),
    pending: lineSideInventoryPending,
    error: lineSideInventoryError,
    ready: lineSideInventoryReady,
    refresh: refreshLineSideInventory,
  }),
}))

import IssuePage from './issue.vue'

describe('PDA MES material issue page', () => {
  beforeEach(() => {
    createIssue.mockClear()
    createIssue.mockResolvedValue(undefined)
    confirmLineSideReceipt.mockClear()
    confirmLineSideReceipt.mockResolvedValue(undefined)
    returnLineSideMaterial.mockClear()
    returnLineSideMaterial.mockResolvedValue(undefined)
    push.mockClear()
    issueFilters.keyword = undefined
    issueFilters.organizationId = 'org-001'
    issueFilters.environmentId = 'env-dev'
    workOrderFilters.keyword = undefined
    issuePending.value = false
    issueError.value = null
    issueRequests.value = requests
    issueHasSuccessfulResponse.value = true
    issueHasFailedResponse.value = false
    refreshRequests.mockClear()
    lineSideInventoryPending.value = false
    lineSideInventoryError.value = null
    lineSideInventoryReady.value = true
    lineSideInventoryBalances.value = initialLineSideInventoryBalances
    refreshLineSideInventory.mockClear()
  })

  it('lists material issue requests with readable info', () => {
    const wrapper = mount(IssuePage)
    // ScanBar 可见
    expect(wrapper.find('input[placeholder^="扫"]').exists()).toBe(true)
    // 工单号/物料可读呈现
    expect(wrapper.text()).toContain('WO-2026-0001')
    expect(wrapper.text()).toContain('MAT-A')
    // 不暴露原始 requestId 作为标签
    expect(wrapper.text()).not.toContain('REQ-1')
    expect(wrapper.text()).toContain('范围：当前登录组织 / 当前业务环境')
    expect(wrapper.text()).toContain('来源：生产领料申请服务（组织/环境范围）')
    expect(wrapper.text()).toContain('已加载 2 / 共 2')
    expect(wrapper.text()).toContain('最近成功响应')
  })

  it('shows touch-friendly line-side balances without turning unknown age into zero days', async () => {
    const wrapper = mount(IssuePage)
    await flushPromises()

    expect(wrapper.text()).toContain('线边库存')
    expect(wrapper.text()).toContain('SKU-DAMPER-001')
    expect(wrapper.text()).toContain('LINE-A01')
    expect(wrapper.text()).toContain('可用 100 pcs')
    expect(wrapper.text()).toContain('6 天 · 账龄完整')
    expect(wrapper.text()).toContain('4 天（部分批次缺少生产日期） · 账龄部分可知')
    expect(wrapper.text()).toContain('账龄未知（批次缺少生产日期）')
    expect(wrapper.text()).not.toContain('0 天')
  })

  it('distinguishes line-side loading, error, empty, and refresh behavior', async () => {
    lineSideInventoryBalances.value = []
    lineSideInventoryPending.value = true
    lineSideInventoryReady.value = false
    const wrapper = mount(IssuePage)
    await flushPromises()
    expect(wrapper.text()).toContain('正在加载线边库存')

    lineSideInventoryPending.value = false
    lineSideInventoryError.value = new Error('网络暂不可用')
    await flushPromises()
    expect(wrapper.get('[data-testid="line-side-inventory-error"]').text()).toContain(
      '网络暂不可用',
    )
    expect(wrapper.text()).not.toContain('暂无线边库存余额')

    lineSideInventoryError.value = null
    lineSideInventoryReady.value = true
    await flushPromises()
    expect(wrapper.text()).toContain('当前组织/环境范围暂无线边库存余额')

    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('刷新库存'))!
      .trigger('click')
    expect(refreshLineSideInventory).toHaveBeenCalledTimes(1)
  })

  it('shows the list error (not the empty state) when the requests query fails', async () => {
    issueRequests.value = []
    issueError.value = new Error('加载失败：网络异常')
    const wrapper = mount(IssuePage)
    await flushPromises()

    const alert = wrapper.find('[role="alert"]')
    expect(alert.exists()).toBe(true)
    expect(alert.text()).toContain('加载失败：网络异常')
    // 错误态不应退化为「暂无领料申请」空态
    expect(wrapper.text()).not.toContain('暂无领料申请')
    expect(wrapper.find('[data-testid="list-empty-explanation"]').exists()).toBe(false)
  })

  it('explains a successful empty organization-scope response without claiming personal ownership', async () => {
    issueRequests.value = []
    const wrapper = mount(IssuePage)
    await flushPromises()

    expect(wrapper.text()).toContain('当前组织/环境范围暂无领料申请')
    expect(wrapper.text()).toContain('不代表当前人员没有领料任务')
  })

  it('shows a retryable failure for success:false instead of a business empty state', async () => {
    issueRequests.value = []
    issueHasSuccessfulResponse.value = false
    issueHasFailedResponse.value = true
    const wrapper = mount(IssuePage)
    await flushPromises()

    expect(wrapper.find('[role="alert"]').text()).toContain('领料申请服务未返回成功结果')
    expect(wrapper.text()).not.toContain('当前组织/环境范围暂无领料申请')
    await wrapper.get('[data-testid="retry-list"]').trigger('click')
    expect(refreshRequests).toHaveBeenCalledTimes(1)
  })

  it('does not render cached issue rows or totals after the organization scope is lost', async () => {
    const wrapper = mount(IssuePage)
    expect(wrapper.text()).toContain('WO-2026-0001')

    issueFilters.organizationId = ''
    issueFilters.environmentId = ''
    await flushPromises()

    expect(wrapper.text()).toContain('缺少组织或环境范围，未发起查询')
    expect(wrapper.text()).toContain('已加载 0 / 共 0')
    expect(wrapper.text()).not.toContain('WO-2026-0001')
    expect(wrapper.text()).not.toContain('MAT-A')
  })

  it('scanning sets the issue keyword filter', async () => {
    const wrapper = mount(IssuePage)
    const input = wrapper.get('input[placeholder^="扫"]')
    await input.setValue('WO-2026-0002')
    await input.trigger('keydown.enter')
    expect(issueFilters.keyword).toBe('WO-2026-0002')
  })

  it('creates an issue with the bound fields and a page-supplied idempotencyKey', async () => {
    const wrapper = mount(IssuePage, { attachTo: document.body })

    // 打开新建领料表单
    const newBtn = wrapper.get('[data-testid="new-issue"]')
    await newBtn.trigger('click')
    await flushPromises()

    // 选工单（表单内列出工单）
    const woRows = document.body.querySelectorAll<HTMLElement>('[data-testid="issue-work-order"]')
    woRows[0].click()
    await flushPromises()

    // 录入物料与数量
    const materialInput = document.body.querySelector<HTMLInputElement>(
      '[data-testid="issue-material"]',
    )!
    materialInput.value = 'MAT-X'
    materialInput.dispatchEvent(new Event('input'))
    const qtyInput = document.body.querySelector<HTMLInputElement>(
      '[data-testid="issue-quantity"]',
    )!
    qtyInput.value = '12'
    qtyInput.dispatchEvent(new Event('input'))
    await flushPromises()

    const submitBtn = document.body.querySelector<HTMLElement>('[data-testid="submit-issue"]')!
    submitBtn.click()
    await flushPromises()

    expect(createIssue).toHaveBeenCalledTimes(1)
    const [workOrderId, body] = createIssue.mock.calls[0]
    expect(workOrderId).toBe('WO-2026-0001')
    expect(body.materialId).toBe('MAT-X')
    expect(body.quantity).toBe(12)
    // idempotencyKey 现由页面提供（稳定逐操作键）；org/env 仍由 composable 注入
    expect(body.idempotencyKey).toBeTruthy()
    expect(body).not.toHaveProperty('organizationId')
    expect(body).not.toHaveProperty('environmentId')

    // 成功 Result
    expect(wrapper.find('[data-result][data-status="success"]').exists()).toBe(true)
    wrapper.unmount()
  })

  it('reuses the SAME idempotencyKey on create retry; a new create mints a different key', async () => {
    const wrapper = mount(IssuePage, { attachTo: document.body })

    async function fillCreate(material: string) {
      await wrapper.get('[data-testid="new-issue"]').trigger('click')
      await flushPromises()
      document.body.querySelectorAll<HTMLElement>('[data-testid="issue-work-order"]')[0].click()
      await flushPromises()
      const materialInput = document.body.querySelector<HTMLInputElement>(
        '[data-testid="issue-material"]',
      )!
      materialInput.value = material
      materialInput.dispatchEvent(new Event('input'))
      await flushPromises()
      document.body.querySelector<HTMLElement>('[data-testid="submit-issue"]')!.click()
      await flushPromises()
    }

    // 首次提交失败
    createIssue.mockRejectedValueOnce(new Error('lost response'))
    await fillCreate('MAT-X')
    expect(wrapper.find('[data-result][data-status="error"]').exists()).toBe(true)

    // 不重新发起，直接点重试 → 复用同一 idempotencyKey
    await wrapper.get('[data-testid="retry-issue"]').trigger('click')
    await flushPromises()

    expect(createIssue).toHaveBeenCalledTimes(2)
    const firstKey = createIssue.mock.calls[0][1].idempotencyKey
    const retryKey = createIssue.mock.calls[1][1].idempotencyKey
    expect(firstKey).toBeTruthy()
    expect(retryKey).toBe(firstKey)

    // 成功后回到起点，发起新一轮新建 → 新键
    await wrapper
      .findAll('button')
      .find((b) => b.text() === '继续')!
      .trigger('click')
    await flushPromises()
    await fillCreate('MAT-Y')

    expect(createIssue).toHaveBeenCalledTimes(3)
    const newKey = createIssue.mock.calls[2][1].idempotencyKey
    expect(newKey).toBeTruthy()
    expect(newKey).not.toBe(firstKey)
    wrapper.unmount()
  })

  it('does not create when required fields are missing', async () => {
    const wrapper = mount(IssuePage, { attachTo: document.body })
    const newBtn = wrapper.get('[data-testid="new-issue"]')
    await newBtn.trigger('click')
    await flushPromises()

    // 选工单但不填物料 → 提交不触发
    const woRows = document.body.querySelectorAll<HTMLElement>('[data-testid="issue-work-order"]')
    woRows[0].click()
    await flushPromises()

    const submitBtn = document.body.querySelector<HTMLButtonElement>(
      '[data-testid="submit-issue"]',
    )!
    submitBtn.click()
    await flushPromises()
    expect(createIssue).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('confirms line-side receipt with a page-supplied idempotencyKey and shows success', async () => {
    const wrapper = mount(IssuePage, { attachTo: document.body })

    // 行内线边接收动作（第一条申请）
    const receiveBtn = wrapper.get('[data-testid="receive-REQ-1"]')
    await receiveBtn.trigger('click')
    await flushPromises()

    const qtyInput = document.body.querySelector<HTMLInputElement>(
      '[data-testid="received-quantity"]',
    )!
    qtyInput.value = '100'
    qtyInput.dispatchEvent(new Event('input'))
    await flushPromises()

    const confirmBtn = document.body.querySelector<HTMLElement>('[data-testid="submit-receive"]')!
    confirmBtn.click()
    await flushPromises()

    expect(confirmLineSideReceipt).toHaveBeenCalledTimes(1)
    const [requestId, body] = confirmLineSideReceipt.mock.calls[0]
    expect(requestId).toBe('REQ-1')
    expect(body.receivedQuantity).toBe(100)
    // idempotencyKey 现由页面提供（稳定逐操作键）
    expect(body.idempotencyKey).toBeTruthy()

    expect(wrapper.find('[data-result][data-status="success"]').exists()).toBe(true)
    wrapper.unmount()
  })

  it('reuses the SAME idempotencyKey on receive retry; a new receive mints a different key', async () => {
    const wrapper = mount(IssuePage, { attachTo: document.body })

    async function fillReceive(testid: string) {
      await wrapper.get(`[data-testid="${testid}"]`).trigger('click')
      await flushPromises()
      const qtyInput = document.body.querySelector<HTMLInputElement>(
        '[data-testid="received-quantity"]',
      )!
      qtyInput.value = '100'
      qtyInput.dispatchEvent(new Event('input'))
      await flushPromises()
      document.body.querySelector<HTMLElement>('[data-testid="submit-receive"]')!.click()
      await flushPromises()
    }

    // 首次确认失败
    confirmLineSideReceipt.mockRejectedValueOnce(new Error('lost response'))
    await fillReceive('receive-REQ-1')
    expect(wrapper.find('[data-result][data-status="error"]').exists()).toBe(true)

    // 不重新发起，直接点重试 → 复用同一 idempotencyKey
    await wrapper.get('[data-testid="retry-issue"]').trigger('click')
    await flushPromises()

    expect(confirmLineSideReceipt).toHaveBeenCalledTimes(2)
    const firstKey = confirmLineSideReceipt.mock.calls[0][1].idempotencyKey
    const retryKey = confirmLineSideReceipt.mock.calls[1][1].idempotencyKey
    expect(firstKey).toBeTruthy()
    expect(retryKey).toBe(firstKey)

    // 成功后回到起点，发起对另一条申请的新一轮接收 → 新键
    await wrapper
      .findAll('button')
      .find((b) => b.text() === '继续')!
      .trigger('click')
    await flushPromises()
    await fillReceive('receive-REQ-1')

    expect(confirmLineSideReceipt).toHaveBeenCalledTimes(3)
    const newKey = confirmLineSideReceipt.mock.calls[2][1].idempotencyKey
    expect(newKey).toBeTruthy()
    expect(newKey).not.toBe(firstKey)
    wrapper.unmount()
  })

  it('returns received line-side material from the same issue list', async () => {
    const wrapper = mount(IssuePage, { attachTo: document.body })

    await wrapper.get('[data-testid="return-REQ-2"]').trigger('click')
    await flushPromises()
    const quantity = document.body.querySelector<HTMLInputElement>(
      '[data-testid="returned-quantity"]',
    )!
    quantity.value = '10'
    quantity.dispatchEvent(new Event('input'))
    await flushPromises()
    document.body.querySelector<HTMLElement>('[data-testid="submit-return"]')!.click()
    await flushPromises()

    expect(returnLineSideMaterial).toHaveBeenCalledWith(
      'REQ-2',
      expect.objectContaining({ returnedQuantity: 10 }),
      { workOrderId: 'WO-2026-0002' },
    )
    expect(wrapper.find('[data-result][data-status="success"]').exists()).toBe(true)
    wrapper.unmount()
  })

  it('blocks an over-limit return with visible feedback before submitting', async () => {
    const wrapper = mount(IssuePage, { attachTo: document.body })

    await wrapper.get('[data-testid="return-REQ-2"]').trigger('click')
    await flushPromises()
    const quantity = document.body.querySelector<HTMLInputElement>(
      '[data-testid="returned-quantity"]',
    )!
    quantity.value = '51'
    quantity.dispatchEvent(new Event('input'))
    await flushPromises()

    expect(document.body.textContent).toContain('退料数量不能超过当前可退数量 50')
    expect(
      document.body
        .querySelector<HTMLElement>('[data-testid="submit-return"]')!
        .hasAttribute('disabled'),
    ).toBe(true)
    expect(returnLineSideMaterial).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('shows a structured MES rejection and reuses the return idempotency key on retry', async () => {
    returnLineSideMaterial.mockRejectedValueOnce({ detail: '当前可退数量不足。' })
    const wrapper = mount(IssuePage, { attachTo: document.body })

    await wrapper.get('[data-testid="return-REQ-2"]').trigger('click')
    await flushPromises()
    document.body.querySelector<HTMLElement>('[data-testid="submit-return"]')!.click()
    await flushPromises()

    expect(wrapper.find('[data-result][data-status="error"]').text()).toContain('当前可退数量不足')
    const firstKey = returnLineSideMaterial.mock.calls[0][1].idempotencyKey
    await wrapper.get('[data-testid="retry-issue"]').trigger('click')
    await flushPromises()
    expect(returnLineSideMaterial.mock.calls[1][1].idempotencyKey).toBe(firstKey)
    wrapper.unmount()
  })
})
