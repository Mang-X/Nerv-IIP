import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import PurchaseRequisitionsPage from './index.vue'
import PurchaseOrdersPage from './procurement/purchase-orders.vue'
import ReceiptsPage from './procurement/receipts.vue'

const state = vi.hoisted(() => ({
  purchaseOrders: [] as Array<Record<string, unknown>>,
  purchaseOrdersTotal: 42,
  canManagePurchaseOrders: false,
  canReadMrp: false,
  revisePurchaseOrder: vi.fn(),
  purchaseRequisitions: [] as Array<Record<string, unknown>>,
  purchaseRequisitionsTotal: 9,
  receipts: [] as Array<Record<string, unknown>>,
  receiptsTotal: 7,
  convertPurchaseRequisition: vi.fn(),
  recordPurchaseReceipt: vi.fn(),
  notifySuccess: vi.fn(),
  notifyWarning: vi.fn(),
  supplierPartners: [] as Array<Record<string, unknown>>,
}))

const purchaseOrderFilters = reactive<{
  status?: string
  keyword?: string
  skip: number
  take: number
}>({
  status: undefined,
  keyword: undefined,
  skip: 0,
  take: 10,
})

const receiptFilters = reactive<{ status?: string; keyword?: string; skip: number; take: number }>({
  status: undefined,
  keyword: undefined,
  skip: 0,
  take: 10,
})

vi.mock('@/composables/erp/useBusinessErp', () => ({
  useErpPurchaseRequisitions: () => ({
    filters: purchaseOrderFilters,
    items: computed(() => state.purchaseRequisitions),
    total: computed(() => state.purchaseRequisitionsTotal),
    organizationId: computed(() => 'org-001'),
    environmentId: computed(() => 'env-dev'),
    error: shallowRef(undefined),
    pending: shallowRef(false),
    ready: computed(() => true),
    refresh: vi.fn(),
    convertToPurchaseOrder: state.convertPurchaseRequisition,
    convertToPurchaseOrderPending: shallowRef(false),
    convertToPurchaseOrderError: shallowRef(undefined),
  }),
  useErpPurchaseOrders: () => ({
    filters: purchaseOrderFilters,
    items: computed(() => state.purchaseOrders),
    total: computed(() => state.purchaseOrdersTotal),
    organizationId: computed(() => 'org-001'),
    environmentId: computed(() => 'env-dev'),
    error: shallowRef(undefined),
    pending: shallowRef(false),
    ready: computed(() => true),
    refresh: vi.fn(),
    createPurchaseOrder: vi.fn(),
    revisePurchaseOrder: state.revisePurchaseOrder,
    revisePurchaseOrderPending: shallowRef(false),
    createPurchaseOrderPending: shallowRef(false),
    createPurchaseOrderError: shallowRef(undefined),
  }),
  useErpPurchaseReceipts: () => ({
    filters: receiptFilters,
    items: computed(() => state.receipts),
    total: computed(() => state.receiptsTotal),
    organizationId: computed(() => 'org-001'),
    environmentId: computed(() => 'env-dev'),
    error: shallowRef(undefined),
    pending: shallowRef(false),
    ready: computed(() => true),
    refresh: vi.fn(),
    recordPurchaseReceipt: state.recordPurchaseReceipt,
    recordPurchaseReceiptPending: shallowRef(false),
  }),
}))

vi.mock('@/composables/master-data', () => ({
  useBusinessPartners: () => ({
    filters: reactive({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      resourceType: 'business-partner',
      includeDisabled: false,
      skip: 0,
      take: 100,
    }),
    partners: computed(() => state.supplierPartners),
    partnersTotal: computed(() => state.supplierPartners.length),
    partnersPending: shallowRef(false),
    partnersError: shallowRef(undefined),
    refreshPartners: vi.fn(),
  }),
  // 采购订单的工厂 / 物料 / 单位改成只选，`useErpPickerCatalog` 会来取这几份主数据目录。
  useBusinessMasterDataResources: () => ({
    filters: reactive({ skip: 0, take: 500 }),
    resources: computed(() => [
      { resourceType: 'site', code: 'SITE-01', displayName: '一号工厂', active: true },
    ]),
    resourcesTotal: computed(() => 1),
    resourcesPending: shallowRef(false),
    resourcesError: shallowRef(undefined),
    refreshResources: vi.fn(),
  }),
  useBusinessSkus: () => ({
    filters: reactive({ skip: 0, take: 500 }),
    skus: computed(() => [
      { code: 'SKU-RM-001', displayName: '原材料一', baseUomCode: 'EA', active: true },
    ]),
    skusTotal: computed(() => 1),
    skusPending: shallowRef(false),
    skusError: shallowRef(undefined),
    refreshSkus: vi.fn(),
  }),
  useBusinessUoms: () => ({
    filters: reactive({ skip: 0, take: 500 }),
    uoms: computed(() => [{ code: 'EA', displayName: '个', active: true }]),
    uomsTotal: computed(() => 1),
    uomsPending: shallowRef(false),
    uomsError: shallowRef(undefined),
    refreshUoms: vi.fn(),
  }),
}))

vi.mock('@/utils/notify', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/utils/notify')>()),
  notifySuccess: state.notifySuccess,
  notifyWarning: state.notifyWarning,
}))

vi.mock('@/composables/shared/usePagedList', () => ({
  usePagedList: () => ({
    page: shallowRef(1),
    pageSize: shallowRef('10'),
    pageSizeNumber: shallowRef(10),
    resetPage: vi.fn(),
  }),
}))

vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({
    principal: {
      permissionCodes: [
        ...(state.canManagePurchaseOrders ? ['business.erp.procurement.manage'] : []),
        ...(state.canReadMrp ? ['business.planning.mrp.read'] : []),
      ],
    },
  }),
}))

vi.mock('vue-router', () => ({
  RouterLink: { props: ['to'], template: '<a><slot /></a>' },
  useRoute: () => ({ query: {} }),
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
  NvSelectValue: { template: '<span />' },
  NvSelectContent: { template: '<slot />' },
  NvSelectItem: { props: ['value'], template: '<option :value="value"><slot /></option>' },
}
const dialogStubs = {
  NvDialog: {
    props: ['open'],
    emits: ['update:open'],
    template: '<section v-if="open" class="dialog"><slot /></section>',
  },
  NvDialogClose: { template: '<span><slot /></span>' },
  NvDialogContent: { template: '<div><slot /></div>' },
  NvDialogDescription: { template: '<p><slot /></p>' },
  NvDialogFooter: { template: '<footer><slot /></footer>' },
  NvDialogHeader: { template: '<header><slot /></header>' },
  NvDialogTitle: { template: '<h2><slot /></h2>' },
}
const checkboxStubs = {
  NvCheckbox: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template:
      '<input type="checkbox" :checked="modelValue" @change="$emit(\'update:modelValue\', $event.target.checked)" />',
  },
}
const tableStub = {
  NvDataTable: {
    props: ['rows', 'totalItems'],
    template: `
      <section class="data-table" :data-total="totalItems">
        <slot v-if="rows.length === 0" name="empty" />

        <article v-for="row in rows" :key="(row.purchaseOrderNo || row.requisitionNo) + '-' + (row.lineNo || row.purchaseRequisitionId)">
          <span>{{ row.purchaseOrderNo || row.requisitionNo }}</span>
          <span>{{ row.lineNo }}</span>
          <span>{{ row.skuCode }}</span>
          <slot name="cell-actions" :row="row" />
          <slot name="cell-openQuantity" :row="row">{{ row.openQuantity }}</slot>
        </article>
      </section>
    `,
  },
}

const globalStubs = {
  ...layoutStub,
  ...selectStubs,
  ...dialogStubs,
  ...checkboxStubs,
  ...tableStub,
}

beforeEach(() => {
  vi.restoreAllMocks()
  purchaseOrderFilters.status = undefined
  purchaseOrderFilters.keyword = undefined
  purchaseOrderFilters.skip = 0
  purchaseOrderFilters.take = 10
  receiptFilters.status = undefined
  receiptFilters.keyword = undefined
  receiptFilters.skip = 0
  receiptFilters.take = 10
  state.canManagePurchaseOrders = false
  state.canReadMrp = false

  state.revisePurchaseOrder.mockReset()
  state.purchaseOrdersTotal = 42
  state.purchaseRequisitionsTotal = 9
  state.receiptsTotal = 7
  state.convertPurchaseRequisition.mockReset()
  state.convertPurchaseRequisition.mockResolvedValue({
    success: true,
    data: { status: 'PurchaseOrderCreated', purchaseOrderNo: 'PO-REQ-001' },
  })
  state.supplierPartners = [
    {
      resourceType: 'business-partner',
      code: 'SUP-001',
      displayName: '第一供应商',
      active: true,
      partnerType: 'supplier',
    },
    {
      resourceType: 'business-partner',
      code: 'SUP-002',
      displayName: '第二供应商',
      active: true,
      partnerType: 'supplier',
    },
    {
      resourceType: 'business-partner',
      code: 'CUS-001',
      displayName: '客户一号',
      active: true,
      partnerType: 'customer',
    },
  ]
  state.purchaseRequisitions = [
    {
      purchaseRequisitionId: 'pr-001',
      requisitionNo: 'PR-001',
      suggestionId: 'suggestion-001',
      skuCode: 'SKU-RM-001',
      uomCode: 'EA',
      siteCode: 'SITE-01',
      quantity: 5,
      status: 'Open',
    },
    {
      purchaseRequisitionId: 'pr-002',
      requisitionNo: 'PR-002',
      suggestionId: 'suggestion-002',
      skuCode: 'SKU-RM-002',
      uomCode: 'EA',
      siteCode: 'SITE-01',
      quantity: 3,
      status: 'Converted',
      convertedPurchaseOrderNo: 'PO-OLD-001',
    },
  ]
  state.purchaseOrders = [
    {
      purchaseOrderNo: 'PO-001',
      supplierCode: 'SUP-001',
      siteCode: 'SITE-01',
      status: 'Released',
      receiptReadiness: 'Partial',
      lines: [
        {
          lineNo: '10',
          skuCode: 'SKU-RM-001',
          orderedQuantity: 5,
          receivedQuantity: 2,
          unitPrice: 10,
        },
        {
          lineNo: '20',
          skuCode: 'SKU-RM-002',
          orderedQuantity: 2,
          receivedQuantity: 5,
          unitPrice: 6,
        },
      ],
    },
  ]
  state.receipts = [
    {
      purchaseOrderNo: 'PO-002',
      supplierCode: 'SUP-002',
      siteCode: 'SITE-02',
      status: 'Released',
      receiptReadiness: 'Open',
      lines: [
        { lineNo: '10', skuCode: 'SKU-RM-003', orderedQuantity: 8, receivedQuantity: 3 },
        { lineNo: '20', skuCode: 'SKU-RM-004', orderedQuantity: 1, receivedQuantity: 4 },
      ],
    },
  ]
})

describe('ERP procurement purchase requisition page', () => {
  it('offers conversion only for open requisitions and calls the conversion action once', async () => {
    const wrapper = mount(PurchaseRequisitionsPage, { global: { stubs: globalStubs } })
    await flushPromises()

    expect(wrapper.get('.data-table').attributes('data-total')).toBe('9')
    expect(wrapper.text()).toContain('PR-001')
    expect(wrapper.text()).toContain('PR-002')
    const convertButtons = wrapper
      .findAll('button')
      .filter((button) => button.text().includes('转采购订单'))
    expect(convertButtons).toHaveLength(1)

    await convertButtons[0]!.trigger('click')
    await flushPromises()

    expect(state.convertPurchaseRequisition).toHaveBeenCalledWith(['PR-001'])
  })

  it('starts RFQ conversion from selected supplier candidates', async () => {
    const promptSpy = vi.spyOn(window, 'prompt')
    state.convertPurchaseRequisition.mockResolvedValue({
      success: true,
      data: { status: 'RfqCreated', rfqNo: 'RFQ-REQ-001' },
    })
    const wrapper = mount(PurchaseRequisitionsPage, { global: { stubs: globalStubs } })
    await flushPromises()

    const rfqButton = wrapper.findAll('button').find((button) => button.text().includes('发起 RFQ'))
    expect(rfqButton).toBeTruthy()

    await rfqButton!.trigger('click')
    await flushPromises()
    expect(promptSpy).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('选择询价供应商')
    expect(wrapper.text()).toContain('第一供应商')
    expect(wrapper.text()).toContain('第二供应商')
    expect(wrapper.text()).not.toContain('客户一号')

    const supplierCheckboxes = wrapper.findAll('input[type="checkbox"]')
    await supplierCheckboxes[0]!.setValue(true)
    await supplierCheckboxes[1]!.setValue(true)
    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('生成 RFQ'))!
      .trigger('click')
    await flushPromises()

    expect(state.convertPurchaseRequisition).toHaveBeenCalledWith(['PR-001'], {
      rfqSupplierCodes: ['SUP-001', 'SUP-002'],
    })
  })
})

describe('ERP procurement purchase order page', () => {
  it('restores purchase order status filtering with backend status values', async () => {
    const wrapper = mount(PurchaseOrdersPage, { global: { stubs: globalStubs } })
    await flushPromises()

    const select = wrapper.get('select')
    expect(new Set(select.findAll('option').map((o) => o.attributes('value')))).toEqual(
      new Set(['all', 'Released', 'Closed', 'Cancelled']),
    )

    await select.setValue('Released')
    expect(purchaseOrderFilters.status).toBe('Released')

    await select.setValue('all')
    expect(purchaseOrderFilters.status).toBeUndefined()
  })

  it('keeps order-level total while rendering line rows and clamps open quantity at zero', async () => {
    const wrapper = mount(PurchaseOrdersPage, { global: { stubs: globalStubs } })
    await flushPromises()

    expect(wrapper.get('.data-table').attributes('data-total')).toBe('42')
    expect(wrapper.text()).toContain('PO-001')
    expect(wrapper.text()).toContain('SKU-RM-001')
    expect(wrapper.text()).toContain('3')
    expect(wrapper.text()).not.toContain('-3')
  })
})

describe('ERP procurement receipt page', () => {
  it('keeps purchase-order-source total while rendering receivable lines and clamps open quantity at zero', async () => {
    const wrapper = mount(ReceiptsPage, { global: { stubs: globalStubs } })
    await flushPromises()

    expect(wrapper.get('.data-table').attributes('data-total')).toBe('7')
    expect(wrapper.text()).toContain('PO-002')
    expect(wrapper.text()).toContain('SKU-RM-003')
    expect(wrapper.text()).toContain('5')
    expect(wrapper.text()).not.toContain('-3')
  })

  // #1345：qualityStatus 是 ERP 收货命令必填字段，表单必须带出质检状态并随行提交。
  // #3900：直接过账必须带收货库位；库位候选按采购单的工厂收窄。
  const receiptLocationStub = {
    DirectoryPicker: {
      props: ['modelValue', 'id', 'formSiteCode'],
      emits: ['update:modelValue'],
      template:
        '<input :id="id" :data-form-site-code="formSiteCode" :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />',
    },
  }

  it('submits the receipt with the selected quality status and receiving location, defaulting to pending inspection', async () => {
    state.recordPurchaseReceipt
      .mockRejectedValueOnce(
        Object.assign(new Error('response lost'), { name: 'RequestTimeoutError' }),
      )
      .mockResolvedValue({
        purchaseReceiptId: 'receipt-id-1',
        purchaseReceiptNo: 'PR-20260928-000001',
      })
    const wrapper = mount(ReceiptsPage, {
      global: { stubs: { ...globalStubs, ...receiptLocationStub } },
    })
    await flushPromises()

    const receiveButton = wrapper
      .findAll('button')
      .find((button) => button.text().includes('登记收货'))
    expect(receiveButton).toBeTruthy()
    await receiveButton!.trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('质检状态')
    const select = wrapper.get('select')
    expect(new Set(select.findAll('option').map((o) => o.attributes('value')))).toEqual(
      new Set(['quality', 'unrestricted', 'blocked']),
    )
    expect((select.element as HTMLSelectElement).value).toBe('quality')
    const location = wrapper.get('#erp-receipt-location')
    expect(location.attributes('data-form-site-code')).toBe('SITE-02')

    // 没选库位不提交，并提示原因。
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(state.recordPurchaseReceipt).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('请选择收货库位。')

    await location.setValue('loc-raw-01')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(state.notifyWarning).toHaveBeenCalledWith(
      '收货结果暂未确认，请保持原填写内容并再次确认收货。',
    )
    expect(state.notifySuccess).not.toHaveBeenCalled()
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(state.recordPurchaseReceipt.mock.calls[1]).toEqual(
      state.recordPurchaseReceipt.mock.calls[0],
    )

    expect(state.recordPurchaseReceipt).toHaveBeenCalledWith({
      purchaseOrderNo: 'PO-002',
      lines: [
        {
          purchaseOrderLineNo: '10',
          receivedQuantity: 5,
          qualityStatus: 'quality',
          locationCode: 'loc-raw-01',
        },
      ],
    })
    // 我方收货单号由编码规则生成，收货成功后回显给用户。
    expect(state.notifySuccess).toHaveBeenCalledWith(
      'PO-002 第 10 行已收货，收货单号 PR-20260928-000001',
    )

    await receiveButton!.trigger('click')
    await wrapper.get('select').setValue('unrestricted')
    await wrapper.get('#erp-receipt-location').setValue('loc-raw-01')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(state.recordPurchaseReceipt).toHaveBeenLastCalledWith({
      purchaseOrderNo: 'PO-002',
      lines: [
        {
          purchaseOrderLineNo: '10',
          receivedQuantity: 5,
          qualityStatus: 'unrestricted',
          locationCode: 'loc-raw-01',
        },
      ],
    })
  })
})

it.each([true, false])(
  '只允许有采购维护权限的用户从待修订原单重提（权限 %s）',
  async (canManage) => {
    state.canManagePurchaseOrders = canManage
    state.purchaseOrders = [
      {
        purchaseOrderNo: 'PO-REV-001',
        status: 'PendingApproval',
        canReviseBeforeApproval: true,
        lines: [
          {
            lineNo: '10',
            skuCode: 'STEEL-14',
            orderedQuantity: 4,
            unitPrice: 12,
            promisedDate: '2026-12-01',
          },
        ],
      },
      {
        purchaseOrderNo: 'PO-ACTIVE-001',
        status: 'PendingApproval',
        canReviseBeforeApproval: false,
        approvalChainId: 'chain-active',
        lines: [{ lineNo: '10', skuCode: 'STEEL-14' }],
      },
    ]
    state.revisePurchaseOrder.mockResolvedValue({ approvalChainId: 'chain-revised' })
    const wrapper = mount(PurchaseOrdersPage, { global: { stubs: globalStubs } })
    const actions = wrapper.findAll('button').filter((button) => button.text() === '修订并重提')
    expect(actions).toHaveLength(canManage ? 1 : 0)
    if (canManage) {
      await actions[0]!.trigger('click')
      await wrapper.get('#revision-quantity-0').setValue(6)
      await wrapper.get('.dialog form').trigger('submit')
      await flushPromises()
      expect(state.revisePurchaseOrder).toHaveBeenCalledWith('PO-REV-001', [
        { lineNo: '10', orderedQuantity: 6, unitPrice: 12, promisedDate: '2026-12-01' },
      ])
    }
    wrapper.unmount()
  },
)

it('待审批采购订单有待收量时显示原因且无登记入口', async () => {
  state.receipts = [{ ...state.receipts[0], status: 'PendingApproval' }]
  const wrapper = mount(ReceiptsPage, { global: { stubs: globalStubs } })
  await flushPromises()
  expect(wrapper.text()).toContain('待审批，审批通过后可登记')
  expect(wrapper.findAll('button').some((button) => button.text() === '登记收货')).toBe(false)
  expect(state.recordPurchaseReceipt).not.toHaveBeenCalled()
})

it.each([true, false])('采购申请真实空态按 MRP 权限提供下一步（读权 %s）', async (canReadMrp) => {
  state.canReadMrp = canReadMrp
  state.purchaseRequisitions = []
  state.purchaseRequisitionsTotal = 0
  const wrapper = mount(PurchaseRequisitionsPage, { global: { stubs: globalStubs } })
  await flushPromises()
  expect(wrapper.text()).toContain(canReadMrp ? '查看采购类 MRP 建议' : '由计划员查看和处理')
  expect(wrapper.text().includes('查看采购类 MRP 建议')).toBe(canReadMrp)
})
