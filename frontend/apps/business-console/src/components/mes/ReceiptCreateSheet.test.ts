import { flushPromises, mount } from '@vue/test-utils'
import { reactive, ref } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import ReceiptCreateSheet from './ReceiptCreateSheet.vue'

// 名录解析不是这些用例的被测对象；给稳定桩（解析不出名称→页面回退显编码），
// 避免真实实现去取业务上下文 store 而要求测试装 Pinia。
// 物料主档：登记单位取所选成品的基本单位（两个成品刻意不同单位，用来证明单位来自主档而非常量）。
const baseUomBySkuFixture: Record<string, string> = { 'FG-A': 'pcs', 'FG-B': 'kg' }
vi.mock('@/composables/useSkuNames', async () => {
  const { computed } = await import('vue')
  return {
    useSkuNames: () => ({
      baseUomBySku: computed(() => new Map(Object.entries(baseUomBySkuFixture))),
      resolveBaseUom: (code?: string | null) =>
        code ? baseUomBySkuFixture[code.trim()] : undefined,
      resolveSkuName: () => undefined,
      resolveSkuLabel: (code?: string | null) => code ?? '未指定物料',
      skuByCode: computed(() => new Map<string, string>()),
      skusPending: computed(() => false),
    }),
  }
})
vi.mock('@/composables/useBusinessPartnerNames', async () => {
  const { computed } = await import('vue')
  return {
    useBusinessPartnerNames: () => ({
      resolvePartner: () => undefined,
      resolvePartnerLabel: (code?: string | null, fallback = '未指定') => code ?? fallback,
      partnerByCode: computed(() => new Map<string, string>()),
      partners: computed(() => []),
      partnersPending: computed(() => false),
    }),
  }
})
vi.mock('@/composables/useMasterDataDisplayNames', async () => {
  const { computed } = await import('vue')
  const emptyIndex = computed(() => new Map<string, string>())
  return {
    useMasterDataDisplayNames: () => ({
      resolveDevice: () => undefined,
      resolveLocation: () => undefined,
      resolveWorkCenter: () => undefined,
      resolveTeam: () => undefined,
      resolveUom: () => undefined,
      resolveWorkshop: () => undefined,
      resolveLine: () => undefined,
      formatUom: (code?: string | null, fallback = '') => code ?? fallback,
      deviceByCode: emptyIndex,
      locationByCode: emptyIndex,
      workCenterByCode: emptyIndex,
      teamByCode: emptyIndex,
      uomByCode: emptyIndex,
      workshopByCode: emptyIndex,
      lineByCode: emptyIndex,
    }),
  }
})

const state = vi.hoisted(() => ({
  createReceiptRequest: vi.fn(async (_body: unknown) => undefined),
  keyCounter: 0,
}))

vi.mock('@/utils/notify', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/utils/notify')>()),
  notifySuccess: vi.fn(),
  notifyError: vi.fn(),
  notifyOperationFailure: vi.fn(),
}))

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

// 单位已从自由文本改成计量单位主数据选择器，目录 composable 走 pinia + colada，测试直接给定选项。
// 弹窗用它把 skuId 解析成成品名；名录不是本用例被测对象，给稳定桩。
vi.mock('@/composables/mes/useMesDisplayNames', () => ({
  useMesDisplayNames: () => ({
    resolveSku: (v?: string | null) => v ?? '无',
    resolveSkuLabel: (v?: string | null) => v ?? '未指定物料',
  }),
}))

vi.mock('@/composables/useBusinessMasterData', () => ({
  useBusinessUoms: () => ({
    filters: reactive({ skip: 0, take: 200 }),
    uoms: ref([
      { code: 'pcs', displayName: '个', active: true },
      { code: 'kg', displayName: '千克', active: true },
      { code: 'BOX', displayName: '箱', active: true },
    ]),
    uomsError: ref(undefined),
    uomsPending: ref(false),
    uomsTotal: ref(3),
    refreshUoms: vi.fn(),
  }),
}))

const stubs = {
  CarriedContextSummary: { props: ['label', 'items'], template: '<div><slot /></div>' },
  // 单位选择器桩成同名 id 的输入位，让下面的 setValue 仍然表达「选中了某个单位」。
  NvSearchSelect: {
    props: ['modelValue', 'options', 'id'],
    emits: ['update:modelValue'],
    template:
      '<input :id="id" :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />',
  },
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

  // 回归 #1285：登记体的 uomCode 曾写死为 'EA'，世界里没有这个单位，后端单位换算必然失败。
  it('submits the finished-goods base unit of measure from SKU master data', async () => {
    const wrapper = mountSheet('WO-A', 'FG-A')
    await flushPromises()

    expect((wrapper.get('#receipt-uom').element as HTMLInputElement).value).toBe('pcs')
    await wrapper.get('#receipt-unit-cost').setValue('9.9')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    const body = state.createReceiptRequest.mock.calls[0]![0] as { uomCode?: string }
    expect(body.uomCode).toBe('pcs')
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

    // 切换到工单 B（未带建议数量）：表单整体重置，不得沿用 A 的数量/成本/单位；
    // 单位回到 B 这个成品在物料主档里的基本单位（kg），不是任何界面常量。
    await wrapper.setProps({ workOrderId: 'WO-B', skuId: 'FG-B' })
    await flushPromises()
    expect((wrapper.get('#receipt-quantity').element as HTMLInputElement).value).toBe('1')
    expect((wrapper.get('#receipt-unit-cost').element as HTMLInputElement).value).toBe('')
    expect((wrapper.get('#receipt-uom').element as HTMLInputElement).value).toBe('kg')

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
