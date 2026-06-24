import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import SalesPage from './sales.vue'

// 受控读面数据：每个 describe 用例前重置，避免跨用例污染。
const state = vi.hoisted(() => ({
  quotations: [] as Array<Record<string, unknown>>,
  deliveries: [] as Array<Record<string, unknown>>,
  opportunities: [] as Array<Record<string, unknown>>,
  salesOrders: [] as Array<Record<string, unknown>>,
}))

function listShape(itemsRef: () => Array<Record<string, unknown>>) {
  return {
    filters: reactive({ status: undefined as string | undefined, keyword: undefined as string | undefined, skip: 0, take: 10 }),
    items: computed(() => itemsRef()),
    total: computed(() => itemsRef().length),
    organizationId: computed(() => 'org-001'),
    environmentId: computed(() => 'env-dev'),
    error: shallowRef(undefined),
    pending: shallowRef(false),
    refresh: vi.fn(),
  }
}

vi.mock('@/composables/useBusinessErp', () => ({
  useErpOpportunities: () => ({
    ...listShape(() => state.opportunities),
    openOpportunity: vi.fn(),
    openOpportunityPending: shallowRef(false),
    openOpportunityError: shallowRef(undefined),
  }),
  useErpQuotations: () => ({
    ...listShape(() => state.quotations),
    approveQuotation: vi.fn(),
    approveQuotationPending: shallowRef(false),
    approveQuotationError: shallowRef(undefined),
    createQuotation: vi.fn(),
    createQuotationPending: shallowRef(false),
    createQuotationError: shallowRef(undefined),
  }),
  useErpSalesOrders: () => {
    const base = listShape(() => state.salesOrders)
    return {
      filters: base.filters,
      salesOrders: base.items,
      salesOrdersTotal: base.total,
      salesOrdersError: shallowRef(undefined),
      salesOrdersPending: shallowRef(false),
      refreshSalesOrders: vi.fn(),
      createSalesOrder: vi.fn(),
      createSalesOrderPending: shallowRef(false),
      createSalesOrderError: shallowRef(undefined),
    }
  },
  useErpDeliveryOrders: () => listShape(() => state.deliveries),
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
// reka-ui Select 换成原生 <select>/<option>，让测试能枚举状态选项的 value 集合。
const selectStubs = {
  Select: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template: '<select :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)"><slot /></select>',
  },
  SelectTrigger: { template: '<span><slot /></span>' },
  SelectValue: { template: '<span />' },
  SelectContent: { template: '<slot />' },
  SelectItem: { props: ['value'], template: '<option :value="value"><slot /></option>' },
}
// RowActions 下拉默认 portal 渲染，stub 成就地容器，让审批触发器进入 DOM 可被断言。
const rowActionStubs = {
  RowActions: { template: '<div data-testid="row-actions"><slot /></div>' },
  DropdownMenuItem: { emits: ['click'], template: '<button type="button" @click="$emit(\'click\', $event)"><slot /></button>' },
}

// 切到指定 Tab（reka-ui Tabs 用 focus + mousedown 激活）。
async function switchTab(wrapper: ReturnType<typeof mount>, label: string) {
  const tab = wrapper.findAll('[role="tab"]').find((t) => t.text().includes(label))!
  await tab.trigger('focus')
  await tab.trigger('mousedown')
  await flushPromises()
}

// reka-ui Tabs 同时渲染全部 panel（仅 data-state 区分），断言须取「当前激活」panel。
function activePanel(wrapper: ReturnType<typeof mount>) {
  return wrapper.find('[role="tabpanel"][data-state="active"]')
}

// 状态筛选 Select 用 'all' 作「全部状态」哨兵 option；分页页大小下拉无此哨兵，
// 故以「是否含 value=all 的 option」结构化识别状态筛选 <select>（aria-label 落在被 stub 的
// SelectTrigger 上、不在 <select> 元素上，不能据此匹配）。
function statusFilterSelects(panel: ReturnType<ReturnType<typeof mount>['find']>) {
  return panel
    .findAll('select')
    .filter((s) => s.findAll('option').some((o) => o.attributes('value') === 'all'))
}

// 取状态筛选 <select> 的全部 option value；无状态筛选时返回 undefined。
function statusFilterOptionValues(panel: ReturnType<ReturnType<typeof mount>['find']>) {
  const select = statusFilterSelects(panel)[0]
  if (!select) return undefined
  return select.findAll('option').map((o) => o.attributes('value'))
}

beforeEach(() => {
  state.opportunities = []
  state.salesOrders = []
  state.deliveries = []
  state.quotations = []
})

describe('ERP sales page — 报价状态筛选收敛到后端真值', () => {
  it('报价状态 Select 仅含后端真值 Draft/Approved/Rejected/Expired（含 all 哨兵），无 submitted/待审批 幻值', async () => {
    const wrapper = mount(SalesPage, { global: { stubs: { ...layoutStub, ...selectStubs } } })
    await flushPromises()
    await switchTab(wrapper, '报价单')

    const panel = activePanel(wrapper)
    const values = statusFilterOptionValues(panel)
    expect(values).toBeDefined()
    // value 集合（语义）== {all, Draft, Approved, Rejected, Expired}
    expect(new Set(values)).toEqual(new Set(['all', 'Draft', 'Approved', 'Rejected', 'Expired']))
    // 守住已被移除的幻值：不得出现 submitted（旧「待审批」状态）。
    expect(values).not.toContain('submitted')
    expect(values).not.toContain('delivered')
    // 状态筛选选项中文文案不应回归为「待审批」（报价用「待审」对应 Draft）。
    const statusSelect = statusFilterSelects(panel)[0]!
    const optionTexts = statusSelect.findAll('option').map((o) => o.text())
    expect(optionTexts).toContain('待审')
    expect(optionTexts).not.toContain('待审批')
  })
})

describe('ERP sales page — 报价审批门禁（仅 Draft 可审批）', () => {
  it('Draft 行渲染审批动作；Approved 行渲染占位 — 且无审批触发器', async () => {
    state.quotations = [
      { quotationNo: 'QUO-DRAFT-1', customerCode: 'CUST-A', status: 'Draft', totalAmount: 1000, expiresOn: '2026-12-31' },
      { quotationNo: 'QUO-APPROVED-1', customerCode: 'CUST-B', status: 'Approved', totalAmount: 2000, expiresOn: '2026-12-31' },
    ]
    const wrapper = mount(SalesPage, { global: { stubs: { ...layoutStub, ...rowActionStubs } } })
    await flushPromises()
    await switchTab(wrapper, '报价单')

    const panel = activePanel(wrapper)
    // 仅 Draft 行渲染 RowActions（审批入口）→ 报价 Tab 恰好 1 个。
    const rowActions = panel.findAll('[data-testid="row-actions"]')
    expect(rowActions).toHaveLength(1)
    // 审批触发器唯一，文案为「审批通过」。
    const approveTriggers = panel.findAll('button').filter((b) => b.text().includes('审批通过'))
    expect(approveTriggers).toHaveLength(1)
    // Approved 行落到 v-else 占位 —。
    expect(panel.text()).toContain('—')
    // 两行均出现在表中（确认 Approved 行确实被渲染，只是无审批入口）。
    expect(panel.text()).toContain('QUO-DRAFT-1')
    expect(panel.text()).toContain('QUO-APPROVED-1')
  })

  it('全部为 Approved 时无任何审批入口', async () => {
    state.quotations = [
      { quotationNo: 'QUO-APPROVED-2', customerCode: 'CUST-C', status: 'Approved', totalAmount: 3000, expiresOn: '2026-12-31' },
    ]
    const wrapper = mount(SalesPage, { global: { stubs: { ...layoutStub, ...rowActionStubs } } })
    await flushPromises()
    await switchTab(wrapper, '报价单')

    const panel = activePanel(wrapper)
    expect(panel.findAll('[data-testid="row-actions"]')).toHaveLength(0)
    expect(panel.findAll('button').filter((b) => b.text().includes('审批通过'))).toHaveLength(0)
  })
})

describe('ERP sales page — 销售订单/发货单 Tab 无状态筛选', () => {
  it('销售订单 Tab 只保留关键字筛选，无状态 Select（旧 open/released 幻值已移除）', async () => {
    const wrapper = mount(SalesPage, { global: { stubs: { ...layoutStub, ...selectStubs } } })
    await flushPromises()
    await switchTab(wrapper, '销售订单')

    const panel = activePanel(wrapper)
    // 关键字筛选仍在。
    expect(panel.find('[aria-label="销售订单关键字"]').exists()).toBe(true)
    // 不渲染状态筛选 Select（分页页大小下拉无 all 哨兵，不计入）。
    expect(statusFilterSelects(panel)).toHaveLength(0)
  })

  it('发货单 Tab 只保留关键字筛选，无状态 Select（旧 delivered 幻值已移除）', async () => {
    const wrapper = mount(SalesPage, { global: { stubs: { ...layoutStub, ...selectStubs } } })
    await flushPromises()
    await switchTab(wrapper, '发货单')

    const panel = activePanel(wrapper)
    expect(panel.find('[aria-label="发货单关键字"]').exists()).toBe(true)
    expect(statusFilterSelects(panel)).toHaveLength(0)
  })
})

describe('ERP sales page — 待审报价 KPI 只数 Draft', () => {
  it('1 张 Draft + 1 张 Approved → 待审报价 KPI == 1', async () => {
    state.quotations = [
      { quotationNo: 'QUO-D', customerCode: 'CUST-A', status: 'Draft', totalAmount: 100, expiresOn: '2026-12-31' },
      { quotationNo: 'QUO-A', customerCode: 'CUST-B', status: 'Approved', totalAmount: 200, expiresOn: '2026-12-31' },
    ]
    const wrapper = mount(SalesPage, { global: { stubs: { ...layoutStub } } })
    await flushPromises()

    // 定位「待审报价」KPI 卡，断其展示值为 1（而非 2 = 总数）。
    const card = wrapper.findAll('*').find((el) => el.text().includes('待审报价') && el.classes().some((c) => c.includes('card')))
    // 退路：若无法用 class 精确定位卡片，则断整页文案含独立的 KPI 标签与值组合。
    const text = (card ?? wrapper).text()
    expect(text).toContain('待审报价')
    // KPI 值为 1（Draft 计数），不等于报价总数 2。
    expect(text).toMatch(/待审报价[^0-9]*1/)
  })
})
