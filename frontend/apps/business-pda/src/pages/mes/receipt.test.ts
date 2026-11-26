import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, ref } from 'vue'

const push = vi.fn()
vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
}))

// --- composable mock: 2 receipts + 2 work orders + createReceipt spy ---
const confirmation = {
  success: true,
  data: { finishedGoodsReceiptRequestId: 'RCPT-1', requestNo: 'FGR-2026-0001' },
}
const createReceipt = vi.fn(
  async (_input: Record<string, unknown>, _requestedAtUtc: string) => confirmation,
)
const readProducedLots = vi.fn(async () => [{ producedLotNo: 'LOT-A', remainingQuantity: 25 }])
const readReceipt = vi.fn(async () => ({ ...receipts[0], producedLotNo: 'LOT-A' }))
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
    // 用域里真实存在的码（FinishedGoodsReceiptRequest.PostedStatus），不是 `Received`——
    // 入库申请的已入库态叫 Posted，`Received` 是完工入库前记的别的东西，从不作为本字段取值（#3898）。
    receiptStatus: 'Posted',
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

vi.mock('@/composables/mes', () => ({
  useMesReceipts: () => ({
    filters: receiptFilters,
    receiptScopeIdentity: computed(() =>
      JSON.stringify([receiptFilters.organizationId, receiptFilters.environmentId]),
    ),
    receipts: computed(() => receiptRows.value),
    total: computed(() => receiptRows.value.length),
    pending: receiptsPending,
    error: receiptsError,
    hasSuccessfulResponse: receiptsHasSuccessfulResponse,
    hasFailedResponse: receiptsHasFailedResponse,
    refresh: refreshReceipts,
    createReceipt,
    readProducedLots,
    readReceipt,
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
    createReceipt.mockResolvedValue(confirmation)
    readReceipt.mockClear()
    readProducedLots.mockClear()
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
  })

  async function fillReceipt(wrapper: ReturnType<typeof mount>, uom = 'PCS') {
    await wrapper.get('[data-testid="new-receipt"]').trigger('click')
    await flushPromises()
    document.body.querySelector<HTMLElement>('[data-testid="receipt-work-order"]')!.click()
    await flushPromises()
    document.body.querySelector<HTMLInputElement>('[name="receipt-lot"]')!.click()
    document.body.querySelector<HTMLElement>('[data-testid="receipt-quantity"]')!.click()
    await flushPromises()
    for (const digit of ['2', '0', '完成']) {
      Array.from(document.body.querySelectorAll('button'))
        .find((button) => button.textContent?.trim() === digit)!
        .click()
      await flushPromises()
    }
    setUom(uom)
    await flushPromises()
    document.body.querySelector<HTMLElement>('[data-testid="submit-receipt"]')!.click()
    await flushPromises()
  }

  function setUom(value: string) {
    const input = document.body.querySelector<HTMLInputElement>('[data-testid="receipt-uom"]')!
    input.value = value
    input.dispatchEvent(new Event('input'))
  }

  it('retries the original payload and timestamp after a lost response and uses a new key for a new receipt', async () => {
    const wrapper = mount(ReceiptPage, { attachTo: document.body })
    createReceipt.mockRejectedValueOnce(new TypeError('lost response'))
    await fillReceipt(wrapper)
    expect(wrapper.text()).toContain('完工入库结果待确认')

    await wrapper.get('[data-testid="retry-receipt"]').trigger('click')
    await flushPromises()
    expect(createReceipt).toHaveBeenCalledTimes(2)
    expect(createReceipt.mock.calls[1]).toEqual(createReceipt.mock.calls[0])
    const [original] = createReceipt.mock.calls[0]
    expect(original).toMatchObject({
      workOrderId: 'WO-2026-0001',
      skuId: 'SKU-A',
      producedLotNo: 'LOT-A',
      quantity: 20,
      uomCode: 'PCS',
    })
    expect(original).not.toHaveProperty('unitCost')
    expect(wrapper.text()).toContain('完工入库已提交')

    await wrapper.get('[data-testid="continue-receipt"]').trigger('click')
    await fillReceipt(wrapper)
    expect(createReceipt).toHaveBeenCalledTimes(3)
    expect(createReceipt.mock.calls[2][0].idempotencyKey).not.toBe(original.idempotencyKey)
  })

  it('lets the operator correct a unit rejected by MES', async () => {
    const wrapper = mount(ReceiptPage, { attachTo: document.body })
    createReceipt.mockRejectedValueOnce({ message: '完工入库 UoM 与工单不一致' })
    await fillReceipt(wrapper, 'KG')
    expect(wrapper.text()).toContain('完工入库 UoM 与工单不一致')

    await wrapper.get('[data-testid="retry-receipt"]').trigger('click')
    await flushPromises()
    setUom('PCS')
    await flushPromises()
    document.body.querySelector<HTMLElement>('[data-testid="submit-receipt"]')!.click()
    await flushPromises()
    expect(createReceipt).toHaveBeenCalledTimes(2)
    expect(createReceipt.mock.calls[1][0].uomCode).toBe('PCS')
    expect(createReceipt.mock.calls[1][0].idempotencyKey).not.toBe(
      createReceipt.mock.calls[0][0].idempotencyKey,
    )
    expect(wrapper.text()).toContain('完工入库已提交')
  })

  it('retries only the read when the accepted registration is temporarily unavailable', async () => {
    const wrapper = mount(ReceiptPage, { attachTo: document.body })
    readReceipt.mockRejectedValueOnce(new Error('read unavailable'))
    await fillReceipt(wrapper)
    expect(wrapper.text()).toContain('完工入库待回读')

    await wrapper.get('[data-testid="retry-receipt"]').trigger('click')
    await flushPromises()
    expect(createReceipt).toHaveBeenCalledTimes(1)
    expect(readReceipt.mock.calls).toHaveLength(2)
    expect(readReceipt.mock.calls[1]).toEqual(readReceipt.mock.calls[0])
    expect(wrapper.text()).toContain('单号 FGR-2026-0001')
  })
})
