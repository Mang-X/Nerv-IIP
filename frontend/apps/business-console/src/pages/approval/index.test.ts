import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'
import { createPinia } from 'pinia'
import { useAuthStore } from '@/stores/auth'
import ApprovalPage from './index.vue'

const approvalState = vi.hoisted(() => ({
  resolveTask: vi.fn(async () => undefined),
  revokeDelegation: vi.fn(async () => undefined),
}))

vi.mock('@/composables/useBusinessApproval', () => ({
  useBusinessApproval: () => ({
    chainDetail: computed(() => undefined),
    chainDetailSelection: reactive({ chainId: '' }),
    chains: computed(() => [{ chainId: 'chain-1', status: 'Running', documentType: '采购订单', documentId: 'PO-260701-001', templateCode: 'purchase-order' }]),
    chainsPending: shallowRef(false),
    chainsTotal: computed(() => 1),
    chainsError: shallowRef(undefined),
    chainFilters: reactive({ status: undefined, startedBy: undefined, sourceService: undefined, documentType: undefined, documentId: undefined, skip: 0, take: 10 }),
    createDelegation: vi.fn(),
    createDelegationError: shallowRef(undefined),
    createDelegationPending: shallowRef(false),
    decisions: computed(() => [{ decisionId: 'decision-1', chainId: 'chain-1', decision: 'Approve', actorRef: 'manager-a', documentType: '采购订单', documentId: 'PO-260701-001' }]),
    decisionsPending: shallowRef(false),
    decisionsTotal: computed(() => 1),
    decisionsError: shallowRef(undefined),
    decisionFilters: reactive({ chainId: undefined, actorType: undefined, actorRef: undefined, decision: undefined, documentType: undefined, documentId: undefined, skip: 0, take: 10 }),
    delegations: computed(() => [{ delegationId: 'delegation-1', status: 'Active', delegatorActorRef: 'manager-a', delegateActorRef: 'manager-b', documentType: '采购订单' }]),
    delegationsPending: shallowRef(false),
    delegationsTotal: computed(() => 1),
    delegationsError: shallowRef(undefined),
    delegationFilters: reactive({ status: undefined, delegatorActorRef: undefined, delegateActorRef: undefined, documentType: undefined, skip: 0, take: 10 }),
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
    tasks: computed(() => [{ chainId: 'chain-1', stepNo: 10, stepName: '采购经理审批', documentType: '采购订单', documentId: 'PO-260701-001' }]),
    tasksPending: shallowRef(false),
    tasksTotal: computed(() => 1),
    tasksError: shallowRef(undefined),
    taskFilters: reactive({ skip: 0, take: 10 }),
    templates: computed(() => [{ templateId: 'template-1', templateCode: 'purchase-order', documentType: '采购订单', version: 1, isActive: true, steps: [] }]),
    templatesPending: shallowRef(false),
    templatesTotal: computed(() => 1),
    templatesError: shallowRef(undefined),
    templateFilters: reactive({ documentType: undefined, isActive: undefined, skip: 0, take: 10 }),
  }),
}))

const tableStub = {
  props: ['rows'],
  template: `
    <section data-testid="data-table">
      <div v-for="row in rows" :key="row.chainId || row.delegationId || row.templateId || row.decisionId" data-testid="row">
        <span>{{ row.documentId || row.templateCode || row.delegationId || row.decisionId }}</span>
        <slot name="cell-actions" :row="row" />
      </div>
    </section>
  `,
}

const tabsStubs = {
  TabsPro: { template: '<section><slot /></section>' },
  TabsProList: { template: '<nav><slot /></nav>' },
  TabsProTrigger: { props: ['value'], template: '<button type="button"><slot /></button>' },
  TabsProContent: { props: ['value'], template: '<section><slot /></section>' },
}

function mountApproval(permissionCodes: string[]) {
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
        DataTablePro: tableStub,
        RowActions: { template: '<div data-testid="row-actions"><slot /></div>' },
        DropdownMenuProItem: { emits: ['click'], template: '<button type="button" @click="$emit(\'click\', $event)"><slot /></button>' },
        PageHeader: { props: ['title'], template: '<header><h1>{{ title }}</h1><slot /><slot name="actions" /></header>' },
        SectionCard: true,
        SectionCards: { template: '<section><slot /></section>' },
        StatusBadgePro: { props: ['value'], template: '<span>{{ value }}</span>' },
        ...tabsStubs,
      },
    },
  })
}

beforeEach(() => {
  approvalState.resolveTask.mockClear()
  approvalState.revokeDelegation.mockClear()
})

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
})
