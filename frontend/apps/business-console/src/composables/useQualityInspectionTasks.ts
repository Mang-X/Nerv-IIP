import {
  assignBusinessConsoleQualityInspectionTask,
  claimBusinessConsoleQualityInspectionTask,
  createBusinessConsoleQualityInspectionRecordFromTask,
  confirmBusinessConsoleOperation,
  listBusinessConsoleQualityInspectionTasks,
  listBusinessConsoleQualityInspectionTasksQueryOptions,
  type BusinessConsoleCreateInspectionRecordFromTaskRequest,
  type BusinessConsoleQualityInspectionTaskItem,
} from '@nerv-iip/api-client'
import { useQuery, useQueryCache, type UseQueryEntry } from '@pinia/colada'
import { computed, reactive, shallowRef } from 'vue'
import {
  acquirePendingBusinessIntent,
  completePendingBusinessIntent,
  peekPendingBusinessIntent,
} from '@nerv-iip/business-core'
import { useAuthStore } from '@/stores/auth'
import {
  bindBusinessContext,
  hasBusinessContext,
  refetchWithBusinessContext,
  type BusinessContextFields,
} from './businessContextBinding'
import { executeLifecycleAction } from './lifecycleAction'
import {
  useListFreshness,
  useListResponseState,
  useScopeBoundListResponse,
} from './useListFreshness'

const DEFAULT_TAKE = 200

function requirePendingPayloadSnapshot<T extends object>(snapshot: unknown, operation: string): T {
  if (!snapshot || typeof snapshot !== 'object') {
    throw new Error(`${operation}缺少冻结的待处理载荷，请保留当前页面并人工核实。`)
  }
  return snapshot as T
}

export type InspectionTaskSourceType = 'receiving' | 'operation' | 'final' | 'all'

export interface InspectionTaskFilters extends BusinessContextFields {
  sourceType: InspectionTaskSourceType
  status?: string
  skuCode?: string
  skip: number
  take: number
  sourceDocumentNo?: string
  inspectionTaskId?: string
}

interface InspectionTaskPage {
  success?: boolean
  data?: {
    items?: BusinessConsoleQualityInspectionTaskItem[] | null
    total?: number
  } | null
}

interface InspectionTaskLocator {
  sourceDocumentNo?: string
  inspectionTaskId?: string
}

type QualityInspectionSubmitIntent = Omit<
  BusinessConsoleCreateInspectionRecordFromTaskRequest,
  'idempotencyKey'
> & {
  idempotencyKey?: string
}

type InspectionTaskPageLoader = (skip: number, take: number) => Promise<InspectionTaskPage>

function isBusinessQuery(id: string) {
  return (entry: UseQueryEntry) => {
    const keyParts = Array.isArray(entry.key) ? entry.key : [entry.key]
    return keyParts.some(
      (part) => typeof part === 'object' && part !== null && '_id' in part && part._id === id,
    )
  }
}

function ignoreBackgroundError(_error: unknown) {}

function newQualitySubmitIntentKey() {
  const cryptoApi = (globalThis as { crypto?: { randomUUID?: () => string } }).crypto
  const uniquePart =
    cryptoApi && typeof cryptoApi.randomUUID === 'function'
      ? cryptoApi.randomUUID()
      : `${Date.now()}-${Math.random().toString(36).slice(2)}`
  return `quality-submit-${uniquePart}`
}

function newQualityTaskActionKey(action: 'claim' | 'assignment') {
  const cryptoApi = (globalThis as { crypto?: { randomUUID?: () => string } }).crypto
  const uniquePart =
    cryptoApi && typeof cryptoApi.randomUUID === 'function'
      ? cryptoApi.randomUUID()
      : `${Date.now()}-${Math.random().toString(36).slice(2)}`
  return `quality-${action}-${uniquePart}`
}

export function isInspectionTaskOverdue(
  task: BusinessConsoleQualityInspectionTaskItem,
  now = new Date(),
) {
  return (
    task.status === 'pending' &&
    !!task.dueAtUtc &&
    new Date(task.dueAtUtc).getTime() < now.getTime()
  )
}

export function sortInspectionTasks(
  tasks: BusinessConsoleQualityInspectionTaskItem[],
  now = new Date(),
) {
  return [...tasks].sort((left, right) => {
    const overdueDifference =
      Number(isInspectionTaskOverdue(right, now)) - Number(isInspectionTaskOverdue(left, now))
    if (overdueDifference !== 0) return overdueDifference

    const leftDue = left.dueAtUtc ? new Date(left.dueAtUtc).getTime() : Number.POSITIVE_INFINITY
    const rightDue = right.dueAtUtc ? new Date(right.dueAtUtc).getTime() : Number.POSITIVE_INFINITY
    return leftDue - rightDue
  })
}

export async function locateInspectionTasks(
  loadPage: InspectionTaskPageLoader,
  locator: InspectionTaskLocator,
) {
  const sourceDocumentNo = locator.sourceDocumentNo?.trim()
  const inspectionTaskId = locator.inspectionTaskId?.trim()
  const located: BusinessConsoleQualityInspectionTaskItem[] = []
  let skip = 0
  let total = Number.POSITIVE_INFINITY

  while (skip < total) {
    const response = await loadPage(skip, DEFAULT_TAKE)
    const items = response.success ? (response.data?.items ?? []) : []
    total = Math.max(0, response.data?.total ?? 0)

    located.push(
      ...items.filter((task) => {
        if (inspectionTaskId && task.inspectionTaskId?.trim() !== inspectionTaskId) return false
        if (!sourceDocumentNo) return !!inspectionTaskId
        return (
          task.sourceService?.trim().toLowerCase() === 'wms' &&
          task.sourceType === 'receiving' &&
          task.sourceDocumentId?.trim() === sourceDocumentNo
        )
      }),
    )

    if (items.length === 0) break
    skip += DEFAULT_TAKE
  }

  return located
}

function defaultFilters(initial: Partial<InspectionTaskFilters> = {}) {
  return bindBusinessContext(
    reactive<InspectionTaskFilters>({
      organizationId: '',
      environmentId: '',
      sourceType: 'all',
      skip: 0,
      take: DEFAULT_TAKE,
      ...initial,
    }),
  )
}

export function useQualityInspectionTasks(initialFilters: Partial<InspectionTaskFilters> = {}) {
  const filters = defaultFilters(initialFilters)
  const hasLocator = computed(
    () => !!filters.sourceDocumentNo?.trim() || !!filters.inspectionTaskId?.trim(),
  )
  const tasksQuery = useQuery(() => {
    const query = {
      organizationId: filters.organizationId,
      environmentId: filters.environmentId,
      ...(filters.status ? { status: filters.status } : {}),
      ...(filters.skuCode?.trim() ? { skuCode: filters.skuCode.trim() } : {}),
      skip: filters.skip,
      take: filters.take,
    }
    const generatedOptions = listBusinessConsoleQualityInspectionTasksQueryOptions({ query })
    const sourceDocumentNo = filters.sourceDocumentNo?.trim()
    const inspectionTaskId = filters.inspectionTaskId?.trim()

    return {
      ...generatedOptions,
      ...(hasLocator.value
        ? {
            key: [
              ...generatedOptions.key,
              {
                sourceDocumentNo: sourceDocumentNo ?? '',
                inspectionTaskId: inspectionTaskId ?? '',
              },
            ],
            query: async () => {
              const items = await locateInspectionTasks(
                async (skip, take) => {
                  const { data } = await listBusinessConsoleQualityInspectionTasks({
                    query: { ...query, skip, take },
                    throwOnError: true,
                  })
                  return data
                },
                { sourceDocumentNo, inspectionTaskId },
              )
              return { success: true, data: { items, total: items.length } }
            },
          }
        : {}),
      enabled: hasBusinessContext(filters),
    }
  })
  const scopeReady = computed(() => hasBusinessContext(filters))
  const currentResponse = useScopeBoundListResponse(
    () => tasksQuery.data.value,
    () => `${filters.organizationId.trim()}:${filters.environmentId.trim()}`,
    scopeReady,
  )
  const lastUpdatedAt = useListFreshness(currentResponse, scopeReady)
  const { hasSuccessfulResponse, hasFailedResponse } = useListResponseState(
    currentResponse,
    scopeReady,
    () => tasksQuery.isLoading.value,
  )
  const taskActions = useQualityInspectionTaskActions(filters)

  const rawTasks = computed<BusinessConsoleQualityInspectionTaskItem[]>(() => {
    const data = currentResponse.value
    return data?.success ? (data.data?.items ?? []) : []
  })
  const tasks = computed(() => {
    const filtered =
      filters.sourceType === 'all'
        ? rawTasks.value
        : rawTasks.value.filter((task) => task.sourceType === filters.sourceType)
    return sortInspectionTasks(filtered)
  })

  return {
    filters,
    hasLocator,
    tasks,
    total: computed(() =>
      currentResponse.value?.success ? (currentResponse.value.data?.total ?? 0) : 0,
    ),
    pending: tasksQuery.isLoading,
    error: tasksQuery.error,
    lastUpdatedAt,
    hasSuccessfulResponse,
    hasFailedResponse,
    startInspection: taskActions.startInspection,
    startInspectionError: taskActions.startInspectionError,
    startInspectionPending: taskActions.startInspectionPending,
    claimInspectionTask: taskActions.claimInspectionTask,
    assignInspectionTask: taskActions.assignInspectionTask,
    refreshTasks: () => refetchWithBusinessContext(filters, tasksQuery),
  }
}

export function useQualityInspectionTaskActions(filters: BusinessContextFields) {
  const auth = useAuthStore()
  const queryCache = useQueryCache()
  const startInspectionPending = shallowRef(false)
  const startInspectionError = shallowRef<unknown>()
  const refreshInspectionTasks = () =>
    queryCache
      .invalidateQueries({
        predicate: isBusinessQuery('listBusinessConsoleQualityInspectionTasks'),
      })
      .catch(ignoreBackgroundError)

  async function claimInspectionTask(inspectionTaskId: string, expectedVersion: number) {
    const result = await claimBusinessConsoleQualityInspectionTask({
      path: { inspectionTaskId },
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
      },
      body: {
        idempotencyKey: newQualityTaskActionKey('claim'),
        expectedVersion,
      },
      throwOnError: true,
    })
    await refreshInspectionTasks()
    return result
  }

  async function assignInspectionTask(
    inspectionTaskId: string,
    assignedInspectorUserId: string,
    reason: string,
    expectedVersion: number,
  ) {
    const result = await assignBusinessConsoleQualityInspectionTask({
      path: { inspectionTaskId },
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
      },
      body: {
        assignedInspectorUserId,
        reason,
        idempotencyKey: newQualityTaskActionKey('assignment'),
        expectedVersion,
      },
      throwOnError: true,
    })
    await refreshInspectionTasks()
    return result
  }

  async function startInspection(inspectionTaskId: string, body: QualityInspectionSubmitIntent) {
    const { idempotencyKey: suppliedKey, ...intent } = body
    const scope = {
      principalId: auth.principal?.principalId ?? auth.sessionId ?? 'unrestored-session',
      organizationId: filters.organizationId,
      environmentId: filters.environmentId,
      operationType: 'quality.inspection-task.submit',
      payloadFingerprint: `${inspectionTaskId}:${JSON.stringify(intent)}`,
    }
    const restored = peekPendingBusinessIntent(scope)
    const pending = acquirePendingBusinessIntent(
      scope,
      () => suppliedKey ?? newQualitySubmitIntentKey(),
      intent,
    )
    const stableIntent = requirePendingPayloadSnapshot<
      Omit<QualityInspectionSubmitIntent, 'idempotencyKey'>
    >(pending.payloadSnapshot, '创建检验记录')
    startInspectionPending.value = true
    startInspectionError.value = undefined
    try {
      const result = await completePendingBusinessIntent(scope, async () => {
        const envelope = await executeLifecycleAction({
          readLatest: async () => {
            const response = await listBusinessConsoleQualityInspectionTasks({
              query: {
                organizationId: filters.organizationId,
                environmentId: filters.environmentId,
                inspectionTaskId,
                skip: 0,
                take: 1,
              },
              throwOnError: false,
            })
            if (response.error !== undefined) throw response.error
            if (!response.data?.success) throw response.data ?? new Error('读取待检任务失败')
            const item = response.data.data?.items?.find(
              (candidate) => candidate.inspectionTaskId === inspectionTaskId,
            )
            return item
              ? {
                  domain: 'quality-inspection-task' as const,
                  action: 'create-record' as const,
                  facts: {
                    status: item.status,
                    inspectionRecordId: item.inspectionRecordId,
                    idempotentReplay: Boolean(restored),
                  },
                }
              : undefined
          },
          command: () =>
            createBusinessConsoleQualityInspectionRecordFromTask({
              path: { inspectionTaskId },
              query: {
                organizationId: filters.organizationId,
                environmentId: filters.environmentId,
              },
              body: { ...stableIntent, idempotencyKey: pending.idempotencyKey },
              throwOnError: false,
            }),
        })
        if (!envelope) throw new Error('创建检验记录未返回业务信封')
        await confirmBusinessConsoleOperation(envelope, {
          expectedOperationType: 'quality.inspection-task.submit',
          expectedIdempotencyKey: pending.idempotencyKey,
          expectedResourceIdSelector: (candidate) => candidate.data?.inspectionRecordId,
        })
        return envelope
      })
      await refreshInspectionTasks()
      return result
    } catch (error) {
      startInspectionError.value = error
      throw error
    } finally {
      startInspectionPending.value = false
    }
  }

  return {
    claimInspectionTask,
    assignInspectionTask,
    startInspection,
    startInspectionError,
    startInspectionPending,
    refreshInspectionTasks,
  }
}
