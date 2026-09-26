import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, ref } from 'vue'

const push = vi.fn()
vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
}))

// --- composable mock: 2 receipts + 2 work orders + createReceipt spy ---
const createReceipt = vi.fn(async (_input: Record<string, unknown>) => {})
const refreshReceipts = vi.fn(async () => {})
const refreshWorkOrders = vi.fn(async () => {})

const receiptFilters = reactive({
  organizationId: 'org-001',
  environmentId: 'env-dev',
  keyword: undefined as string | undefined,
  status: undefined as string | undefined,
  workOrderId: undefined as string | undefined,
})
const workOrderFilters = reactive({
  organizationId: 'org-001',
  environmentId: 'env-dev',
  keyword: undefined as string | undefined,
  workOrderId: undefined as string | undefined,
})

// costCapitalization 是可选字段：网关只在调用者有 ERP 财务读权限时附带（#3767）。
interface MockReceipt {
  receiptRequestId: string
  requestNo: string
  workOrderId: string
  skuId: string
  quantity: number
  unitCost?: number
  receiptStatus: string
  costCapitalization?: {
    workOrderCompleted?: boolean
    receivedReportCount?: number
    expectedReportCount?: number
    receivedMaterialMovementCount?: number
    expectedMaterialMovementCount?: number
    capitalizationPublished?: boolean
  }
}

const receipts: MockReceipt[] = [
  {
    receiptRequestId: 'RCPT-1',
    requestNo: 'FGR-2026-0001',
    workOrderId: 'WO-2026-0001',
    skuId: 'SKU-A',
    quantity: 100,
    unitCost: 12.34,
    receiptStatus: 'Requested',
  },
  {
    receiptRequestId: 'RCPT-2',
    requestNo: 'FGR-2026-0002',
    workOrderId: 'WO-2026-0002',
    skuId: 'SKU-B',
    quantity: 50,
    unitCost: 23.45,
    receiptStatus: 'Received',
  },
]

const workOrders = [
  { workOrderId: 'WO-2026-0001', skuId: 'SKU-A', quantity: 100, status: 'Released' },
  { workOrderId: 'WO-2026-0002', skuId: 'SKU-B', quantity: 50, status: 'Released' },
]

// 可变的列表加载态，让用例切换 loading/error 与正常态。
const receiptsPending = ref(false)
const receiptsError = ref<unknown>(null)
const receiptRows = ref(receipts)
const receiptsHasSuccessfulResponse = ref(true)
const receiptsHasFailedResponse = ref(false)

vi.mock('@/composables/useBusinessMes', () => ({
  useMesReceipts: () => ({
    filters: receiptFilters,
    receipts: computed(() => receiptRows.value),
    total: computed(() => receiptRows.value.length),
    pending: receiptsPending,
    error: receiptsError,
    hasSuccessfulResponse: receiptsHasSuccessfulResponse,
    hasFailedResponse: receiptsHasFailedResponse,
    refresh: refreshReceipts,
    createReceipt,
  }),
  useMesWorkOrders: () => ({
    filters: workOrderFilters,
    workOrders: computed(() => workOrders),
    total: computed(() => workOrders.length),
    pending: ref(false),
    error: ref(null),
    refresh: refreshWorkOrders,
  }),
}))

import ReceiptPage from './receipt.vue'

describe('PDA MES finished-goods receipt page', () => {
  beforeEach(() => {
    createReceipt.mockClear()
    createReceipt.mockResolvedValue(undefined)
    push.mockClear()
    receiptFilters.keyword = undefined
    receiptFilters.workOrderId = undefined
    receiptFilters.organizationId = 'org-001'
    receiptFilters.environmentId = 'env-dev'
    workOrderFilters.keyword = undefined
    workOrderFilters.workOrderId = undefined
    receiptsPending.value = false
    receiptsError.value = null
    receiptRows.value = receipts
    receiptsHasSuccessfulResponse.value = true
    receiptsHasFailedResponse.value = false
    refreshReceipts.mockClear()
  })

  it('renders the receipt list with readable Chinese status and work order numbers', () => {
    const wrapper = mount(ReceiptPage)
    expect(wrapper.text()).toContain('WO-2026-0001')
    expect(wrapper.text()).toContain('WO-2026-0002')
    expect(wrapper.text()).toContain('成本 12.34')
    // 可读中文状态（不外显原始状态码）
    expect(wrapper.text()).toContain('待入库')
    expect(wrapper.text()).toContain('已入库')
    expect(wrapper.text()).not.toContain('Requested')
    expect(wrapper.text()).not.toContain('Received')
  })

  it('shows which stage a pending receipt is stuck on, same wording as business console (#3767)', async () => {
    // 同一份 costCapitalization 输入，business-console 与 PDA 走同一份 receiptPendingReason
    // （@nerv-iip/business-core），判断逻辑只有一份，不是两份复制。
    const pendingReceipt: MockReceipt = {
      receiptRequestId: 'RCPT-3',
      requestNo: 'FGR-2026-0003',
      workOrderId: 'WO-2026-0003',
      skuId: 'SKU-C',
      quantity: 10,
      receiptStatus: 'Requested',
      costCapitalization: {
        workOrderCompleted: true,
        receivedReportCount: 0,
        expectedReportCount: 8,
        receivedMaterialMovementCount: 3,
        expectedMaterialMovementCount: 3,
      },
    }
    receiptRows.value = [pendingReceipt]
    const wrapper = mount(ReceiptPage)
    await flushPromises()

    const reason = wrapper.find('[data-testid="receipt-pending-reason"]')
    expect(reason.exists()).toBe(true)
    expect(reason.text()).toContain('报工成本 0/8')
    expect(reason.text()).toContain('物料过账 3/3')
  })

  it('shows the list error (not the empty state) when the receipts query fails', async () => {
    receiptRows.value = []
    receiptsError.value = new Error('加载失败：网络异常')
    const wrapper = mount(ReceiptPage)
    await flushPromises()

    const alert = wrapper.find('[role="alert"]')
    expect(alert.exists()).toBe(true)
    expect(alert.text()).toContain('加载失败：网络异常')
    // 错误态不应退化为「暂无完工入库申请」空态
    expect(wrapper.text()).not.toContain('暂无完工入库申请')
  })

  it('shows the business empty state for a successful empty response', async () => {
    receiptRows.value = []
    const wrapper = mount(ReceiptPage)
    await flushPromises()

    expect(wrapper.text()).toContain('暂无完工入库申请')
  })

  it('shows a retryable failure for success:false instead of a business empty state', async () => {
    receiptRows.value = []
    receiptsHasSuccessfulResponse.value = false
    receiptsHasFailedResponse.value = true
    const wrapper = mount(ReceiptPage)
    await flushPromises()

    expect(wrapper.find('[role="alert"]').text()).toContain('完工入库申请加载失败')
    expect(wrapper.text()).not.toContain('暂无完工入库申请')
    await wrapper.get('[data-testid="retry-list"]').trigger('click')
    expect(refreshReceipts).toHaveBeenCalledTimes(1)
  })

  it('does not render cached receipt rows or totals after the organization scope is lost', async () => {
    const wrapper = mount(ReceiptPage)
    expect(wrapper.text()).toContain('FGR-2026-0001')

    receiptFilters.organizationId = ''
    receiptFilters.environmentId = ''
    await flushPromises()

    expect(wrapper.text()).not.toContain('FGR-2026-0001')
    expect(wrapper.text()).not.toContain('SKU-A')
  })

  it('uses the resolved work-order strong id as an exact receipt filter', async () => {
    const wrapper = mount(ReceiptPage)
    await wrapper.getComponent({ name: 'MesScanPrevalidation' }).vm.$emit('accepted', {
      kind: 'work-order',
      candidate: {},
      workOrderId: 'WO-2026-0002',
    })
    expect(receiptFilters.workOrderId).toBe('WO-2026-0002')
    expect(receiptFilters.keyword).toBeUndefined()
  })

  it('starts the new-receipt flow on the select-work-order step', async () => {
    const wrapper = mount(ReceiptPage, { attachTo: document.body })
    const newBtn = wrapper.get('[data-testid="new-receipt"]')
    await newBtn.trigger('click')
    await flushPromises()
    // 选工单步：列出工单，尚未要求录 SKU
    expect(document.body.querySelector('[data-testid="receipt-work-order"]')).not.toBeNull()
    expect(document.body.querySelector('[data-testid="receipt-sku"]')).toBeNull()
    wrapper.unmount()
  })

  it('creates a receipt with the bound fields after picking a work order and entering sku/quantity/uom', async () => {
    const wrapper = mount(ReceiptPage, { attachTo: document.body })
    await wrapper.get('[data-testid="new-receipt"]').trigger('click')
    await flushPromises()

    // 选工单
    const woRow = document.body.querySelector<HTMLElement>('[data-testid="receipt-work-order"]')!
    woRow.click()
    await flushPromises()

    // 录 SKU / 数量 / 单位
    const skuInput = document.body.querySelector<HTMLInputElement>('[data-testid="receipt-sku"]')!
    skuInput.value = 'SKU-A'
    skuInput.dispatchEvent(new Event('input'))
    const qtyInput = document.body.querySelector<HTMLInputElement>(
      '[data-testid="receipt-quantity"]',
    )!
    qtyInput.value = '20'
    qtyInput.dispatchEvent(new Event('input'))
    const uomInput = document.body.querySelector<HTMLInputElement>('[data-testid="receipt-uom"]')!
    uomInput.value = 'PCS'
    uomInput.dispatchEvent(new Event('input'))
    await flushPromises()

    document.body.querySelector<HTMLElement>('[data-testid="submit-receipt"]')!.click()
    await flushPromises()

    expect(createReceipt).toHaveBeenCalledTimes(1)
    const body = createReceipt.mock.calls[0][0]
    expect(document.body.querySelector('[data-testid="receipt-unit-cost"]')).toBeNull()
    expect(body).toMatchObject({
      workOrderId: 'WO-2026-0001',
      skuId: 'SKU-A',
      quantity: 20,
      uomCode: 'PCS',
    })
    expect(body).not.toHaveProperty('unitCost')
    // idempotencyKey 现由页面提供（稳定逐操作键）；org/env/timestamp 仍由 composable 注入
    expect(body.idempotencyKey).toBeTruthy()
    expect(body).not.toHaveProperty('organizationId')
    expect(body).not.toHaveProperty('environmentId')
    expect(body).not.toHaveProperty('requestedAtUtc')

    // 成功后 Result 成功态
    expect(wrapper.find('[data-result][data-status="success"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('完工入库已提交')
    wrapper.unmount()
  })

  it('reuses the SAME idempotencyKey on create retry; a new receipt mints a different key', async () => {
    const wrapper = mount(ReceiptPage, { attachTo: document.body })

    async function fillCreate(sku: string) {
      await wrapper.get('[data-testid="new-receipt"]').trigger('click')
      await flushPromises()
      document.body.querySelector<HTMLElement>('[data-testid="receipt-work-order"]')!.click()
      await flushPromises()
      const skuInput = document.body.querySelector<HTMLInputElement>('[data-testid="receipt-sku"]')!
      skuInput.value = sku
      skuInput.dispatchEvent(new Event('input'))
      const qtyInput = document.body.querySelector<HTMLInputElement>(
        '[data-testid="receipt-quantity"]',
      )!
      qtyInput.value = '20'
      qtyInput.dispatchEvent(new Event('input'))
      const uomInput = document.body.querySelector<HTMLInputElement>('[data-testid="receipt-uom"]')!
      uomInput.value = 'PCS'
      uomInput.dispatchEvent(new Event('input'))
      await flushPromises()
      document.body.querySelector<HTMLElement>('[data-testid="submit-receipt"]')!.click()
      await flushPromises()
    }

    // 首次提交失败
    createReceipt.mockRejectedValueOnce(new Error('lost response'))
    await fillCreate('SKU-A')
    expect(wrapper.find('[data-result][data-status="error"]').exists()).toBe(true)

    // 不重新发起，直接点重试 → 复用同一 idempotencyKey
    await wrapper.get('[data-testid="retry-receipt"]').trigger('click')
    await flushPromises()

    expect(createReceipt).toHaveBeenCalledTimes(2)
    const firstKey = createReceipt.mock.calls[0][0].idempotencyKey
    const retryKey = createReceipt.mock.calls[1][0].idempotencyKey
    expect(firstKey).toBeTruthy()
    expect(retryKey).toBe(firstKey)

    // 成功后继续，发起新一轮完工入库 → 新键
    await wrapper.get('[data-testid="continue-receipt"]').trigger('click')
    await flushPromises()
    await fillCreate('SKU-B')

    expect(createReceipt).toHaveBeenCalledTimes(3)
    const newKey = createReceipt.mock.calls[2][0].idempotencyKey
    expect(newKey).toBeTruthy()
    expect(newKey).not.toBe(firstKey)
    wrapper.unmount()
  })

  it('does not submit when sku or quantity is missing', async () => {
    const wrapper = mount(ReceiptPage, { attachTo: document.body })
    await wrapper.get('[data-testid="new-receipt"]').trigger('click')
    await flushPromises()
    document.body.querySelector<HTMLElement>('[data-testid="receipt-work-order"]')!.click()
    await flushPromises()

    // 仅填单位，缺 SKU 与数量
    const uomInput = document.body.querySelector<HTMLInputElement>('[data-testid="receipt-uom"]')!
    uomInput.value = 'PCS'
    uomInput.dispatchEvent(new Event('input'))
    await flushPromises()

    document.body.querySelector<HTMLElement>('[data-testid="submit-receipt"]')!.click()
    await flushPromises()
    expect(createReceipt).not.toHaveBeenCalled()
    wrapper.unmount()
  })
})
