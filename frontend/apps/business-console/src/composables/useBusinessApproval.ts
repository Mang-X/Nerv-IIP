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
  type BusinessConsoleApprovalChainEnvelope,
  type BusinessConsoleApprovalChainItem,
  type BusinessConsoleApprovalChainListEnvelope,
  type BusinessConsoleApprovalDecisionListEnvelope,
  type BusinessConsoleApprovalDecisionListItem,
  type BusinessConsoleApprovalDelegationItem,
  type BusinessConsoleApprovalDelegationListEnvelope,
  type BusinessConsoleApprovalTaskItem,
  type BusinessConsoleApprovalTaskListEnvelope,
  type BusinessConsoleApprovalTemplateItem,
  type BusinessConsoleApprovalTemplateListEnvelope,
  type BusinessConsoleApprovalTemplateStepItem,
} from '@nerv-iip/api-client'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useMutation, useQuery, useQueryCache, type UseQueryEntry } from '@pinia/colada'
import { computed, reactive } from 'vue'

const DEFAULT_TAKE = 10

const APPROVAL_QUERY_IDS = [
  'getBusinessConsoleApprovalChain',
  'listBusinessConsoleApprovalChains',
  'listBusinessConsoleApprovalDecisions',
  'listBusinessConsoleApprovalDelegations',
  'listBusinessConsoleApprovalTasks',
  'listBusinessConsoleApprovalTemplates',
]

export interface ApprovalActor {
  actorType: string
  actorRef: string
}

export interface ApprovalPagedFilters {
  skip: number
  take: number
}

export interface ApprovalTemplateFilters extends ApprovalPagedFilters {
  documentType?: string
  isActive?: boolean
}

export interface ApprovalChainFilters extends ApprovalPagedFilters {
  status?: string
  startedBy?: string
  sourceService?: string
  documentType?: string
  documentId?: string
}

export interface ApprovalDecisionFilters extends ApprovalPagedFilters {
  chainId?: string
  actorType?: string
  actorRef?: string
  decision?: string
  documentType?: string
  documentId?: string
}

export interface ApprovalDelegationFilters extends ApprovalPagedFilters {
  status?: string
  delegatorActorRef?: string
  delegateActorRef?: string
  documentType?: string
}

export interface ApprovalTemplatePayload {
  templateCode: string
  documentType: string
  version: number
  isActive: boolean
  steps: BusinessConsoleApprovalTemplateStepItem[]
}

export interface ApprovalDecisionPayload {
  chainId: string
  stepNo: number
  decision: string
  comment?: string
}

export interface ApprovalDelegationPayload {
  delegatorActorType: string
  delegatorActorRef: string
  delegateActorType: string
  delegateActorRef: string
  documentType?: string
  effectiveFromUtc?: string
  effectiveToUtc?: string
  reason?: string
}

function defaultPaged<T extends ApprovalPagedFilters>(input: Omit<T, 'skip' | 'take'> = {} as Omit<T, 'skip' | 'take'>): T {
  return reactive({
    skip: 0,
    take: DEFAULT_TAKE,
    ...input,
  }) as T
}

function optionalText(value?: string) {
  const trimmed = value?.trim()
  return trimmed ? trimmed : undefined
}

function optionalNullableText(value?: string) {
  const trimmed = value?.trim()
  return trimmed ? trimmed : null
}

function unwrapItems<T>(envelope: { success?: boolean, data?: { items?: T[] } | null } | undefined): T[] {
  return envelope?.success ? envelope.data?.items ?? [] : []
}

function unwrapTotal(envelope: { success?: boolean, data?: { total?: number } | null } | undefined): number {
  return envelope?.success ? envelope.data?.total ?? 0 : 0
}

function unwrapData<T>(envelope: { success?: boolean, data?: T | null } | undefined): T | undefined {
  return envelope?.success ? envelope.data ?? undefined : undefined
}

function isApprovalQuery(entry: UseQueryEntry) {
  const keyParts = Array.isArray(entry.key) ? entry.key : [entry.key]
  return keyParts.some(
    (part) => typeof part === 'object' && part !== null && '_id' in part && APPROVAL_QUERY_IDS.includes(String(part._id)),
  )
}

function ignoreBackgroundError(_error: unknown) {}

export function useBusinessApproval(actor: ApprovalActor) {
  const businessContext = useBusinessContextStore()
  const queryCache = useQueryCache()

  const templateFilters = defaultPaged<ApprovalTemplateFilters>()
  const chainFilters = defaultPaged<ApprovalChainFilters>()
  const taskFilters = defaultPaged<ApprovalPagedFilters>()
  const decisionFilters = defaultPaged<ApprovalDecisionFilters>()
  const delegationFilters = defaultPaged<ApprovalDelegationFilters>({ status: 'Active' })
  const chainDetailSelection = reactive({ chainId: '' })

  const templateQuery = useQuery(() =>
    listBusinessConsoleApprovalTemplatesQueryOptions({
      query: {
        organizationId: businessContext.organizationId,
        environmentId: businessContext.environmentId,
        documentType: optionalText(templateFilters.documentType),
        isActive: templateFilters.isActive,
        skip: templateFilters.skip,
        take: templateFilters.take,
      },
    }),
  )

  const chainQuery = useQuery(() =>
    listBusinessConsoleApprovalChainsQueryOptions({
      query: {
        organizationId: businessContext.organizationId,
        environmentId: businessContext.environmentId,
        status: optionalText(chainFilters.status),
        startedBy: optionalText(chainFilters.startedBy),
        sourceService: optionalText(chainFilters.sourceService),
        documentType: optionalText(chainFilters.documentType),
        documentId: optionalText(chainFilters.documentId),
        skip: chainFilters.skip,
        take: chainFilters.take,
      },
    }),
  )

  const taskQuery = useQuery(() =>
    listBusinessConsoleApprovalTasksQueryOptions({
      query: {
        organizationId: businessContext.organizationId,
        environmentId: businessContext.environmentId,
        actorType: actor.actorType,
        actorRef: actor.actorRef,
        skip: taskFilters.skip,
        take: taskFilters.take,
      },
    }),
  )

  const decisionQuery = useQuery(() =>
    listBusinessConsoleApprovalDecisionsQueryOptions({
      query: {
        organizationId: businessContext.organizationId,
        environmentId: businessContext.environmentId,
        chainId: optionalText(decisionFilters.chainId),
        actorType: optionalText(decisionFilters.actorType),
        actorRef: optionalText(decisionFilters.actorRef),
        decision: optionalText(decisionFilters.decision),
        documentType: optionalText(decisionFilters.documentType),
        documentId: optionalText(decisionFilters.documentId),
        skip: decisionFilters.skip,
        take: decisionFilters.take,
      },
    }),
  )

  const delegationQuery = useQuery(() =>
    listBusinessConsoleApprovalDelegationsQueryOptions({
      query: {
        organizationId: businessContext.organizationId,
        environmentId: businessContext.environmentId,
        status: optionalText(delegationFilters.status),
        delegatorActorRef: optionalText(delegationFilters.delegatorActorRef),
        delegateActorRef: optionalText(delegationFilters.delegateActorRef),
        documentType: optionalText(delegationFilters.documentType),
        skip: delegationFilters.skip,
        take: delegationFilters.take,
      },
    }),
  )

  const chainDetailQuery = useQuery(() => ({
    ...getBusinessConsoleApprovalChainQueryOptions({
      path: { chainId: chainDetailSelection.chainId },
      query: {
        organizationId: businessContext.organizationId,
        environmentId: businessContext.environmentId,
      },
    }),
    enabled: chainDetailSelection.chainId.trim().length > 0,
  }))

  const invalidateApprovalQueries = () =>
    queryCache.invalidateQueries({ predicate: isApprovalQuery })

  const resolveMutation = useMutation({
    ...resolveBusinessConsoleApprovalStepMutationOptions(),
    onSuccess() {
      void invalidateApprovalQueries().catch(ignoreBackgroundError)
    },
  })
  const templateMutation = useMutation({
    ...createOrUpdateBusinessConsoleApprovalTemplateMutationOptions(),
    onSuccess() {
      void invalidateApprovalQueries().catch(ignoreBackgroundError)
    },
  })
  const createDelegationMutation = useMutation({
    ...createBusinessConsoleApprovalDelegationMutationOptions(),
    onSuccess() {
      void invalidateApprovalQueries().catch(ignoreBackgroundError)
    },
  })
  const revokeDelegationMutation = useMutation({
    ...revokeBusinessConsoleApprovalDelegationMutationOptions(),
    onSuccess() {
      void invalidateApprovalQueries().catch(ignoreBackgroundError)
    },
  })

  return {
    chainDetail: computed(() =>
      unwrapData(chainDetailQuery.data.value as BusinessConsoleApprovalChainEnvelope | undefined),
    ),
    chainDetailError: chainDetailQuery.error,
    chainDetailPending: chainDetailQuery.isLoading,
    chainDetailSelection,
    chainFilters,
    chains: computed<BusinessConsoleApprovalChainItem[]>(() =>
      unwrapItems(chainQuery.data.value as BusinessConsoleApprovalChainListEnvelope | undefined),
    ),
    chainsError: chainQuery.error,
    chainsPending: chainQuery.isLoading,
    chainsTotal: computed(() =>
      unwrapTotal(chainQuery.data.value as BusinessConsoleApprovalChainListEnvelope | undefined),
    ),
    createDelegation: (payload: ApprovalDelegationPayload) =>
      createDelegationMutation.mutateAsync({
        body: {
          organizationId: businessContext.organizationId,
          environmentId: businessContext.environmentId,
          delegatorActorType: payload.delegatorActorType,
          delegatorActorRef: payload.delegatorActorRef,
          delegateActorType: payload.delegateActorType,
          delegateActorRef: payload.delegateActorRef,
          documentType: optionalNullableText(payload.documentType),
          effectiveFromUtc: optionalText(payload.effectiveFromUtc),
          effectiveToUtc: optionalText(payload.effectiveToUtc),
          reason: optionalNullableText(payload.reason),
          createdBy: actor.actorRef,
        },
      }),
    createDelegationError: createDelegationMutation.error,
    createDelegationPending: createDelegationMutation.isLoading,
    decisionFilters,
    decisions: computed<BusinessConsoleApprovalDecisionListItem[]>(() =>
      unwrapItems(decisionQuery.data.value as BusinessConsoleApprovalDecisionListEnvelope | undefined),
    ),
    decisionsError: decisionQuery.error,
    decisionsPending: decisionQuery.isLoading,
    decisionsTotal: computed(() =>
      unwrapTotal(decisionQuery.data.value as BusinessConsoleApprovalDecisionListEnvelope | undefined),
    ),
    delegationFilters,
    delegations: computed<BusinessConsoleApprovalDelegationItem[]>(() =>
      unwrapItems(delegationQuery.data.value as BusinessConsoleApprovalDelegationListEnvelope | undefined),
    ),
    delegationsError: delegationQuery.error,
    delegationsPending: delegationQuery.isLoading,
    delegationsTotal: computed(() =>
      unwrapTotal(delegationQuery.data.value as BusinessConsoleApprovalDelegationListEnvelope | undefined),
    ),
    refreshAll: async () => {
      await Promise.all([
        templateQuery.refetch(),
        chainQuery.refetch(),
        taskQuery.refetch(),
        decisionQuery.refetch(),
        delegationQuery.refetch(),
      ])
    },
    resolveTask: (payload: ApprovalDecisionPayload) =>
      resolveMutation.mutateAsync({
        path: { chainId: payload.chainId, stepNo: payload.stepNo },
        body: {
          organizationId: businessContext.organizationId,
          environmentId: businessContext.environmentId,
          actorType: actor.actorType,
          actorRef: actor.actorRef,
          decision: payload.decision,
          comment: optionalNullableText(payload.comment),
        },
      }),
    resolveTaskError: resolveMutation.error,
    resolveTaskPending: resolveMutation.isLoading,
    revokeDelegation: (delegationId: string) =>
      revokeDelegationMutation.mutateAsync({
        path: { delegationId },
        query: {
          organizationId: businessContext.organizationId,
          environmentId: businessContext.environmentId,
        },
        body: { revokedBy: actor.actorRef },
      }),
    revokeDelegationError: revokeDelegationMutation.error,
    revokeDelegationPending: revokeDelegationMutation.isLoading,
    saveTemplate: (payload: ApprovalTemplatePayload) =>
      templateMutation.mutateAsync({
        body: {
          organizationId: businessContext.organizationId,
          environmentId: businessContext.environmentId,
          templateCode: payload.templateCode,
          documentType: payload.documentType,
          version: payload.version,
          isActive: payload.isActive,
          steps: payload.steps,
        },
      }),
    saveTemplateError: templateMutation.error,
    saveTemplatePending: templateMutation.isLoading,
    taskFilters,
    tasks: computed<BusinessConsoleApprovalTaskItem[]>(() =>
      unwrapItems(taskQuery.data.value as BusinessConsoleApprovalTaskListEnvelope | undefined),
    ),
    tasksError: taskQuery.error,
    tasksPending: taskQuery.isLoading,
    tasksTotal: computed(() =>
      unwrapTotal(taskQuery.data.value as BusinessConsoleApprovalTaskListEnvelope | undefined),
    ),
    templateFilters,
    templates: computed<BusinessConsoleApprovalTemplateItem[]>(() =>
      unwrapItems(templateQuery.data.value as BusinessConsoleApprovalTemplateListEnvelope | undefined),
    ),
    templatesError: templateQuery.error,
    templatesPending: templateQuery.isLoading,
    templatesTotal: computed(() =>
      unwrapTotal(templateQuery.data.value as BusinessConsoleApprovalTemplateListEnvelope | undefined),
    ),
  }
}
