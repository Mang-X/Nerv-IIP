import { DOMWrapper, flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'
import { NvEntityPicker } from '@nerv-iip/ui'
import { createPinia } from 'pinia'
import { useAuthStore } from '@/stores/auth'
import ApprovalPage from './index.vue'

const approvalState = vi.hoisted(() => ({
  createDelegation: vi.fn(async () => undefined),
  resolveTask: vi.fn(async () => undefined),
  revokeDelegation: vi.fn(async () => undefined),
  saveTemplate: vi.fn(async () => undefined),
  toastError: vi.fn(),
  toastSuccess: vi.fn(),
  taskDocumentType: 'purchase-order',
  workers: [
    { userId: 'manager-a', displayName: '张敏', employeeNo: 'YG001', active: true },
    { userId: 'manager-c', displayName: '李华', employeeNo: 'YG003', active: true },
    { userId: 'internal-user-without-name', displayName: '', employeeNo: 'YG004', active: true },
  ],
}))

// 反馈走真实 notify 分层透传，只把 toast 换成 spy：断言的是「用户最终看到的那句话」。
vi.mock('@nerv-iip/ui', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@nerv-iip/ui')>()),
  toast: {
    error: (...args: unknown[]) => approvalState.toastError(...args),
    success: (...args: unknown[]) => approvalState.toastSuccess(...args),
  },
}))

vi.mock('@/composables/useBusinessMasterData', () => ({
  useBusinessWorkers: () => ({
    workers: computed(() => approvalState.workers),
    workersPending: shallowRef(false),
  }),
  useBusinessMasterDataResources: () => ({
    resources: computed(() => []),
    resourcesPending: shallowRef(false),
  }),
}))

vi.mock('@/composables/useBusinessApproval', () => ({
  useBusinessApproval: () => ({
    contextReady: computed(() => true),
    chainDetail: computed(() => undefined),
    chainDetailSelection: reactive({ chainId: '' }),
    chains: computed(() => [
      {
        chainId: 'chain-1',
        status: 'Running',
        documentType: '采购订单',
        documentId: 'PO-260701-001',
        templateCode: 'purchase-order',
      },
    ]),
    chainsPending: shallowRef(false),
    chainsTotal: computed(() => 1),
    chainsError: shallowRef(undefined),
    chainFilters: reactive({
      status: undefined,
      startedBy: undefined,
      sourceService: undefined,
      documentType: undefined,
      documentId: undefined,
      skip: 0,
      take: 10,
    }),
    createDelegation: approvalState.createDelegation,
    createDelegationError: shallowRef(undefined),
    createDelegationPending: shallowRef(false),
    decisions: computed(() => [
      {
        decisionId: 'decision-1',
        chainId: 'chain-1',
        decision: 'approve',
        actorRef: 'manager-a',
        documentType: '采购订单',
        documentId: 'PO-260701-001',
      },
    ]),
    decisionsPending: shallowRef(false),
    decisionsTotal: computed(() => 1),
    decisionsError: shallowRef(undefined),
    decisionFilters: reactive({
      chainId: undefined,
      actorType: undefined,
      actorRef: undefined,
      decision: undefined,
      documentType: undefined,
      documentId: undefined,
      skip: 0,
      take: 10,
    }),
    delegations: computed(() => [
      {
        delegationId: 'delegation-1',
        status: 'Active',
        delegatorActorRef: 'manager-a',
        delegateActorRef: 'manager-b',
        documentType: '采购订单',
      },
    ]),
    delegationsPending: shallowRef(false),
    delegationsTotal: computed(() => 1),
    delegationsError: shallowRef(undefined),
    delegationFilters: reactive({
      status: undefined,
      delegatorActorRef: undefined,
      delegateActorRef: undefined,
      documentType: undefined,
      skip: 0,
      take: 10,
    }),
    refreshAll: vi.fn(),
    resolveTask: approvalState.resolveTask,
    resolveTaskError: shallowRef(undefined),
    resolveTaskPending: shallowRef(false),
    revokeDelegation: approvalState.revokeDelegation,
    revokeDelegationError: shallowRef(undefined),
    revokeDelegationPending: shallowRef(false),
    saveTemplate: approvalState.saveTemplate,
    saveTemplateError: shallowRef(undefined),
    saveTemplatePending: shallowRef(false),
    tasks: computed(() => [
      {
        chainId: 'chain-1',
        stepNo: 10,
        stepName: '采购经理审批',
        documentType: approvalState.taskDocumentType,
        documentId: 'PO-260701-001',
      },
    ]),
    tasksPending: shallowRef(false),
    tasksTotal: computed(() => 1),
    tasksError: shallowRef(undefined),
    taskFilters: reactive({ skip: 0, take: 10 }),
    templates: computed(() => [
      {
        templateId: 'template-1',
        templateCode: 'purchase-order',
        documentType: '采购订单',
        version: 1,
        isActive: true,
        steps: [],
      },
    ]),
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

/** 只选选择器桩成带同名 id 的输入位，保留用例原有的 `setValue` 语义。 */
const entityPickerStub = {
  props: ['modelValue', 'options', 'id'],
  emits: ['update:modelValue'],
  template:
    '<input :id="id" :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />',
}

const tabsStubs = {
  NvTabs: { template: '<section><slot /></section>' },
  NvTabsList: { template: '<nav><slot /></nav>' },
  NvTabsTrigger: { props: ['value'], template: '<button type="button"><slot /></button>' },
  NvTabsContent: { props: ['value'], template: '<section><slot /></section>' },
}

function mountApproval(permissionCodes: string[], realPickers = false) {
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
        // 代理人已从自由文本改成只选的实体选择器；这个用例关心的是「提交体的时区换算」，
        // 所以把选择器桩成带同名 id 的输入位，让 setValue 仍然表达「选中了某个代理人」。
        NvEntityPicker: realPickers ? false : entityPickerStub,
        ...(realPickers
          ? {
              NvSelect: {
                name: 'NvSelect',
                props: ['modelValue'],
                emits: ['update:modelValue'],
                template:
                  '<select :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)"><slot /></select>',
              },
              NvSelectValue: { template: '<span />' },
              NvSelectTrigger: { template: '<span><slot /></span>' },
              NvSelectContent: { template: '<span><slot /></span>' },
              NvSelectItem: {
                props: ['value'],
                template: '<option :value="value"><slot /></option>',
              },
            }
          : {}),
        NvSearchSelect: entityPickerStub,
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
        ...tabsStubs,
      },
    },
  })
}

beforeEach(() => {
  approvalState.saveTemplate.mockClear()
  approvalState.taskDocumentType = 'purchase-order'
  approvalState.createDelegation.mockClear()
  approvalState.resolveTask.mockClear()
  approvalState.revokeDelegation.mockClear()
  approvalState.toastError.mockReset()
  approvalState.toastSuccess.mockReset()
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
      decision: 'approve',
      comment: '',
    })
    expect(approvalState.revokeDelegation).toHaveBeenCalledWith('delegation-1')
  })

  // #1311：三个裁决动作发出去的必须是后端权威小写取值（approve/reject/return）。
  // 曾经发的是 'Approve'/'Reject'/'Resolve'——大小写不匹配 + 'Resolve' 后端根本不支持，一切裁决必 400。
  it.each([
    ['驳回', 'reject'],
    ['退回', 'return'],
  ])('submits the lowercase contract value for %s', async (label, expected) => {
    const wrapper = mountApproval(['business.approvals.read', 'business.approvals.manage'])
    await flushPromises()

    const trigger = wrapper.findAll('button').find((button) => button.text().includes(label))!
    await trigger.trigger('click')
    const decisionForm = wrapper
      .findAll('form')
      .find((form) => form.find('#approval-comment').exists())!
    await decisionForm.trigger('submit')
    await flushPromises()

    expect(approvalState.resolveTask).toHaveBeenCalledWith({
      chainId: 'chain-1',
      stepNo: 10,
      decision: expected,
      comment: '',
    })
  })

  it('surfaces the server rejection reason instead of the generic retry copy', async () => {
    // 后端非法取值的 400 会给出中文领域消息（列出合法值），分层透传必须原样带到 toast 上。
    approvalState.resolveTask.mockRejectedValueOnce({
      message: '审批裁决取值非法，只能是 approve（同意）、reject（驳回）或 return（退回）。',
    })
    const wrapper = mountApproval(['business.approvals.read', 'business.approvals.manage'])
    await flushPromises()

    const approve = wrapper.findAll('button').find((button) => button.text().includes('通过'))!
    await approve.trigger('click')
    await flushPromises()

    expect(approvalState.toastError).toHaveBeenCalledWith(
      '审批处理失败：审批裁决取值非法，只能是 approve（同意）、reject（驳回）或 return（退回）。',
    )
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

describe('approval display identities', () => {
  it.each([
    ['purchase-order', '采购订单'],
    ['unregistered-system-type', '—'],
  ])('translates the rejection document type %s', async (code, label) => {
    approvalState.taskDocumentType = code
    const wrapper = mountApproval(['business.approvals.manage'])
    await flushPromises()
    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('驳回'))!
      .trigger('click')
    const context = wrapper.findComponent({ name: 'CarriedContextSummary' })
    expect(context.text()).toContain(label)
    expect(context.text()).not.toContain(code)
  })

  it('shows personnel business information, searches and delegates with the original identity', async () => {
    const wrapper = mountApproval(['business.approvals.manage'], true)
    await flushPromises()
    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('新建委托'))!
      .trigger('click')
    await wrapper.find('#approval-delegate').trigger('click')
    await flushPromises()
    const popup = new DOMWrapper(document.body)
    expect(popup.text()).toContain('李华')
    expect(popup.text()).toContain('YG003')
    expect(popup.text()).toContain('—')
    expect(popup.text()).not.toContain('internal-user-without-name')
    expect(popup.text()).not.toContain('manager-c')
    const search = popup.find('input[role="combobox"]')
    await search.setValue('YG003')
    expect(popup.findAll('[role="option"]')).toHaveLength(1)
    await popup.find('[role="option"]').trigger('click')
    expect(wrapper.find('#approval-delegate').text()).toBe('李华')
    await wrapper.find('#approval-delegation-from').setValue('2026-07-01T09:30')
    await wrapper.find('#approval-delegation-to').setValue('2026-07-03T18:45')
    await wrapper
      .findAll('form')
      .find((form) => form.find('#approval-delegate').exists())!
      .trigger('submit')
    expect(approvalState.createDelegation).toHaveBeenCalledWith(
      expect.objectContaining({ delegateActorRef: 'manager-c' }),
    )
    wrapper.unmount()
  })

  it('retains an approver identity without using it as the selected display name', async () => {
    const wrapper = mountApproval(['business.approvals.manage'], true)
    await flushPromises()
    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('维护模板'))!
      .trigger('click')
    const typeSelect = wrapper
      .findAllComponents({ name: 'NvSelect' })
      .find((select) => select.props('modelValue') === 'role')!
    typeSelect.vm.$emit('update:modelValue', 'user')
    await flushPromises()
    const picker = wrapper.findComponent(NvEntityPicker)
    picker.vm.$emit('update:modelValue', 'unavailable-user')
    await flushPromises()
    expect(wrapper.find('#approval-template-actor-ref').text()).toBe('—')
    expect(picker.props('modelValue')).toBe('unavailable-user')
    await wrapper.find('#approval-template-actor-ref').trigger('click')
    await flushPromises()
    const popup = new DOMWrapper(document.body)
    await popup.find('input[role="combobox"]').setValue('李华')
    await popup.find('[role="option"]').trigger('click')
    expect(wrapper.find('#approval-template-actor-ref').text()).toBe('李华')
    wrapper.findComponent({ name: 'NvCombobox' }).vm.$emit('update:modelValue', 'purchase-order')
    wrapper
      .findAllComponents({ name: 'NvSelect' })
      .find((select) => select.find('#approval-template-doc').exists())!
      .vm.$emit('update:modelValue', 'purchase-order')
    await wrapper.find('#approval-template-step-name').setValue('采购经理审批')
    await wrapper
      .findAll('form')
      .find((form) => form.find('#approval-template-actor-ref').exists())!
      .trigger('submit')
    expect(approvalState.saveTemplate).toHaveBeenCalledWith(
      expect.objectContaining({
        steps: [expect.objectContaining({ approverType: 'user', approverRef: 'manager-c' })],
      }),
    )
    wrapper.unmount()
  })
})
