import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'
import { createPinia } from 'pinia'
import { useAuthStore } from '@/stores/auth'
import ApprovalPage from './index.vue'

const approvalState = vi.hoisted(() => ({
  chainFilters: {
    status: undefined as string | undefined,
    startedBy: undefined as string | undefined,
    sourceService: undefined as string | undefined,
    documentType: undefined as string | undefined,
    documentId: undefined as string | undefined,
    skip: 0,
    take: 10,
  },
  chains: [
    {
      chainId: 'chain-1',
      status: 'Running',
      documentType: '采购订单',
      documentId: 'PO-260701-001',
      templateCode: 'purchase-order',
    },
  ],
  createDelegation: vi.fn(async () => undefined),
  decisionFilters: {
    chainId: undefined as string | undefined,
    actorType: undefined as string | undefined,
    actorRef: undefined as string | undefined,
    decision: undefined as string | undefined,
    documentType: undefined as string | undefined,
    documentId: undefined as string | undefined,
    skip: 0,
    take: 10,
  },
  decisions: [
    {
      decisionId: 'decision-1',
      chainId: 'chain-1',
      decision: 'Approve',
      actorRef: 'manager-a',
      documentType: '采购订单',
      documentId: 'PO-260701-001',
    },
  ],
  delegationFilters: {
    status: 'Active' as string | undefined,
    delegatorActorRef: undefined as string | undefined,
    delegateActorRef: undefined as string | undefined,
    documentType: undefined as string | undefined,
    skip: 0,
    take: 10,
  },
  delegations: [
    {
      delegationId: 'delegation-1',
      status: 'Active',
      delegatorActorRef: 'manager-a',
      delegateActorRef: 'manager-b',
      documentType: '采购订单',
    },
  ],
  resolveTask: vi.fn(async () => undefined),
  revokeDelegation: vi.fn(async () => undefined),
  templateFilters: {
    documentType: undefined as string | undefined,
    isActive: undefined as boolean | undefined,
    skip: 0,
    take: 10,
  },
  templates: [
    {
      templateId: 'template-1',
      templateCode: 'purchase-order',
      documentType: '采购订单',
      version: 1,
      isActive: true,
      steps: [],
    },
  ],
}))

vi.mock('@/composables/useBusinessApproval', () => ({
  useBusinessApproval: () => ({
    chainDetail: computed(() => undefined),
    chainDetailSelection: reactive({ chainId: '' }),
    chains: computed(() => approvalState.chains),
    chainsPending: shallowRef(false),
    chainsTotal: computed(() => approvalState.chains.length),
    chainsError: shallowRef(undefined),
    chainFilters: reactive(approvalState.chainFilters),
    createDelegation: approvalState.createDelegation,
    createDelegationError: shallowRef(undefined),
    createDelegationPending: shallowRef(false),
    decisions: computed(() => approvalState.decisions),
    decisionsPending: shallowRef(false),
    decisionsTotal: computed(() => approvalState.decisions.length),
    decisionsError: shallowRef(undefined),
    decisionFilters: reactive(approvalState.decisionFilters),
    delegations: computed(() => approvalState.delegations),
    delegationsPending: shallowRef(false),
    delegationsTotal: computed(() => approvalState.delegations.length),
    delegationsError: shallowRef(undefined),
    delegationFilters: reactive(approvalState.delegationFilters),
    refreshAll: vi.fn(),
    resolveTask: approvalState.resolveTask,
    resolveTaskError: shallowRef(undefined),
    resolveTaskPending: shallowRef(false),
    revokeDelegation: approvalState.revokeDelegation,
    revokeDelegationError: shallowRef(undefined),
    revokeDelegationPending: shallowRef(false),
    saveTemplate: vi.fn(),
    saveTemplateError: shallowRef(undefined),
    saveTemplatePending: shallowRef(false),
    tasks: computed(() => [
      {
        chainId: 'chain-1',
        stepNo: 10,
        stepName: '采购经理审批',
        documentType: '采购订单',
        documentId: 'PO-260701-001',
      },
    ]),
    tasksPending: shallowRef(false),
    tasksTotal: computed(() => 1),
    tasksError: shallowRef(undefined),
    taskFilters: reactive({ skip: 0, take: 10 }),
    templates: computed(() => approvalState.templates),
    templatesPending: shallowRef(false),
    templatesTotal: computed(() => approvalState.templates.length),
    templatesError: shallowRef(undefined),
    templateFilters: reactive(approvalState.templateFilters),
  }),
}))

const tableStub = {
  props: ['emptyMessage', 'page', 'rows'],
  emits: ['update:page', 'update:page-size'],
  template: `
    <section
      data-testid="data-table"
      :data-empty-message="emptyMessage"
      :data-page="page"
    >
      <p v-if="rows.length === 0">{{ emptyMessage }}</p>
      <button
        type="button"
        aria-label="将当前列表切换到第 3 页"
        @click="$emit('update:page', 3)"
      >
        第 3 页
      </button>
      <div v-for="row in rows" :key="row.chainId || row.delegationId || row.templateId || row.decisionId" data-testid="row">
        <span>{{ row.documentId || row.templateCode || row.delegationId || row.decisionId }}</span>
        <slot name="cell-actions" :row="row" />
      </div>
    </section>
  `,
}

const tabsStubs = {
  NvTabs: { template: '<section><slot /></section>' },
  NvTabsList: { template: '<nav><slot /></nav>' },
  NvTabsTrigger: { props: ['value'], template: '<button type="button"><slot /></button>' },
  NvTabsContent: { props: ['value'], template: '<section><slot /></section>' },
}

const nativeSelectStub = {
  inheritAttrs: false,
  props: ['modelValue'],
  emits: ['update:modelValue'],
  template:
    '<select :value="modelValue" v-bind="$attrs" @change="$emit(\'update:modelValue\', $event.target.value)"><slot /></select>',
}
const selectTriggerStub = { inheritAttrs: false, template: '<slot />' }
const selectValueStub = { template: '<span />' }
const selectContentStub = { template: '<slot />' }
const selectItemStub = {
  props: ['value'],
  template: '<option :value="value"><slot /></option>',
}
const selectStubs = {
  NvSelect: nativeSelectStub,
  Select: nativeSelectStub,
  NvSelectTrigger: selectTriggerStub,
  SelectTrigger: selectTriggerStub,
  NvSelectValue: selectValueStub,
  SelectValue: selectValueStub,
  NvSelectContent: selectContentStub,
  SelectContent: selectContentStub,
  NvSelectItem: selectItemStub,
  SelectItem: selectItemStub,
}

function mountApproval(permissionCodes: string[], options: { emptyRecords?: boolean } = {}) {
  if (options.emptyRecords) {
    approvalState.chains = []
    approvalState.decisions = []
    approvalState.delegations = []
    approvalState.templates = []
  }

  const pinia = createPinia()
  const auth = useAuthStore(pinia)
  auth.$patch({
    principal: {
      principalId: 'manager-a',
      principalType: 'user',
      organizationId: 'org-001',
      environmentId: 'env-dev',
      loginName: 'manager-a',
      permissionCodes,
    },
  })

  return mount(ApprovalPage, {
    global: {
      plugins: [pinia],
      stubs: {
        BusinessLayout: { template: '<main><slot /></main>' },
        NvDataTable: tableStub,
        NvDialog: { props: ['open'], template: '<section v-if="open"><slot /></section>' },
        NvDialogClose: { template: '<span><slot /></span>' },
        NvDialogContent: { template: '<section><slot /></section>' },
        NvDialogDescription: { template: '<p><slot /></p>' },
        NvDialogFooter: { template: '<footer><slot /></footer>' },
        NvDialogHeader: { template: '<header><slot /></header>' },
        NvDialogTitle: { template: '<h2><slot /></h2>' },
        RowActions: { template: '<div data-testid="row-actions"><slot /></div>' },
        NvDropdownMenuItem: {
          emits: ['click'],
          template: '<button type="button" @click="$emit(\'click\', $event)"><slot /></button>',
        },
        PageHeader: {
          props: ['title'],
          template: '<header><h1>{{ title }}</h1><slot /><slot name="actions" /></header>',
        },
        SectionCard: true,
        SectionCards: { template: '<section><slot /></section>' },
        NvStatusBadge: { props: ['value'], template: '<span>{{ value }}</span>' },
        NvToolbar: {
          props: ['showSearch'],
          template: '<div><slot name="filters" /><slot name="actions" /></div>',
        },
        Toolbar: {
          props: ['showSearch'],
          template: '<div><slot name="filters" /><slot name="actions" /></div>',
        },
        ...selectStubs,
        ...tabsStubs,
      },
    },
  })
}

beforeEach(() => {
  Object.assign(approvalState.chainFilters, {
    status: undefined,
    startedBy: undefined,
    sourceService: undefined,
    documentType: undefined,
    documentId: undefined,
    skip: 0,
    take: 10,
  })
  Object.assign(approvalState.decisionFilters, {
    chainId: undefined,
    actorType: undefined,
    actorRef: undefined,
    decision: undefined,
    documentType: undefined,
    documentId: undefined,
    skip: 0,
    take: 10,
  })
  Object.assign(approvalState.delegationFilters, {
    status: 'Active',
    delegatorActorRef: undefined,
    delegateActorRef: undefined,
    documentType: undefined,
    skip: 0,
    take: 10,
  })
  Object.assign(approvalState.templateFilters, {
    documentType: undefined,
    isActive: undefined,
    skip: 0,
    take: 10,
  })
  approvalState.chains = [
    {
      chainId: 'chain-1',
      status: 'Running',
      documentType: '采购订单',
      documentId: 'PO-260701-001',
      templateCode: 'purchase-order',
    },
  ]
  approvalState.decisions = [
    {
      decisionId: 'decision-1',
      chainId: 'chain-1',
      decision: 'Approve',
      actorRef: 'manager-a',
      documentType: '采购订单',
      documentId: 'PO-260701-001',
    },
  ]
  approvalState.delegations = [
    {
      delegationId: 'delegation-1',
      status: 'Active',
      delegatorActorRef: 'manager-a',
      delegateActorRef: 'manager-b',
      documentType: '采购订单',
    },
  ]
  approvalState.templates = [
    {
      templateId: 'template-1',
      templateCode: 'purchase-order',
      documentType: '采购订单',
      version: 1,
      isActive: true,
      steps: [],
    },
  ]
  approvalState.createDelegation.mockClear()
  approvalState.resolveTask.mockClear()
  approvalState.revokeDelegation.mockClear()
})

afterEach(() => {
  vi.useRealTimers()
})

const filterLabels = {
  chain: {
    status: '审批流程状态',
    startedBy: '审批流程发起人',
    sourceService: '审批流程来源服务',
    documentType: '审批流程单据类型',
    documentId: '审批流程单据编号',
  },
  decision: {
    chainId: '审批决策流程编号',
    actorType: '审批决策处理人类型',
    actorRef: '审批决策处理人',
    decision: '审批决策类型',
    documentType: '审批决策单据类型',
    documentId: '审批决策单据编号',
  },
  delegation: {
    status: '审批委托状态',
    delegatorActorRef: '审批委托委托人',
    delegateActorRef: '审批委托代理人',
    documentType: '审批委托单据范围',
  },
  template: {
    documentType: '审批模板单据类型',
    isActive: '审批模板状态',
  },
} as const

function filterControl(wrapper: ReturnType<typeof mountApproval>, label: string) {
  return wrapper.get(`[aria-label="${label}"]`)
}

async function flushFilterDebounce() {
  await vi.advanceTimersByTimeAsync(301)
  await flushPromises()
}

describe('approval center page permissions and actions', () => {
  it('renders task processing and delegation maintenance actions for approval managers', async () => {
    const wrapper = mountApproval(['business.approvals.read', 'business.approvals.manage'])
    await flushPromises()

    expect(wrapper.text()).toContain('审批中心')
    expect(wrapper.text()).toContain('PO-260701-001')

    const buttons = wrapper.findAll('button')
    const approve = buttons.find((button) => button.text().includes('通过'))!
    const revoke = buttons.find((button) => button.text().includes('撤销'))!
    await approve.trigger('click')
    await revoke.trigger('click')

    expect(approvalState.resolveTask).toHaveBeenCalledWith({
      chainId: 'chain-1',
      stepNo: 10,
      decision: 'Approve',
      comment: '',
    })
    expect(approvalState.revokeDelegation).toHaveBeenCalledWith('delegation-1')
  })

  it('keeps records visible but hides task/delegation actions without manage permission', async () => {
    const wrapper = mountApproval(['business.approvals.read'])
    await flushPromises()

    expect(wrapper.text()).toContain('PO-260701-001')
    expect(wrapper.findAll('button').some((button) => button.text().includes('通过'))).toBe(false)
    expect(wrapper.findAll('button').some((button) => button.text().includes('撤销'))).toBe(false)
    expect(wrapper.text()).toContain('没有审批处理权限')
  })

  it('converts delegation datetime-local values to UTC ISO strings before submit', async () => {
    const wrapper = mountApproval(['business.approvals.read', 'business.approvals.manage'])
    await flushPromises()

    const newDelegation = wrapper
      .findAll('button')
      .find((button) => button.text().includes('新建委托'))!
    await newDelegation.trigger('click')
    await wrapper.find('#approval-delegate').setValue('manager-c')
    await wrapper.find('#approval-delegation-from').setValue('2026-07-01T09:30')
    await wrapper.find('#approval-delegation-to').setValue('2026-07-03T18:45')
    const delegationForm = wrapper
      .findAll('form')
      .find((form) => form.find('#approval-delegate').exists())!
    await delegationForm.trigger('submit')

    expect(approvalState.createDelegation).toHaveBeenCalledWith(
      expect.objectContaining({
        delegatorActorRef: 'manager-a',
        delegateActorRef: 'manager-c',
        effectiveFromUtc: new Date('2026-07-01T09:30').toISOString(),
        effectiveToUtc: new Date('2026-07-03T18:45').toISOString(),
      }),
    )
  })
})

describe('approval center tab filter panels', () => {
  it('preserves the real Active delegation default while displaying the normalized selection', async () => {
    const wrapper = mountApproval(['business.approvals.read'])
    await flushPromises()

    expect(
      (filterControl(wrapper, filterLabels.delegation.status).element as HTMLSelectElement).value,
    ).toBe('active')
    expect(approvalState.delegationFilters.status).toBe('Active')
  })

  it('renders an accessible control for every supported facade filter field', () => {
    const wrapper = mountApproval(['business.approvals.read'])

    for (const labels of Object.values(filterLabels)) {
      for (const label of Object.values(labels)) {
        expect(filterControl(wrapper, label).attributes('aria-label')).toBe(label)
      }
    }
  })

  it('debounces and normalizes every free-text filter against the real filter objects', async () => {
    vi.useFakeTimers()
    const wrapper = mountApproval(['business.approvals.read'])

    await filterControl(wrapper, filterLabels.chain.startedBy).setValue('  manager-a')
    await vi.advanceTimersByTimeAsync(120)
    await filterControl(wrapper, filterLabels.chain.startedBy).setValue(' manager-b  ')
    await vi.advanceTimersByTimeAsync(299)
    expect(approvalState.chainFilters.startedBy).toBeUndefined()
    await vi.advanceTimersByTimeAsync(1)
    expect(approvalState.chainFilters.startedBy).toBe('manager-b')

    const textValues = [
      [filterLabels.chain.startedBy, '   '],
      [filterLabels.chain.sourceService, '  business-quality  '],
      [filterLabels.chain.documentType, '  质量处置单  '],
      [filterLabels.chain.documentId, '  NCR-260726-001  '],
      [filterLabels.decision.chainId, '  chain-260726-001  '],
      [filterLabels.decision.actorType, '  role  '],
      [filterLabels.decision.actorRef, '  quality-manager  '],
      [filterLabels.decision.documentType, '  质量处置单  '],
      [filterLabels.decision.documentId, '  NCR-260726-001  '],
      [filterLabels.delegation.delegatorActorRef, '  manager-a  '],
      [filterLabels.delegation.delegateActorRef, '  manager-b  '],
      [filterLabels.delegation.documentType, '  采购订单  '],
      [filterLabels.template.documentType, '  采购订单  '],
    ] as const
    for (const [label, value] of textValues) {
      await filterControl(wrapper, label).setValue(value)
    }
    await flushFilterDebounce()

    expect(approvalState.chainFilters).toMatchObject({
      startedBy: undefined,
      sourceService: 'business-quality',
      documentType: '质量处置单',
      documentId: 'NCR-260726-001',
    })
    expect(approvalState.decisionFilters).toMatchObject({
      chainId: 'chain-260726-001',
      actorType: 'role',
      actorRef: 'quality-manager',
      documentType: '质量处置单',
      documentId: 'NCR-260726-001',
    })
    expect(approvalState.delegationFilters).toMatchObject({
      delegatorActorRef: 'manager-a',
      delegateActorRef: 'manager-b',
      documentType: '采购订单',
    })
    expect(approvalState.templateFilters.documentType).toBe('采购订单')
  })

  it('maps every closed selection to its exact facade query value and clears all selections', async () => {
    const wrapper = mountApproval(['business.approvals.read'])

    for (const status of ['pending', 'approved', 'rejected', 'returned', 'withdrawn']) {
      await filterControl(wrapper, filterLabels.chain.status).setValue(status)
      expect(approvalState.chainFilters.status).toBe(status)
    }
    await filterControl(wrapper, filterLabels.chain.status).setValue('all')
    expect(approvalState.chainFilters.status).toBeUndefined()

    for (const decision of [
      'approve',
      'reject',
      'return',
      'withdraw',
      'resubmit',
      'add_signer',
      'transfer',
    ]) {
      await filterControl(wrapper, filterLabels.decision.decision).setValue(decision)
      expect(approvalState.decisionFilters.decision).toBe(decision)
    }
    await filterControl(wrapper, filterLabels.decision.decision).setValue('all')
    expect(approvalState.decisionFilters.decision).toBeUndefined()

    expect(approvalState.delegationFilters.status).toBe('Active')
    await filterControl(wrapper, filterLabels.delegation.status).setValue('revoked')
    expect(approvalState.delegationFilters.status).toBe('revoked')
    await filterControl(wrapper, filterLabels.delegation.status).setValue('active')
    expect(approvalState.delegationFilters.status).toBe('active')
    await filterControl(wrapper, filterLabels.delegation.status).setValue('all')
    expect(approvalState.delegationFilters.status).toBeUndefined()

    await filterControl(wrapper, filterLabels.template.isActive).setValue('true')
    expect(approvalState.templateFilters.isActive).toBe(true)
    await filterControl(wrapper, filterLabels.template.isActive).setValue('false')
    expect(approvalState.templateFilters.isActive).toBe(false)
    await filterControl(wrapper, filterLabels.template.isActive).setValue('all')
    expect(approvalState.templateFilters.isActive).toBeUndefined()
  })

  it('resets a tab pager to the first page when an effective filter changes', async () => {
    const wrapper = mountApproval(['business.approvals.read'])
    const chainTable = wrapper.get('[data-empty-message="当前没有审批流程。"]')

    await chainTable.get('[aria-label="将当前列表切换到第 3 页"]').trigger('click')
    await flushPromises()
    expect(approvalState.chainFilters.skip).toBe(20)
    expect(chainTable.attributes('data-page')).toBe('3')

    await filterControl(wrapper, filterLabels.chain.status).setValue('pending')
    await flushPromises()
    expect(approvalState.chainFilters.skip).toBe(0)
    expect(chainTable.attributes('data-page')).toBe('1')
  })

  it('clears every supported field and its page-owned adapter immediately', async () => {
    vi.useFakeTimers()
    const wrapper = mountApproval(['business.approvals.read'])

    for (const labels of Object.values(filterLabels)) {
      for (const label of Object.values(labels)) {
        const control = filterControl(wrapper, label)
        await control.setValue(
          control.element.tagName === 'SELECT'
            ? control.findAll('option')[1]!.attributes('value')
            : ` ${label} `,
        )
      }
    }
    await flushFilterDebounce()

    for (const label of [
      '清空审批流程筛选',
      '清空审批决策筛选',
      '清空审批委托筛选',
      '清空审批模板筛选',
    ]) {
      await wrapper.get(`[aria-label="${label}"]`).trigger('click')
    }

    expect(approvalState.chainFilters).toMatchObject({
      status: undefined,
      startedBy: undefined,
      sourceService: undefined,
      documentType: undefined,
      documentId: undefined,
    })
    expect(approvalState.decisionFilters).toMatchObject({
      chainId: undefined,
      actorType: undefined,
      actorRef: undefined,
      decision: undefined,
      documentType: undefined,
      documentId: undefined,
    })
    expect(approvalState.delegationFilters).toMatchObject({
      status: undefined,
      delegatorActorRef: undefined,
      delegateActorRef: undefined,
      documentType: undefined,
    })
    expect(approvalState.templateFilters).toMatchObject({
      documentType: undefined,
      isActive: undefined,
    })
    for (const labels of Object.values(filterLabels)) {
      for (const label of Object.values(labels)) {
        const control = filterControl(wrapper, label)
        expect((control.element as HTMLInputElement | HTMLSelectElement).value).toBe(
          control.element.tagName === 'SELECT' ? 'all' : '',
        )
      }
    }
  })

  it('uses clear-filter guidance only for filtered empty tabs', async () => {
    approvalState.delegationFilters.status = undefined
    const wrapper = mountApproval(['business.approvals.read'], { emptyRecords: true })

    expect(wrapper.text()).toContain('当前没有审批流程。')
    expect(wrapper.text()).toContain('当前没有审批决策记录。')
    expect(wrapper.text()).toContain('当前没有审批委托。')
    expect(wrapper.text()).toContain('当前没有审批模板。')

    await filterControl(wrapper, filterLabels.chain.status).setValue('pending')
    await filterControl(wrapper, filterLabels.decision.decision).setValue('approve')
    await filterControl(wrapper, filterLabels.delegation.status).setValue('active')
    await filterControl(wrapper, filterLabels.template.isActive).setValue('true')
    await flushPromises()

    expect(wrapper.text()).toContain('没有符合当前筛选的审批流程。可清空筛选后重试。')
    expect(wrapper.text()).toContain('没有符合当前筛选的审批决策。可清空筛选后重试。')
    expect(wrapper.text()).toContain('没有符合当前筛选的审批委托。可清空筛选后重试。')
    expect(wrapper.text()).toContain('没有符合当前筛选的审批模板。可清空筛选后重试。')
  })
})
