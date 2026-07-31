import type { VueWrapper } from '@vue/test-utils'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import DeliveriesPage from './sales/deliveries.vue'
import OrdersPage from './sales/orders.vue'
import QuotationsPage from './sales/quotations.vue'

// 名录解析不是这些用例的被测对象；给稳定桩（解析不出名称→页面回退显编码），
// 避免真实实现去取业务上下文 store 而要求测试装 Pinia。
vi.mock('@/composables/useSkuNames', async () => {
  const { computed } = await import('vue')
  return {
    useSkuNames: () => ({
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

vi.mock('@/composables/useOrderUrgency', () => ({
  useOrderUrgencies: () => ({ byReference: { value: new Map() }, refresh: vi.fn() }),
}))
vi.mock('@/components/urgency/OrderUrgencyBadge.vue', () => ({
  default: {
    props: ['orderReference', 'mode', 'urgency'],
    template:
      '<span data-testid="order-urgency" :data-ref="orderReference" :data-mode="mode">未计算</span>',
  },
}))

const state = vi.hoisted(() => ({
  createQuotation: vi.fn(async (_body: unknown) => undefined),
  quotations: [] as Array<Record<string, unknown>>,
  deliveries: [] as Array<Record<string, unknown>>,
  salesOrders: [] as Array<Record<string, unknown>>,
  approveQuotation: vi.fn(async (_no: string) => undefined),
  createSalesOrder: vi.fn(async (_body: unknown) => undefined),
  releaseCreditHold: undefined as unknown as ReturnType<typeof vi.fn>,
  toastError: vi.fn(),
  toastSuccess: vi.fn(),
}))

// 反馈走真实 notify 分层透传，只把 toast 换成 spy：断言的是「用户最终看到的那句话」。
vi.mock('@nerv-iip/ui', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@nerv-iip/ui')>()),
  toast: {
    error: (...args: unknown[]) => state.toastError(...args),
    success: (...args: unknown[]) => state.toastSuccess(...args),
  },
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: { keyword: 'SO-DEMO-001' } }),
}))

function listShape(itemsRef: () => Array<Record<string, unknown>>) {
  return {
    filters: reactive({
      status: undefined as string | undefined,
      keyword: undefined as string | undefined,
      skip: 0,
      take: 10,
    }),
    items: computed(() => itemsRef()),
    total: computed(() => itemsRef().length),
    organizationId: computed(() => 'org-001'),
    environmentId: computed(() => 'env-dev'),
    error: shallowRef(undefined),
    pending: shallowRef(false),
    ready: computed(() => true),
    refresh: vi.fn(),
  }
}

vi.mock('@/composables/useBusinessErp', () => ({
  useErpQuotations: () => ({
    ...listShape(() => state.quotations),
    approveQuotation: state.approveQuotation,
    approveQuotationPending: shallowRef(false),
    approveQuotationError: shallowRef(undefined),
    createQuotation: state.createQuotation,
    createQuotationPending: shallowRef(false),
    createQuotationError: shallowRef(undefined),
  }),
  useErpSalesOrders: () => {
    const base = listShape(() => state.salesOrders)
    return {
      filters: base.filters,
      ready: base.ready,
      salesOrders: base.items,
      salesOrdersTotal: base.total,
      salesOrdersError: shallowRef(undefined),
      salesOrdersPending: shallowRef(false),
      refreshSalesOrders: vi.fn(),
      createSalesOrder: state.createSalesOrder,
      createSalesOrderPending: shallowRef(false),
      createSalesOrderError: shallowRef(undefined),
      releaseCreditHold: state.releaseCreditHold,
      releaseCreditHoldPending: shallowRef(false),
      releaseCreditHoldError: shallowRef(undefined),
    }
  },
  useErpDeliveryOrders: () => ({
    ...listShape(() => state.deliveries),
    releaseDeliveryOrder: vi.fn(),
    releaseDeliveryOrderPending: shallowRef(false),
    releaseDeliveryOrderError: shallowRef(undefined),
  }),
}))

// 客户 / 物料 / 工厂改成只选：目录 composable 内部走 pinia + colada，测试整体打桩给定候选。
vi.mock('@/composables/useErpPickerCatalog', () => ({
  useErpPartnerCatalog: () => ({
    customerOptions: computed(() => [{ value: 'CUST-001', label: '示例客户' }]),
    supplierOptions: computed(() => []),
    partnersPending: shallowRef(false),
  }),
  // 两种不同计量单位的物料：用来证明单据行的单位来自物料主档，而不是页面常量。
  useErpItemCatalog: () => ({
    skuOptions: computed(() => [
      { value: 'SKU-SHOCK-FR-01', label: '前减振器总成' },
      { value: 'RM-BAR-45-01', label: '45号钢棒料' },
    ]),
    skusPending: shallowRef(false),
    uomOptions: computed(() => [
      { value: 'pcs', label: '个' },
      { value: 'kg', label: '千克' },
    ]),
    uomsPending: shallowRef(false),
    baseUomBySku: computed(
      () =>
        new Map([
          ['SKU-SHOCK-FR-01', 'pcs'],
          ['RM-BAR-45-01', 'kg'],
        ]),
    ),
  }),
  useErpSiteCatalog: () => ({
    siteOptions: computed(() => [{ value: 'SITE-01', label: '一号工厂' }]),
    sitesPending: shallowRef(false),
  }),
}))

vi.mock('@/composables/usePagedList', () => ({
  usePagedList: () => ({
    page: shallowRef(1),
    pageSize: shallowRef('10'),
    pageSizeNumber: shallowRef(10),
    resetPage: vi.fn(),
  }),
}))

const layoutStub = { BusinessLayout: { template: '<main><slot /></main>' } }
const selectStubs = {
  NvSelect: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template:
      '<select :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)"><slot /></select>',
  },
  NvSelectTrigger: { template: '<span><slot /></span>' },
  SelectValue: { template: '<span />' },
  NvSelectContent: { template: '<slot />' },
  NvSelectItem: { props: ['value'], template: '<option :value="value"><slot /></option>' },
}
// 弹框外壳：真实 NvDialog 会 teleport 到 body，两组用例都只关心壳里的表单行为，桩成普通节点。
const dialogShellStubs = {
  NvDialog: { props: ['open'], template: '<div><slot /></div>' },
  NvDialogContent: { template: '<div><slot /></div>' },
  NvDialogHeader: { template: '<div><slot /></div>' },
  NvDialogTitle: { template: '<h2><slot /></h2>' },
  NvDialogDescription: { template: '<p><slot /></p>' },
  NvDialogFooter: { template: '<div><slot /></div>' },
  NvDialogClose: { template: '<div><slot /></div>' },
}
// 新建销售订单弹框：NvEntityPicker 要开面板选工厂，本用例只关心「提交失败时用户看到什么」。
const orderDialogStubs = {
  ...dialogShellStubs,
  NvEntityPicker: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template:
      '<button type="button" data-testid="site-picker" @click="$emit(\'update:modelValue\', \'SITE-01\')">选择履约工厂</button>',
  },
}

// 新建报价弹窗：实体选择器桩成可直接读写的输入位，用例只关心提交体里的单位来自哪里。
const dialogStubs = {
  ...dialogShellStubs,
  NvEntityPicker: {
    props: ['modelValue', 'options', 'id'],
    emits: ['update:modelValue'],
    template:
      '<input :id="id" :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />',
  },
  NvButton: {
    template: '<button v-bind="$attrs" :type="$attrs.type ?? \'button\'"><slot /></button>',
  },
}
/** 打开「新建报价」弹窗（按钮按文案找，不为测试往页面塞标记）。 */
async function openCreateDialog(wrapper: VueWrapper) {
  const trigger = wrapper.findAll('button').find((b) => b.text().includes('新建报价'))
  expect(trigger).toBeTruthy()
  await trigger!.trigger('click')
  await flushPromises()
}
const rowActionStubs = {
  RowActions: { template: '<div data-testid="row-actions"><slot /></div>' },
  DropdownMenuItem: {
    emits: ['click'],
    template: '<button type="button" @click="$emit(\'click\', $event)"><slot /></button>',
  },
}

// 弹窗结构不是被测对象：内联渲染 slot，避免 teleport 干扰按钮断言。
const dialogStubs = {
  NvDialog: { props: ['open'], template: '<div v-if="open"><slot /></div>' },
  NvDialogContent: { template: '<div><slot /></div>' },
  NvDialogHeader: { template: '<div><slot /></div>' },
  NvDialogTitle: { template: '<h2><slot /></h2>' },
  NvDialogDescription: { template: '<p><slot /></p>' },
  NvDialogFooter: { template: '<div><slot /></div>' },
  NvDialogClose: { template: '<span><slot /></span>' },
}

beforeEach(() => {
  // 履约追踪 Sheet 里的「对该单排产」按权限码显隐，组件因此要读 auth store（MAN-694 / #1262）。
  setActivePinia(createPinia())
  state.createQuotation = vi.fn(async (_body: unknown) => undefined)
  state.salesOrders = []
  state.deliveries = []
  state.quotations = []
  state.approveQuotation = vi.fn(async () => undefined)
  state.createSalesOrder = vi.fn(async () => undefined)
  state.releaseCreditHold = vi.fn().mockResolvedValue('credit-release-approval-started')
  state.toastError.mockReset()
  state.toastSuccess.mockReset()
})

function draftQuotation() {
  return {
    quotationNo: 'QUO-DRAFT-1',
    customerCode: 'CUST-A',
    status: 'Draft',
    totalAmount: 1000,
    expiresOn: '2026-12-31',
  }
}

describe('ERP sales quotation page', () => {
  it('keeps quotation status filter aligned with backend values', async () => {
    const wrapper = mount(QuotationsPage, { global: { stubs: { ...layoutStub, ...selectStubs } } })
    await flushPromises()

    const values = wrapper.findAll('option').map((o) => o.attributes('value'))
    expect(new Set(values)).toEqual(new Set(['all', 'Draft', 'Approved', 'Rejected', 'Expired']))
    expect(values).not.toContain('submitted')
    expect(wrapper.text()).toContain('待审')
    expect(wrapper.text()).not.toContain('待审批')
  })

  it('renders approve action only for Draft quotations and counts Draft KPI', async () => {
    state.quotations = [
      {
        quotationNo: 'QUO-DRAFT-1',
        customerCode: 'CUST-A',
        status: 'Draft',
        totalAmount: 1000,
        expiresOn: '2026-12-31',
      },
      {
        quotationNo: 'QUO-APPROVED-1',
        customerCode: 'CUST-B',
        status: 'Approved',
        totalAmount: 2000,
        expiresOn: '2026-12-31',
      },
    ]
    const wrapper = mount(QuotationsPage, {
      global: { stubs: { ...layoutStub, ...rowActionStubs } },
    })
    await flushPromises()

    expect(wrapper.findAll('[data-testid="row-actions"]')).toHaveLength(1)
    expect(wrapper.findAll('button').filter((b) => b.text().includes('审批通过'))).toHaveLength(1)
    expect(wrapper.text()).toMatch(/待审报价[^0-9]*1/)
  })

  // MAN-700 / #1289：generated client 抛的是响应体对象，旧写法只判 instanceof Error，
  // 后端 400 的领域拒绝理由全被吞成「审批报价失败，请稍后重试。」，用户不知道哪儿不满足。
  it('surfaces the service domain message when approving a quotation fails', async () => {
    state.quotations = [draftQuotation()]
    state.approveQuotation = vi.fn(async () => {
      throw { title: 'Bad Request', status: 400, detail: '报价单已过期，不能审批' }
    })
    const wrapper = mount(QuotationsPage, {
      global: { stubs: { ...layoutStub, ...rowActionStubs } },
    })
    await flushPromises()

    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('审批通过'))!
      .trigger('click')
    await flushPromises()

    expect(state.toastError).toHaveBeenCalledWith('审批报价失败：报价单已过期，不能审批')
    expect(state.toastSuccess).not.toHaveBeenCalled()
  })

  it('never puts an English 500 body on screen — falls back to the domain hint', async () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {})
    state.quotations = [draftQuotation()]
    state.approveQuotation = vi.fn(async () => {
      throw { title: 'Internal Server Error', status: 500 }
    })
    const wrapper = mount(QuotationsPage, {
      global: { stubs: { ...layoutStub, ...rowActionStubs } },
    })
    await flushPromises()

    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('审批通过'))!
      .trigger('click')
    await flushPromises()

    expect(state.toastError).toHaveBeenCalledWith('审批报价失败，请稍后重试。')
    expect(state.toastError).not.toHaveBeenCalledWith(expect.stringContaining('Internal Server'))
    consoleError.mockRestore()
  })

  // 回归 #1285：报价行的 uomCode 曾写死为 'EA'，而世界里根本没有这个单位，
  // 真实 MRP / 单位换算必然 500。单位必须来自所选物料主档的基本单位。
  it('sends the selected SKU base unit of measure instead of a hardcoded one', async () => {
    const wrapper = mount(QuotationsPage, {
      global: { stubs: { ...layoutStub, ...dialogStubs } },
    })
    await flushPromises()

    await openCreateDialog(wrapper)

    await wrapper.get('#erp-quo-customer').setValue('CUST-001')
    await wrapper.get('#erp-quo-expires').setValue('2026-12-31')
    // 选一个按千克计量的物料：单位应自动带出 kg。
    await wrapper.get('#erp-quo-sku').setValue('RM-BAR-45-01')
    await wrapper.get('#erp-quo-required').setValue('2026-09-01')
    await flushPromises()
    expect((wrapper.get('#erp-quo-uom').element as HTMLInputElement).value).toBe('kg')

    await wrapper.get('form').trigger('submit')
    await flushPromises()

    const body = state.createQuotation.mock.calls[0]![0] as {
      lines?: Array<{ skuCode?: string; uomCode?: string }>
    }
    expect(body.lines?.[0]?.skuCode).toBe('RM-BAR-45-01')
    expect(body.lines?.[0]?.uomCode).toBe('kg')

    // 换成按件计量的物料：单位跟着物料主档变，不是任何固定常量。
    await wrapper.get('#erp-quo-sku').setValue('SKU-SHOCK-FR-01')
    await flushPromises()
    expect((wrapper.get('#erp-quo-uom').element as HTMLInputElement).value).toBe('pcs')
  })

  it('blocks submission while the unit of measure is still empty', async () => {
    const wrapper = mount(QuotationsPage, {
      global: { stubs: { ...layoutStub, ...dialogStubs } },
    })
    await flushPromises()

    await openCreateDialog(wrapper)

    await wrapper.get('#erp-quo-customer').setValue('CUST-001')
    await wrapper.get('#erp-quo-expires').setValue('2026-12-31')
    // 物料目录里没有的编码（深链/历史数据）：带不出基本单位，就不许提交，也不预填假单位。
    await wrapper.get('#erp-quo-sku').setValue('SKU-NOT-IN-CATALOG')
    await wrapper.get('#erp-quo-required').setValue('2026-09-01')
    await flushPromises()
    expect((wrapper.get('#erp-quo-uom').element as HTMLInputElement).value).toBe('')

    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(state.createQuotation).not.toHaveBeenCalled()
  })
})

describe('ERP sales order and delivery pages', () => {
  // MAN-700 / #1289 的实测受害点：报价转订单的 400 曾被整条吞成
  // 「创建销售订单失败，请稍后重试。」，后端说的「报价单未审批通过」根本到不了用户眼前。
  it('surfaces the service domain message when converting a quotation to a sales order fails', async () => {
    state.createSalesOrder = vi.fn(async () => {
      throw { title: 'Bad Request', status: 400, detail: '报价单未审批通过，不能转订单' }
    })
    const wrapper = mount(OrdersPage, {
      global: { stubs: { ...layoutStub, ...orderDialogStubs } },
    })
    await flushPromises()

    await wrapper.get('#erp-so-quotation').setValue('QUO-2026-0007')
    await wrapper.get('[data-testid="site-picker"]').trigger('click')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(state.createSalesOrder).toHaveBeenCalledTimes(1)
    expect(state.toastError).toHaveBeenCalledWith('创建销售订单失败：报价单未审批通过，不能转订单')
    expect(state.toastSuccess).not.toHaveBeenCalled()
  })

  it('sales orders keep keyword search and only expose the shared urgency display selector', async () => {
    const wrapper = mount(OrdersPage, { global: { stubs: { ...layoutStub, ...selectStubs } } })
    await flushPromises()

    expect(wrapper.find('[aria-label="销售订单关键字"]').exists()).toBe(true)
    expect((wrapper.get('[aria-label="销售订单关键字"]').element as HTMLInputElement).value).toBe(
      'SO-DEMO-001',
    )
    // The only select is the shared urgency display-mode control (no status filter).
    expect(wrapper.findAll('select')).toHaveLength(1)
    expect(wrapper.findAll('option').map((o) => o.attributes('value'))).toEqual([
      'level',
      'businessPriority',
      'dynamicUrgency',
      'executionRisk',
      'criticalRatio',
      'slack',
      'expectedDelay',
    ])
  })

  it('maps the real sales order number into the shared urgency badge', async () => {
    state.salesOrders = [
      {
        salesOrderNo: 'SO-2026-0007',
        customerCode: 'CUST-A',
        status: 'released',
        totalAmount: 100,
      },
    ]
    const wrapper = mount(OrdersPage, { global: { stubs: layoutStub } })
    await flushPromises()

    const badge = wrapper.get('[data-testid="order-urgency"]')
    expect(badge.attributes('data-ref')).toBe('SO-2026-0007')
    expect(badge.attributes('data-mode')).toBe('level')
  })

  it('shows the credit-hold release entry only on credit-held rows', async () => {
    state.salesOrders = [
      {
        salesOrderNo: 'SO-HELD-001',
        customerCode: 'CUST-A',
        status: 'credit-held',
        totalAmount: 900_000,
      },
      { salesOrderNo: 'SO-REL-001', customerCode: 'CUST-B', status: 'released', totalAmount: 100 },
    ]
    const wrapper = mount(OrdersPage, { global: { stubs: { ...layoutStub, ...dialogStubs } } })
    await flushPromises()

    // 行内入口精确匹配「解冻复核」；弹窗提交按钮是「提交解冻复核」，不计入行入口。
    const releaseButtons = wrapper.findAll('button').filter((b) => b.text().trim() === '解冻复核')
    expect(releaseButtons).toHaveLength(1)
  })

  it('submits credit-hold release for the held order and states the approval semantics', async () => {
    state.salesOrders = [
      {
        salesOrderNo: 'SO-HELD-001',
        customerCode: 'CUST-A',
        status: 'credit-held',
        totalAmount: 900_000,
      },
    ]
    const wrapper = mount(OrdersPage, { global: { stubs: { ...layoutStub, ...dialogStubs } } })
    await flushPromises()

    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('解冻复核'))!
      .trigger('click')
    await flushPromises()

    // 语义在界面明确：谁发起、谁裁决、通过后订单回到什么状态。
    expect(wrapper.text()).toContain('提交信用解冻复核')
    expect(wrapper.text()).toContain('审批')
    expect(wrapper.text()).toContain('已下达（released）')

    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('提交解冻复核'))!
      .trigger('click')
    await flushPromises()

    expect(state.releaseCreditHold).toHaveBeenCalledWith({ salesOrderNo: 'SO-HELD-001' })
  })

  it('deliveries keep keyword search and no status select', async () => {
    const wrapper = mount(DeliveriesPage, { global: { stubs: { ...layoutStub, ...selectStubs } } })
    await flushPromises()

    expect(wrapper.find('[aria-label="发货关键字"]').exists()).toBe(true)
    expect(wrapper.findAll('select')).toHaveLength(0)
  })
})
