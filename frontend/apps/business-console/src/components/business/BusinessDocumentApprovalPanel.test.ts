import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import BusinessDocumentApprovalPanel from './BusinessDocumentApprovalPanel.vue'

const approvalState = vi.hoisted(() => ({
  startChain: vi.fn(async () => ({ success: true, data: { chainId: 'chain-new-1' } })),
  refreshAll: vi.fn(),
  toastError: vi.fn(),
  toastSuccess: vi.fn(),
  templates: [
    { templateCode: 'ncr-disposition-default', documentType: 'quality-ncr', isActive: true },
  ],
  chains: [
    {
      chainId: 'chain-1',
      documentType: 'quality-ncr',
      documentId: 'NCR-260701-001',
      sourceService: 'quality',
      status: 'Running',
      templateCode: 'ncr-disposition-default',
      startedBy: 'qa-lead',
    },
  ],
  decisions: [
    { decisionId: 'decision-1', chainId: 'chain-1', stepNo: 10, actorRef: 'quality-manager', decision: 'Approve', comment: '同意返工' },
  ],
  chainDetail: {
    chainId: 'chain-1',
    status: 'Running',
    documentId: 'NCR-260701-001',
    steps: [
      { stepNo: 10, stepName: '质量经理复核', approverType: 'role', approverRef: 'quality-manager', status: 'Pending' },
    ],
    decisions: [
      { decisionId: 'decision-1', stepNo: 10, actorRef: 'quality-manager', decision: 'Approve', comment: '同意返工' },
    ],
  },
}))

vi.mock('@/composables/useBusinessApproval', () => ({
  useBusinessApproval: () => ({
    chainDetail: computed(() => approvalState.chainDetail),
    chainDetailError: shallowRef(),
    chainDetailPending: shallowRef(false),
    chainDetailSelection: reactive({ chainId: '' }),
    chainFilters: reactive({ sourceService: undefined, documentType: undefined, documentId: undefined, skip: 0, take: 10 }),
    chains: computed(() => approvalState.chains),
    chainsError: shallowRef(),
    chainsPending: shallowRef(false),
    decisions: computed(() => approvalState.decisions),
    decisionFilters: reactive({ chainId: undefined, documentType: undefined, documentId: undefined, skip: 0, take: 10 }),
    decisionsError: shallowRef(),
    decisionsPending: shallowRef(false),
    refreshAll: approvalState.refreshAll,
    startChain: approvalState.startChain,
    startChainError: shallowRef(),
    startChainPending: shallowRef(false),
    templateFilters: reactive({ documentType: undefined, isActive: undefined, skip: 0, take: 10 }),
    templates: computed(() => approvalState.templates),
    templatesError: shallowRef(),
    templatesPending: shallowRef(false),
  }),
}))

vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({
    principal: {
      principalId: 'user-1',
      loginName: 'qa.user',
      permissionCodes: ['business.approvals.read', 'business.approvals.manage'],
    },
  }),
}))

vi.mock('@/utils/notify', () => ({
  notifyError: approvalState.toastError,
  notifySuccess: approvalState.toastSuccess,
}))

vi.mock('vue-router', async (importOriginal) => {
  const actual = await importOriginal<typeof import('vue-router')>()
  return {
    ...actual,
    RouterLink: { props: ['to'], template: '<a data-router-link><slot /></a>' },
  }
})

const uiStubs = {
  ButtonPro: { props: ['disabled'], template: '<button :disabled="disabled"><slot /></button>' },
  FieldPro: { template: '<div><slot /></div>' },
  FieldProLabel: { template: '<label><slot /></label>' },
  SelectPro: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template: '<select :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)"><slot /></select>',
  },
  SelectProContent: { template: '<slot />' },
  SelectProItem: { props: ['value'], template: '<option :value="value"><slot /></option>' },
  SelectProTrigger: { template: '<span><slot /></span>' },
  SelectProValue: true,
  SelectValue: true,
  Spinner: true,
  StatusBadgePro: { props: ['value'], template: '<span>{{ value }}</span>' },
}

function findButton(wrapper: ReturnType<typeof mount>, text: string) {
  return wrapper.findAll('button').find((button) => button.text().trim() === text)
}

describe('business document approval panel', () => {
  beforeEach(() => {
    approvalState.startChain.mockClear()
    approvalState.toastError.mockClear()
    approvalState.toastSuccess.mockClear()
    approvalState.templates = [
      { templateCode: 'ncr-disposition-default', documentType: 'quality-ncr', isActive: true },
    ]
    approvalState.chains = [
      {
        chainId: 'chain-1',
        documentType: 'quality-ncr',
        documentId: 'NCR-260701-001',
        sourceService: 'quality',
        status: 'Running',
        templateCode: 'ncr-disposition-default',
        startedBy: 'qa-lead',
      },
    ]
    approvalState.decisions = [
      { decisionId: 'decision-1', chainId: 'chain-1', stepNo: 10, actorRef: 'quality-manager', decision: 'Approve', comment: '同意返工' },
    ]
    approvalState.chainDetail = {
      chainId: 'chain-1',
      status: 'Running',
      documentId: 'NCR-260701-001',
      steps: [
        { stepNo: 10, stepName: '质量经理复核', approverType: 'role', approverRef: 'quality-manager', status: 'Pending' },
      ],
      decisions: [
        { decisionId: 'decision-1', stepNo: 10, actorRef: 'quality-manager', decision: 'Approve', comment: '同意返工' },
      ],
    }
  })

  it('shows real approval status, current step, decision history, and approval center link', async () => {
    const wrapper = mount(BusinessDocumentApprovalPanel, {
      props: {
        modelValue: 'chain-1',
        sourceService: 'quality',
        documentType: 'quality-ncr',
        documentId: 'NCR-260701-001',
      },
      global: { stubs: uiStubs },
    })
    await flushPromises()

    expect(wrapper.text()).toContain('Running')
    expect(wrapper.text()).toContain('质量经理复核')
    expect(wrapper.text()).toContain('quality-manager')
    expect(wrapper.text()).toContain('同意返工')
    expect(wrapper.find('input').exists()).toBe(false)
    expect(wrapper.find('[data-router-link]').exists()).toBe(true)
  })

  it('starts a real approval chain for the business document and emits the returned chain id', async () => {
    const wrapper = mount(BusinessDocumentApprovalPanel, {
      props: {
        modelValue: '',
        sourceService: 'quality',
        documentType: 'quality-ncr',
        documentId: 'NCR-260701-001',
      },
      global: { stubs: uiStubs },
    })

    await findButton(wrapper, '发起审批')!.trigger('click')
    await flushPromises()

    expect(approvalState.startChain).toHaveBeenCalledWith({
      templateCode: 'ncr-disposition-default',
      sourceService: 'quality',
      documentType: 'quality-ncr',
      documentId: 'NCR-260701-001',
    })
    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual(['chain-new-1'])
    expect(approvalState.toastSuccess).toHaveBeenCalledWith('审批链已发起')
  })

  it('shows an explicit failure when the approval facade returns a soft-failed envelope', async () => {
    approvalState.startChain.mockResolvedValueOnce({ success: false, data: null } as any)
    const wrapper = mount(BusinessDocumentApprovalPanel, {
      props: {
        modelValue: '',
        sourceService: 'quality',
        documentType: 'quality-ncr',
        documentId: 'NCR-260701-001',
      },
      global: { stubs: uiStubs },
    })

    await findButton(wrapper, '发起审批')!.trigger('click')
    await flushPromises()

    expect(wrapper.emitted('update:modelValue')).toBeUndefined()
    expect(approvalState.toastSuccess).not.toHaveBeenCalled()
    expect(approvalState.toastError).toHaveBeenCalledWith(
      { success: false, data: null },
      '审批链发起未成功，请确认模板与单据状态。',
    )
  })

  it('does not promote an arbitrary chain to current state when the document has no durable id', () => {
    const wrapper = mount(BusinessDocumentApprovalPanel, {
      props: {
        modelValue: '',
        sourceService: 'product-engineering',
        documentType: 'engineering-change-order',
      },
      global: { stubs: uiStubs },
    })

    expect(wrapper.text()).toContain('尚未关联审批链')
    expect(wrapper.text()).not.toContain('Running')
    expect(wrapper.text()).not.toContain('质量经理复核')
    expect(findButton(wrapper, '关联此审批链')).toBeUndefined()
  })

  it('keeps legacy text approval references visible when they are not real chain ids', () => {
    approvalState.chains = []
    approvalState.decisions = []
    approvalState.chainDetail = undefined as any

    const wrapper = mount(BusinessDocumentApprovalPanel, {
      props: {
        modelValue: 'APR-OLD-9',
        sourceService: 'product-engineering',
        documentType: 'engineering-change-order',
        documentId: 'ECO-260701-001',
      },
      global: { stubs: uiStubs },
    })

    expect(wrapper.text()).toContain('历史登记')
    expect(wrapper.text()).toContain('APR-OLD-9')
  })

  it('explains missing templates instead of exposing a fake approval entry point', () => {
    approvalState.templates = []

    const wrapper = mount(BusinessDocumentApprovalPanel, {
      props: {
        modelValue: '',
        sourceService: 'quality',
        documentType: 'quality-ncr',
        documentId: 'NCR-260701-001',
      },
      global: { stubs: uiStubs },
    })

    expect(wrapper.text()).toContain('没有可用审批模板')
    expect(findButton(wrapper, '发起审批')?.attributes('disabled')).toBeDefined()
  })
})
