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
  keyword: undefined as string | undefined,
  status: undefined as string | undefined,
  workOrderId: undefined as string | undefined,
})
const workOrderFilters = reactive({
  keyword: undefined as string | undefined,
  workOrderId: undefined as string | undefined,
})

const receipts = [
  {
    receiptRequestId: 'RCPT-1',
    requestNo: 'FGR-2026-0001',
    workOrderId: 'WO-2026-0001',
    skuId: 'SKU-A',
    quantity: 100,
    receiptStatus: 'Requested',
  },
  {
    receiptRequestId: 'RCPT-2',
    requestNo: 'FGR-2026-0002',
    workOrderId: 'WO-2026-0002',
    skuId: 'SKU-B',
    quantity: 50,
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

vi.mock('@/composables/useBusinessMes', () => ({
  useMesReceipts: () => ({
    filters: receiptFilters,
    receipts: computed(() => receiptRows.value),
    total: computed(() => receiptRows.value.length),
    pending: receiptsPending,
    error: receiptsError,
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
    workOrderFilters.keyword = undefined
    receiptsPending.value = false
    receiptsError.value = null
    receiptRows.value = receipts
  })

  it('renders the receipt list with readable Chinese status and work order numbers', () => {
    const wrapper = mount(ReceiptPage)
    expect(wrapper.text()).toContain('WO-2026-0001')
    expect(wrapper.text()).toContain('WO-2026-0002')
    // 可读中文状态（不外显原始状态码）
    expect(wrapper.text()).toContain('待入库')
    expect(wrapper.text()).toContain('已入库')
    expect(wrapper.text()).not.toContain('Requested')
    expect(wrapper.text()).not.toContain('Received')
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

  it('scanning sets the receipt keyword filter', async () => {
    const wrapper = mount(ReceiptPage)
    const input = wrapper.get('input[placeholder^="扫"]')
    await input.setValue('WO-2026-0002')
    await input.trigger('keydown.enter')
    expect(receiptFilters.keyword).toBe('WO-2026-0002')
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
    const qtyInput = document.body.querySelector<HTMLInputElement>('[data-testid="receipt-quantity"]')!
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
    expect(body).toEqual({
      workOrderId: 'WO-2026-0001',
      skuId: 'SKU-A',
      quantity: 20,
      uomCode: 'PCS',
    })
    // org/env/idempotencyKey/timestamp 由 composable 注入，页面不传
    expect(body).not.toHaveProperty('organizationId')
    expect(body).not.toHaveProperty('environmentId')
    expect(body).not.toHaveProperty('idempotencyKey')
    expect(body).not.toHaveProperty('requestedAtUtc')

    // 成功后 Result 成功态
    expect(wrapper.find('[data-result][data-status="success"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('完工入库已提交')
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
