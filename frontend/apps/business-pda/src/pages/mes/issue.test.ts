import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, ref } from 'vue'

const push = vi.fn()
vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
}))

// --- composable mock: 2 issue requests + 2 work orders + create/confirm spies ---
const createIssue = vi.fn(async (_workOrderId: string, _body: Record<string, unknown>) => {})
const confirmLineSideReceipt = vi.fn(async (_requestId: string, _body: Record<string, unknown>) => {})
const refreshRequests = vi.fn(async () => {})
const refreshWorkOrders = vi.fn(async () => {})

const issueFilters = reactive({
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
  },
]

const workOrders = [
  { workOrderId: 'WO-2026-0001', skuId: 'SKU-A', quantity: 100, status: 'Released' },
  { workOrderId: 'WO-2026-0002', skuId: 'SKU-B', quantity: 50, status: 'Released' },
]

vi.mock('@/composables/useBusinessMes', () => ({
  useMesMaterialIssue: () => ({
    filters: issueFilters,
    requests: computed(() => requests),
    total: computed(() => requests.length),
    pending: ref(false),
    error: ref(null),
    refresh: refreshRequests,
    createIssue,
    confirmLineSideReceipt,
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

import IssuePage from './issue.vue'

describe('PDA MES material issue page', () => {
  beforeEach(() => {
    createIssue.mockClear()
    createIssue.mockResolvedValue(undefined)
    confirmLineSideReceipt.mockClear()
    confirmLineSideReceipt.mockResolvedValue(undefined)
    push.mockClear()
    issueFilters.keyword = undefined
    workOrderFilters.keyword = undefined
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
  })

  it('scanning sets the issue keyword filter', async () => {
    const wrapper = mount(IssuePage)
    const input = wrapper.get('input[placeholder^="扫"]')
    await input.setValue('WO-2026-0002')
    await input.trigger('keydown.enter')
    expect(issueFilters.keyword).toBe('WO-2026-0002')
  })

  it('creates an issue with the bound fields and no idempotencyKey', async () => {
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
    const materialInput = document.body.querySelector<HTMLInputElement>('[data-testid="issue-material"]')!
    materialInput.value = 'MAT-X'
    materialInput.dispatchEvent(new Event('input'))
    const qtyInput = document.body.querySelector<HTMLInputElement>('[data-testid="issue-quantity"]')!
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
    expect(body).not.toHaveProperty('idempotencyKey')

    // 成功 Result
    expect(wrapper.find('[data-result][data-status="success"]').exists()).toBe(true)
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

    const submitBtn = document.body.querySelector<HTMLButtonElement>('[data-testid="submit-issue"]')!
    submitBtn.click()
    await flushPromises()
    expect(createIssue).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('confirms line-side receipt with no idempotencyKey and shows success', async () => {
    const wrapper = mount(IssuePage, { attachTo: document.body })

    // 行内线边接收动作（第一条申请）
    const receiveBtn = wrapper.get('[data-testid="receive-REQ-1"]')
    await receiveBtn.trigger('click')
    await flushPromises()

    const qtyInput = document.body.querySelector<HTMLInputElement>('[data-testid="received-quantity"]')!
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
    expect(body).not.toHaveProperty('idempotencyKey')

    expect(wrapper.find('[data-result][data-status="success"]').exists()).toBe(true)
    wrapper.unmount()
  })
})
