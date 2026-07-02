import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'
import { createPinia, setActivePinia } from 'pinia'

import {
  createBusinessConsoleApprovalDelegationMutationOptions,
  createOrUpdateBusinessConsoleApprovalTemplateMutationOptions,
  getBusinessConsoleApprovalChainQueryOptions,
  listBusinessConsoleApprovalChainsQueryOptions,
  listBusinessConsoleApprovalDecisionsQueryOptions,
  listBusinessConsoleApprovalDelegationsQueryOptions,
  listBusinessConsoleApprovalTasksQueryOptions,
  listBusinessConsoleApprovalTemplatesQueryOptions,
  resolveBusinessConsoleApprovalStepMutationOptions,
  revokeBusinessConsoleApprovalDelegationMutationOptions,
  startBusinessConsoleApprovalChainMutationOptions,
} from '@nerv-iip/api-client'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useBusinessApproval } from './useBusinessApproval'

const coladaState = vi.hoisted(() => ({
  invalidateQueries: vi.fn(async () => undefined),
  queryDataById: new Map<string, unknown>(),
  queryOptionsById: new Map<string, { enabled?: boolean }>(),
}))

vi.mock('@nerv-iip/api-client', () => ({
  createBusinessConsoleApprovalDelegationMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({ success: true, data: { delegationId: 'delegation-1', vars } })),
  })),
  createOrUpdateBusinessConsoleApprovalTemplateMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({ success: true, data: { templateId: 'template-1', vars } })),
  })),
  getBusinessConsoleApprovalChainQueryOptions: vi.fn(() => ({
    key: [{ _id: 'getBusinessConsoleApprovalChain' }],
    query: vi.fn(),
  })),
  listBusinessConsoleApprovalChainsQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleApprovalChains' }],
    query: vi.fn(),
  })),
  listBusinessConsoleApprovalDecisionsQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleApprovalDecisions' }],
    query: vi.fn(),
  })),
  listBusinessConsoleApprovalDelegationsQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleApprovalDelegations' }],
    query: vi.fn(),
  })),
  listBusinessConsoleApprovalTasksQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleApprovalTasks' }],
    query: vi.fn(),
  })),
  listBusinessConsoleApprovalTemplatesQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleApprovalTemplates' }],
    query: vi.fn(),
  })),
  resolveBusinessConsoleApprovalStepMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({ success: true, data: { decisionId: 'decision-1', vars } })),
  })),
  revokeBusinessConsoleApprovalDelegationMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({ success: true, data: vars })),
  })),
  startBusinessConsoleApprovalChainMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({ success: true, data: { chainId: 'chain-started-1', vars } })),
  })),
}))

vi.mock('@pinia/colada', () => ({
  useMutation: vi.fn((options) => ({
    error: shallowRef(),
    isLoading: shallowRef(false),
    mutateAsync: vi.fn(async (vars) => {
      const result = await options.mutation(vars)
      await options.onSuccess?.(result)
      return result
    }),
  })),
  useQuery: vi.fn((optionsFactory) => {
    const options = optionsFactory()
    const key = Array.isArray(options.key) ? options.key[0] : undefined
    const id = key && typeof key === 'object' && '_id' in key ? String(key._id) : ''
    coladaState.queryOptionsById.set(id, options)

    return {
      data: shallowRef(coladaState.queryDataById.get(id)),
      error: shallowRef(),
      isLoading: shallowRef(false),
      refetch: vi.fn(async () => undefined),
    }
  }),
  useQueryCache: vi.fn(() => ({
    invalidateQueries: coladaState.invalidateQueries,
  })),
}))

describe('business approval composable', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    coladaState.invalidateQueries.mockClear()
    coladaState.queryDataById.clear()
    coladaState.queryOptionsById.clear()
  })

  it('loads approval center lists with current business context and actor filters', () => {
    const context = useBusinessContextStore()
    context.patchContext({ organizationId: 'org-approval', environmentId: 'plant-a' })
    coladaState.queryDataById.set('listBusinessConsoleApprovalTemplates', {
      success: true,
      data: { items: [{ templateCode: 'purchase-order', documentType: '采购订单' }], total: 1 },
    })
    coladaState.queryDataById.set('listBusinessConsoleApprovalChains', {
      success: true,
      data: { items: [{ chainId: 'chain-1', status: 'Running' }], total: 1 },
    })
    coladaState.queryDataById.set('listBusinessConsoleApprovalTasks', {
      success: true,
      data: { items: [{ chainId: 'chain-1', stepNo: 10, documentId: 'PO-260701-001' }], total: 1 },
    })
    coladaState.queryDataById.set('listBusinessConsoleApprovalDecisions', {
      success: true,
      data: { items: [{ decisionId: 'decision-1', decision: 'Approve' }], total: 1 },
    })
    coladaState.queryDataById.set('listBusinessConsoleApprovalDelegations', {
      success: true,
      data: { items: [{ delegationId: 'delegation-1', status: 'Active' }], total: 1 },
    })
    coladaState.queryDataById.set('getBusinessConsoleApprovalChain', {
      success: true,
      data: { chainId: 'chain-1', steps: [{ stepNo: 10, status: 'Pending' }] },
    })

    const approval = useBusinessApproval({ actorType: 'user', actorRef: 'user-approver' })
    approval.chainDetailSelection.chainId = ''

    expect(listBusinessConsoleApprovalTemplatesQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-approval',
        environmentId: 'plant-a',
        documentType: undefined,
        isActive: undefined,
        skip: 0,
        take: 10,
      },
    })
    expect(listBusinessConsoleApprovalTasksQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-approval',
        environmentId: 'plant-a',
        actorType: 'user',
        actorRef: 'user-approver',
        skip: 0,
        take: 10,
      },
    })
    expect(coladaState.queryOptionsById.get('getBusinessConsoleApprovalChain')?.enabled).toBe(false)
    expect(approval.templates.value[0]?.templateCode).toBe('purchase-order')
    expect(approval.chains.value[0]?.chainId).toBe('chain-1')
    expect(approval.tasks.value[0]?.documentId).toBe('PO-260701-001')
    expect(approval.decisions.value[0]?.decision).toBe('Approve')
    expect(approval.delegations.value[0]?.delegationId).toBe('delegation-1')
  })

  it('submits approval task decisions, templates, chain starts, and delegations through stable api-client mutations', async () => {
    const approval = useBusinessApproval({ actorType: 'user', actorRef: 'approver-a' })

    await approval.resolveTask({
      chainId: 'chain-1',
      stepNo: 20,
      decision: 'Reject',
      comment: '缺少采购合同附件',
    })
    await approval.saveTemplate({
      templateCode: 'purchase-order',
      documentType: '采购订单',
      version: 3,
      isActive: true,
      steps: [{ stepNo: 10, stepName: '采购经理', approverType: 'role', approverRef: 'buyer-manager' }],
    })
    await approval.startChain({
      templateCode: 'ncr-disposition-default',
      sourceService: 'quality',
      documentType: 'quality-ncr',
      documentId: 'NCR-260701-001',
    })
    await approval.createDelegation({
      delegatorActorType: 'user',
      delegatorActorRef: 'approver-a',
      delegateActorType: 'user',
      delegateActorRef: 'approver-b',
      effectiveFromUtc: '2026-07-01T00:00:00.000Z',
      effectiveToUtc: '2026-07-05T00:00:00.000Z',
      reason: '出差期间代理',
    })
    await approval.revokeDelegation('delegation-1')

    expect(vi.mocked(resolveBusinessConsoleApprovalStepMutationOptions).mock.results[0]?.value.mutation)
      .toHaveBeenCalledWith({
        path: { chainId: 'chain-1', stepNo: 20 },
        body: {
          organizationId: 'org-001',
          environmentId: 'env-dev',
          actorType: 'user',
          actorRef: 'approver-a',
          decision: 'Reject',
          comment: '缺少采购合同附件',
        },
      })
    expect(vi.mocked(createOrUpdateBusinessConsoleApprovalTemplateMutationOptions).mock.results[0]?.value.mutation)
      .toHaveBeenCalledWith({
        body: expect.objectContaining({
          organizationId: 'org-001',
          environmentId: 'env-dev',
          templateCode: 'purchase-order',
          version: 3,
        }),
      })
    expect(vi.mocked(startBusinessConsoleApprovalChainMutationOptions).mock.results[0]?.value.mutation)
      .toHaveBeenCalledWith({
        body: {
          organizationId: 'org-001',
          environmentId: 'env-dev',
          templateCode: 'ncr-disposition-default',
          sourceService: 'quality',
          documentType: 'quality-ncr',
          documentId: 'NCR-260701-001',
          documentLineId: null,
          startedBy: 'approver-a',
        },
      })
    expect(vi.mocked(createBusinessConsoleApprovalDelegationMutationOptions).mock.results[0]?.value.mutation)
      .toHaveBeenCalledWith({
        body: expect.objectContaining({
          organizationId: 'org-001',
          environmentId: 'env-dev',
          createdBy: 'approver-a',
          delegateActorRef: 'approver-b',
        }),
      })
    expect(vi.mocked(revokeBusinessConsoleApprovalDelegationMutationOptions).mock.results[0]?.value.mutation)
      .toHaveBeenCalledWith({
        path: { delegationId: 'delegation-1' },
        query: { organizationId: 'org-001', environmentId: 'env-dev' },
        body: { revokedBy: 'approver-a' },
      })
    expect(coladaState.invalidateQueries).toHaveBeenCalledWith({ predicate: expect.any(Function) })
  })

  it('uses the latest actor when approval identity changes after setup', async () => {
    const actor = shallowRef({ actorType: 'user', actorRef: 'approver-a' })
    const approval = useBusinessApproval(actor)

    actor.value = { actorType: 'user', actorRef: 'approver-b' }
    await approval.resolveTask({
      chainId: 'chain-2',
      stepNo: 10,
      decision: 'Approve',
    })
    await approval.createDelegation({
      delegatorActorType: 'user',
      delegatorActorRef: 'approver-b',
      delegateActorType: 'user',
      delegateActorRef: 'approver-c',
    })
    await approval.revokeDelegation('delegation-2')

    expect(vi.mocked(resolveBusinessConsoleApprovalStepMutationOptions).mock.results[0]?.value.mutation)
      .toHaveBeenCalledWith(expect.objectContaining({
        body: expect.objectContaining({ actorRef: 'approver-b' }),
      }))
    expect(vi.mocked(createBusinessConsoleApprovalDelegationMutationOptions).mock.results[0]?.value.mutation)
      .toHaveBeenCalledWith(expect.objectContaining({
        body: expect.objectContaining({ createdBy: 'approver-b' }),
      }))
    expect(vi.mocked(revokeBusinessConsoleApprovalDelegationMutationOptions).mock.results[0]?.value.mutation)
      .toHaveBeenCalledWith(expect.objectContaining({
        body: { revokedBy: 'approver-b' },
      }))
  })
})
