import { flushPromises, mount } from '@vue/test-utils'
import { ref } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import ReceiptCreateSheet from './ReceiptCreateSheet.vue'

const state = vi.hoisted(() => ({
  createReceiptRequest: vi.fn(async (_body: unknown) => undefined),
  keyCounter: 0,
}))

vi.mock('@/utils/notify', () => ({ notifySuccess: vi.fn(), notifyError: vi.fn() }))

vi.mock('@/composables/useBusinessMes', () => ({
  makeIdempotencyKey: (prefix: string) => `${prefix}-key-${++state.keyCounter}`,
  useMesWorkOrderProducedLots: () => ({
    // 两个工单各一个产出批次，均自动选中。
    producedLots: ref([
      { producedLotNo: 'LOT-X', reportNo: 'PRPT-X', goodQuantity: 20, remainingQuantity: 20 },
    ]),
    producedLotsError: ref(undefined),
    producedLotsPending: ref(false),
    refreshProducedLots: vi.fn(async () => undefined),
  }),
  useMesFinishedGoodsReceipts: () => ({
    createReceiptRequest: state.createReceiptRequest,
    createReceiptRequestError: { value: undefined },
    createReceiptRequestPending: ref(false),
    refreshReceiptRequests: vi.fn(async () => undefined),
  }),
}))

const stubs = {
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
  NvSelect: { template: '<div><slot /></div>' },
  NvSelectTrigger: { template: '<button><slot /></button>' },
  NvSelectContent: { template: '<div><slot /></div>' },
  NvSelectItem: { props: ['value'], template: '<div><slot /></div>' },
  NvSelectValue: { template: '<span />' },
  SelectValue: { template: '<span />' },
  NvButton: { template: '<button v-bind="$attrs"><slot /></button>' },
  Spinner: true,
}

function mountSheet(workOrderId: string, skuId: string) {
  return mount(ReceiptCreateSheet, {
    props: { open: true, organizationId: 'org', environmentId: 'dev', workOrderId, skuId },
    global: { stubs },
  })
}

describe('ReceiptCreateSheet', () => {
  beforeEach(() => {
    state.createReceiptRequest = vi.fn(async (_body: unknown) => undefined)
    state.keyCounter = 0
  })

  it('fully resets the form (incl. idempotency key) when the work order context switches', async () => {
    const wrapper = mountSheet('WO-A', 'FG-A')
    await flushPromises()

    // 在工单 A 上改动数量/单位成本/单位，并提交一次拿到 A 的幂等键。
    await wrapper.get('#receipt-quantity').setValue('7')
    await wrapper.get('#receipt-unit-cost').setValue('9.9')
    await wrapper.get('#receipt-uom').setValue('BOX')
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    const keyA = (state.createReceiptRequest.mock.calls[0]![0] as { idempotencyKey?: string })
      .idempotencyKey

    // 切换到工单 B（未带建议数量）：表单整体重置，不得沿用 A 的数量/成本/单位。
    await wrapper.setProps({ workOrderId: 'WO-B', skuId: 'FG-B' })
    await flushPromises()
    expect((wrapper.get('#receipt-quantity').element as HTMLInputElement).value).toBe('1')
    expect((wrapper.get('#receipt-unit-cost').element as HTMLInputElement).value).toBe('')
    expect((wrapper.get('#receipt-uom').element as HTMLInputElement).value).toBe('EA')

    // 在 B 上补全后提交：幂等键与 A 不同（不复用上一工单会话键）。
    await wrapper.get('#receipt-unit-cost').setValue('5')
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    const keyB = state.createReceiptRequest.mock.calls[1]![0] as {
      idempotencyKey?: string
      workOrderId?: string
    }
    expect(keyB.workOrderId).toBe('WO-B')
    expect(keyB.idempotencyKey).not.toBe(keyA)
  })
})
