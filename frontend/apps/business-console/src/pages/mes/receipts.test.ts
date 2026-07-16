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
  retryInventoryPosting: vi.fn(async () => undefined),
  rows: [] as Array<Record<string, unknown>>,
  retryingRequestNo: undefined as unknown as { value: string | null },
}))

vi.mock('@/composables/useBusinessMes', () => {
  return {
    useMesFinishedGoodsReceipts: () => ({
      createReceiptRequest: vi.fn(async () => undefined),
      createReceiptRequestError: ref(undefined),
      createReceiptRequestPending: ref(false),
      filters: { organizationId: 'org', environmentId: 'dev', status: undefined },
      receiptRequests: ref(receiptState.rows),
      receiptRequestsError: ref(undefined),
      receiptRequestsPending: ref(false),
      receiptRequestsTotal: ref(receiptState.rows.length),
      refreshReceiptRequests: vi.fn(async () => undefined),
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
  NvInput: { props: ['modelValue'], template: '<input :value="modelValue" v-bind="$attrs" />' },
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

  it('disables the retry button while that row is retrying', () => {
    receiptState.retryingRequestNo = shallowRef<string | null>('FGR-000001')
    const wrapper = mountPage()
    const failedRetry = wrapper
      .findAll('[data-testid="row"]')[0]
      .findAll('button')
      .find((b) => b.text().includes('重试'))
    expect(failedRetry!.attributes('disabled')).toBeDefined()
  })
})
