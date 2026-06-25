import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import FinancePage from './finance.vue'

// 受控读面数据：每个用例前重置。
const state = vi.hoisted(() => ({
  receivables: [] as Array<Record<string, unknown>>,
  payables: [] as Array<Record<string, unknown>>,
  vouchers: [] as Array<Record<string, unknown>>,
  costCandidates: [] as Array<Record<string, unknown>>,
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
  useErpFinanceSummary: () => ({
    summary: computed(() => ({ openReceivableAmount: 0, openPayableAmount: 0, costCandidateAmount: 0, postedVoucherCount: 0 })),
    summaryError: shallowRef(undefined),
    summaryPending: shallowRef(false),
    refreshSummary: vi.fn(),
  }),
  useErpReceivables: () => ({
    ...listShape(() => state.receivables),
    createReceivable: vi.fn(),
    createReceivablePending: shallowRef(false),
    createReceivableError: shallowRef(undefined),
  }),
  useErpPayables: () => ({
    ...listShape(() => state.payables),
    createPayable: vi.fn(),
    createPayablePending: shallowRef(false),
    createPayableError: shallowRef(undefined),
  }),
  useErpJournalVouchers: () => ({
    ...listShape(() => state.vouchers),
    postVoucher: vi.fn(),
    postVoucherPending: shallowRef(false),
    postVoucherError: shallowRef(undefined),
  }),
  useErpCostCandidates: () => ({
    ...listShape(() => state.costCandidates),
    createCostCandidate: vi.fn(),
    createCostCandidatePending: shallowRef(false),
    createCostCandidateError: shallowRef(undefined),
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
// reka-ui Select 换成原生 <select>/<option>，让测试能枚举状态选项的 value 集合。
const selectStubs = {
  SelectPro: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template: '<select :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)"><slot /></select>',
  },
  SelectProTrigger: { template: '<span><slot /></span>' },
  SelectValue: { template: '<span />' },
  SelectProContent: { template: '<slot />' },
  SelectProItem: { props: ['value'], template: '<option :value="value"><slot /></option>' },
}

// reka-ui Tabs 同时渲染全部 panel（仅 data-state 区分），断言须取「当前激活」panel。
function activePanel(wrapper: ReturnType<typeof mount>) {
  return wrapper.find('[role="tabpanel"][data-state="active"]')
}

// 切到指定 Tab（reka-ui Tabs 用 focus + mousedown 激活）。
async function switchTab(wrapper: ReturnType<typeof mount>, label: string) {
  const tab = wrapper.findAll('[role="tab"]').find((t) => t.text().includes(label))!
  await tab.trigger('focus')
  await tab.trigger('mousedown')
  await flushPromises()
}

// 状态筛选 Select 用 'all' 作「全部状态」哨兵 option；分页页大小下拉无此哨兵，
// 故以「是否含 value=all 的 option」结构化识别状态筛选 <select>（aria-label 在被 stub 的
// SelectTrigger 上、不在 <select> 元素上，不能据此匹配）。
function statusFilterSelects(panel: ReturnType<ReturnType<typeof mount>['find']>) {
  return panel
    .findAll('select')
    .filter((s) => s.findAll('option').some((o) => o.attributes('value') === 'all'))
}

function statusFilterOptionValues(panel: ReturnType<ReturnType<typeof mount>['find']>) {
  const select = statusFilterSelects(panel)[0]
  if (!select) return undefined
  return select.findAll('option').map((o) => o.attributes('value'))
}

beforeEach(() => {
  state.receivables = []
  state.payables = []
  state.vouchers = []
  state.costCandidates = []
})

describe('ERP finance page — 会计凭证 Tab 无状态筛选（reversed 幻值已移除）', () => {
  it('凭证 Tab 只保留关键字筛选，无状态 Select', async () => {
    const wrapper = mount(FinancePage, { global: { stubs: { ...layoutStub, ...selectStubs } } })
    await flushPromises()
    await switchTab(wrapper, '会计凭证')

    const panel = activePanel(wrapper)
    expect(panel.find('[aria-label="凭证关键字"]').exists()).toBe(true)
    expect(statusFilterSelects(panel)).toHaveLength(0)
  })
})

describe('ERP finance page — 成本候选 Tab 无状态筛选（posted 幻值已移除）', () => {
  it('成本候选 Tab 只保留关键字筛选，无状态 Select（来源类型下拉在对话框内、无 all 哨兵，不计入）', async () => {
    const wrapper = mount(FinancePage, { global: { stubs: { ...layoutStub, ...selectStubs } } })
    await flushPromises()
    await switchTab(wrapper, '成本候选')

    const panel = activePanel(wrapper)
    expect(panel.find('[aria-label="成本候选关键字"]').exists()).toBe(true)
    expect(statusFilterSelects(panel)).toHaveLength(0)
  })
})

describe('ERP finance page — 应收/应付仍保留 open/settled 状态筛选（后端真支持）', () => {
  it('应收 Tab 状态 Select == {all, open, settled}', async () => {
    const wrapper = mount(FinancePage, { global: { stubs: { ...layoutStub, ...selectStubs } } })
    await flushPromises()
    // 应收为默认 Tab，但显式切换以稳态。
    await switchTab(wrapper, '应收账款')

    const panel = activePanel(wrapper)
    const values = statusFilterOptionValues(panel)
    expect(values).toBeDefined()
    expect(new Set(values)).toEqual(new Set(['all', 'open', 'settled']))
    // 中文文案为 未结/已结清，无幻值。
    const select = statusFilterSelects(panel)[0]!
    const texts = select.findAll('option').map((o) => o.text())
    expect(texts).toContain('未结')
    expect(texts).toContain('已结清')
  })

  it('应付 Tab 状态 Select == {all, open, settled}', async () => {
    const wrapper = mount(FinancePage, { global: { stubs: { ...layoutStub, ...selectStubs } } })
    await flushPromises()
    await switchTab(wrapper, '应付账款')

    const panel = activePanel(wrapper)
    const values = statusFilterOptionValues(panel)
    expect(values).toBeDefined()
    expect(new Set(values)).toEqual(new Set(['all', 'open', 'settled']))
  })
})
