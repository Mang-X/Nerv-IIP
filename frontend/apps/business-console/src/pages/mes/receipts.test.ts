import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { ref, shallowRef } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { useAuthStore } from '@/stores/auth'
import ReceiptsPage from './receipts.vue'

const routeState = vi.hoisted(() => ({ query: {} as Record<string, string> }))
const routerState = vi.hoisted(() => ({ replace: vi.fn(), push: vi.fn() }))

vi.mock('vue-router', () => ({
  useRoute: () => routeState,
  useRouter: () => routerState,
}))

vi.mock('@/composables/mes/useMesDisplayNames', () => ({
  useMesDisplayNames: () => ({ resolveSku: (v?: string | null) => v ?? '无' }),
}))

const notifySpies = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn() }))
vi.mock('@/utils/notify', () => ({
  notifySuccess: notifySpies.success,
  notifyError: notifySpies.error,
}))

const receiptState = vi.hoisted(() => ({
  createReceiptRequest: vi.fn(async (_body: unknown) => undefined),
  createReceiptRequestError: { value: undefined as unknown },
  refreshReceiptRequests: vi.fn(async () => undefined),
  retryInventoryPosting: vi.fn(async () => undefined),
  rows: [] as Array<Record<string, unknown>>,
  producedLots: [] as Array<{ producedLotNo: string; reportNo?: string; goodQuantity: number }>,
  producedLotsError: undefined as unknown,
  retryingRequestNo: undefined as unknown as { value: string | null },
  keyCounter: 0,
}))

vi.mock('@/composables/useBusinessMes', () => {
  return {
    // 唯一键生成器：每次调用递增计数，用于验证「同一工单连续两次登记 → 两个不同幂等键」。
    makeIdempotencyKey: (prefix: string) => `${prefix}-key-${++receiptState.keyCounter}`,
    // 工单产出批次来源：页面据此选定 producedLotNo（后端强制引用真实产出批次）。
    useMesWorkOrderProducedLots: () => ({
      producedLots: ref(receiptState.producedLots),
      producedLotsError: ref(receiptState.producedLotsError),
      producedLotsPending: ref(false),
      refreshProducedLots: vi.fn(async () => undefined),
    }),
    useMesFinishedGoodsReceipts: () => ({
      createReceiptRequest: receiptState.createReceiptRequest,
      createReceiptRequestError: receiptState.createReceiptRequestError,
      createReceiptRequestPending: ref(false),
      filters: { organizationId: 'org', environmentId: 'dev', status: undefined },
      receiptRequests: ref(receiptState.rows),
      receiptRequestsError: ref(undefined),
      receiptRequestsPending: ref(false),
      receiptRequestsTotal: ref(receiptState.rows.length),
      refreshReceiptRequests: receiptState.refreshReceiptRequests,
      retryInventoryPosting: receiptState.retryInventoryPosting,
      retryInventoryPostingError: ref(undefined),
      retryingRequestNo: receiptState.retryingRequestNo,
    }),
  }
})

// NvDataTable 桩:逐行渲染入库状态与操作两个插槽，便于断言失败徽章/原因/重试按钮。
const tableStub = {
  props: ['rows'],
  template: `
    <section data-testid="table">
      <div v-for="row in rows" :key="row.receiptRequestId" data-testid="row">
        <slot name="cell-receiptStatus" :row="row" />
        <slot name="cell-actions" :row="row" />
      </div>
    </section>
  `,
}

const stubs = {
  BusinessLayout: { template: '<main><slot /></main>' },
  WorkOrderQuickView: true,
  NvPageHeader: { template: '<header><slot name="actions" /></header>' },
  NvToolbar: { template: '<div><slot name="filters" /></div>' },
  NvDataTable: tableStub,
  NvStatusBadge: {
    props: ['tone', 'label', 'value'],
    template: '<span data-testid="badge" :data-tone="tone">{{ label ?? value }}</span>',
  },
  NvButton: {
    props: ['disabled'],
    template: '<button :disabled="disabled" v-bind="$attrs"><slot /></button>',
  },
  NvRowActions: { props: ['label'], template: '<div data-testid="row-actions"><slot /></div>' },
  NvDropdownMenuItem: { props: ['disabled'], template: '<button><slot /></button>' },
  NvSelect: { template: '<div><slot /></div>' },
  NvSelectTrigger: { template: '<button><slot /></button>' },
  NvSelectContent: { template: '<div><slot /></div>' },
  NvSelectItem: { props: ['value'], template: '<div data-testid="status-option"><slot /></div>' },
  SelectValue: { template: '<span />' },
  NvSheet: { props: ['open'], template: '<div><slot /></div>' },
  NvSheetContent: { template: '<div><slot /></div>' },
  NvSheetHeader: { template: '<div><slot /></div>' },
  NvSheetTitle: { template: '<h2><slot /></h2>' },
  NvSheetDescription: { template: '<p><slot /></p>' },
  NvSheetFooter: { template: '<div><slot /></div>' },
  NvFieldGroup: { template: '<div><slot /></div>' },
  NvField: { template: '<div><slot /></div>' },
  NvFieldLabel: { template: '<label><slot /></label>' },
  NvInput: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template:
      '<input :value="modelValue" v-bind="$attrs" @input="$emit(\'update:modelValue\', $event.target.value)" />',
  },
  Spinner: true,
}

function mountPage(permissionCodes: string[] = ['business.mes.receipts.manage']) {
  const pinia = createPinia()
  const auth = useAuthStore(pinia)
  auth.$patch({
    principal: {
      principalId: 'u1',
      principalType: 'user',
      organizationId: 'org',
      environmentId: 'dev',
      loginName: 'op',
      permissionCodes,
    },
  })
  return mount(ReceiptsPage, { global: { plugins: [pinia], stubs } })
}

describe('MES receipts — failed inventory posting retry', () => {
  beforeEach(() => {
    routeState.query = {}
    routerState.replace.mockReset()
    notifySpies.success.mockReset()
    notifySpies.error.mockReset()
    receiptState.createReceiptRequest = vi.fn(async (_body: unknown) => undefined)
    receiptState.createReceiptRequestError = { value: undefined }
    receiptState.refreshReceiptRequests = vi.fn(async () => undefined)
    // 默认单一产出批次：自动选中，让创建相关用例可提交（后端强制引用真实产出批次）。
    receiptState.producedLots = [{ producedLotNo: 'LOT-FG-1', reportNo: 'PRPT-1', goodQuantity: 8 }]
    receiptState.producedLotsError = undefined
    receiptState.keyCounter = 0
    receiptState.retryInventoryPosting.mockClear()
    receiptState.retryingRequestNo = shallowRef<string | null>(null)
    receiptState.rows = [
      {
        receiptRequestId: 'r-failed',
        requestNo: 'FGR-000001',
        workOrderId: 'WO-1',
        skuId: 'FG-1',
        quantity: 10,
        receiptStatus: 'InventoryPostingFailed',
        requestedAtUtc: '2026-07-14T02:00:00Z',
        inventoryPostingFailureMessage: '库存不足，无法过账成品入库',
      },
      {
        receiptRequestId: 'r-posted',
        requestNo: 'FGR-000002',
        workOrderId: 'WO-2',
        skuId: 'FG-2',
        quantity: 5,
        receiptStatus: 'Posted',
        requestedAtUtc: '2026-07-14T03:00:00Z',
      },
    ]
  })

  it('offers a status filter option for failed postings', () => {
    const wrapper = mountPage()
    const optionTexts = wrapper.findAll('[data-testid="status-option"]').map((o) => o.text())
    expect(optionTexts).toContain('入库失败')
  })

  it('shows a danger badge and failure reason for a failed receipt', () => {
    const wrapper = mountPage()
    const failedRow = wrapper.findAll('[data-testid="row"]')[0]
    const badge = failedRow.get('[data-testid="badge"]')
    expect(badge.text()).toBe('入库失败')
    expect(badge.attributes('data-tone')).toBe('danger')
    expect(failedRow.text()).toContain('库存不足，无法过账成品入库')
  })

  it('shows an inline retry only for failed rows and calls the composable', async () => {
    const wrapper = mountPage()
    const rows = wrapper.findAll('[data-testid="row"]')
    const failedRetry = rows[0].findAll('button').find((b) => b.text().includes('重试'))
    const postedRetry = rows[1].findAll('button').find((b) => b.text().includes('重试'))
    expect(failedRetry).toBeDefined()
    expect(postedRetry).toBeUndefined()

    await failedRetry!.trigger('click')
    await flushPromises()
    expect(receiptState.retryInventoryPosting).toHaveBeenCalledWith('FGR-000001')
    expect(notifySpies.success).toHaveBeenCalled()
  })

  it('hides the retry button for read-only users without receipts.manage', () => {
    const wrapper = mountPage(['business.mes.receipts.read'])
    const retry = wrapper
      .findAll('[data-testid="row"]')[0]
      .findAll('button')
      .find((b) => b.text().includes('重试'))
    expect(retry).toBeUndefined()
  })

  it('hides the 登记完工入库 create entry for read-only users (create also needs manage)', () => {
    routeState.query = { workOrderId: 'WO-1', skuId: 'FG-1' }
    const wrapper = mountPage(['business.mes.receipts.read'])
    expect(wrapper.findAll('button').some((b) => b.text().includes('登记完工入库'))).toBe(false)
    expect(wrapper.text()).not.toContain('从工单详情发起')
  })

  it('disables the retry button while that row is retrying', () => {
    receiptState.retryingRequestNo = shallowRef<string | null>('FGR-000001')
    const wrapper = mountPage()
    const failedRetry = wrapper
      .findAll('[data-testid="row"]')[0]
      .findAll('button')
      .find((b) => b.text().includes('重试'))
    expect(failedRetry!.attributes('disabled')).toBeDefined()
  })

  // 工单/成品经 query 带入（工单详情发起），补齐必填单位成本后提交登记。
  async function fillCostAndSubmit(wrapper: ReturnType<typeof mountPage>, unitCost = '3.5') {
    await wrapper.get('#receipt-unit-cost').setValue(unitCost)
    await wrapper.get('form').trigger('submit')
    await flushPromises()
  }

  it('rotates the idempotency key per registration → two receipts for the same work order', async () => {
    routeState.query = { workOrderId: 'WO-1', skuId: 'FG-1' }
    const wrapper = mountPage()
    await flushPromises()

    await fillCostAndSubmit(wrapper, '3.5')
    // 成功后重置清空单位成本；连录第二笔需重新填。
    await fillCostAndSubmit(wrapper, '4')

    expect(receiptState.createReceiptRequest).toHaveBeenCalledTimes(2)
    const key1 = (
      receiptState.createReceiptRequest.mock.calls[0]![0] as { idempotencyKey?: string }
    ).idempotencyKey
    const key2 = (
      receiptState.createReceiptRequest.mock.calls[1]![0] as { idempotencyKey?: string }
    ).idempotencyKey
    expect(key1).toBeTruthy()
    expect(key2).toBeTruthy()
    expect(key1).not.toBe(key2)
    // 不再是「按工单恒定」的键（否则第二笔会回放第一张或幂等冲突）。
    expect(key1).not.toBe('receipt-WO-1')
    expect(notifySpies.success).toHaveBeenCalledTimes(2)
  })

  it('carries the selected produced lot in the create request (auto-selected when single)', async () => {
    routeState.query = { workOrderId: 'WO-1', skuId: 'FG-1' }
    const wrapper = mountPage()
    await flushPromises()

    await fillCostAndSubmit(wrapper)

    expect(receiptState.createReceiptRequest).toHaveBeenCalledTimes(1)
    const body = receiptState.createReceiptRequest.mock.calls[0]![0] as { producedLotNo?: string }
    // 后端在数量校验之前强制引用真实产出批次：请求必须携带工单报工产出的 producedLotNo。
    expect(body.producedLotNo).toBe('LOT-FG-1')
  })

  it('blocks create and guides reporting when the work order has no produced lots', async () => {
    routeState.query = { workOrderId: 'WO-1', skuId: 'FG-1' }
    receiptState.producedLots = []
    const wrapper = mountPage()
    await flushPromises()

    await fillCostAndSubmit(wrapper)

    // 无产出批次 → canCreate 为假，提交被拦截，且给出「先报工产出」引导而非盲提交后 500。
    expect(receiptState.createReceiptRequest).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('该工单暂无可入库的产出批次')
  })

  it('surfaces a retry (not “暂无产出批次”) when produced-lot loading fails', async () => {
    routeState.query = { workOrderId: 'WO-1', skuId: 'FG-1' }
    receiptState.producedLots = []
    receiptState.producedLotsError = new Error('403 forbidden')
    const wrapper = mountPage()
    await flushPromises()

    await fillCostAndSubmit(wrapper)

    // 加载失败区别于真实空态：给重试出口、不误报「暂无」，提交仍被拦截（未选批次）。
    expect(receiptState.createReceiptRequest).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('产出批次加载失败')
    expect(wrapper.text()).not.toContain('该工单暂无可入库的产出批次')
    expect(wrapper.findAll('button').some((b) => b.text().includes('重试'))).toBe(true)
  })

  it('keeps success feedback when the post-create list refresh fails (no contradictory error toast)', async () => {
    routeState.query = { workOrderId: 'WO-1', skuId: 'FG-1' }
    receiptState.refreshReceiptRequests = vi.fn(async () => {
      throw new Error('refresh 500')
    })
    const wrapper = mountPage()
    await flushPromises()

    await fillCostAndSubmit(wrapper)

    expect(receiptState.createReceiptRequest).toHaveBeenCalledTimes(1)
    expect(notifySpies.success).toHaveBeenCalledTimes(1)
    // 登记已成功：刷新失败不得再提示「登记失败」，避免矛盾反馈诱导重复提交。
    expect(notifySpies.error).not.toHaveBeenCalled()
  })

  it('maps the over-quantity backend error to the business copy even for short work order ids', async () => {
    routeState.query = { workOrderId: 'WO-1', skuId: 'FG-1' }
    receiptState.createReceiptRequest = vi.fn(async () => {
      throw new Error('mutation rejected')
    })
    // 短工单号：后端原文为 ≤60 字中文，notifyError 会优先透传原文；映射必须作为「实际错误消息」传入才生效。
    receiptState.createReceiptRequestError = {
      value: new Error('累计完工入库申请数量超过工单完工数量，WorkOrderId = WO-1'),
    }
    const wrapper = mountPage()
    await flushPromises()

    await fillCostAndSubmit(wrapper)

    expect(notifySpies.error).toHaveBeenCalledTimes(1)
    const [arg] = notifySpies.error.mock.calls[0]!
    expect((arg as Error).message).toBe(
      '累计请求量超过完工数量，请先核对该工单的报工完成数量后再登记入库。',
    )
    expect(notifySpies.success).not.toHaveBeenCalled()
  })
})
