import {
  createBusinessConsoleQualityInspectionRecordFromTaskMutationOptions,
  listBusinessConsoleQualityInspectionTasks,
  listBusinessConsoleQualityInspectionTasksQueryOptions,
  type BusinessConsoleCreateInspectionRecordFromTaskRequest,
  type BusinessConsoleQualityInspectionTaskItem,
} from '@nerv-iip/api-client'
import { useMutation, useQuery, useQueryCache, type UseQueryEntry } from '@pinia/colada'
import { computed, reactive } from 'vue'
import {
  bindBusinessContext,
  hasBusinessContext,
  refetchWithBusinessContext,
  type BusinessContextFields,
} from './businessContextBinding'

const DEFAULT_TAKE = 200

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
  const taskActions = useQualityInspectionTaskActions(filters)

  const rawTasks = computed<BusinessConsoleQualityInspectionTaskItem[]>(() => {
    const data = tasksQuery.data.value
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
      tasksQuery.data.value?.success ? (tasksQuery.data.value.data?.total ?? 0) : 0,
    ),
    pending: tasksQuery.isLoading,
    error: tasksQuery.error,
    startInspection: taskActions.startInspection,
    startInspectionError: taskActions.startInspectionError,
    startInspectionPending: taskActions.startInspectionPending,
    refreshTasks: () => refetchWithBusinessContext(filters, tasksQuery),
  }
}

export function useQualityInspectionTaskActions(filters: BusinessContextFields) {
  const queryCache = useQueryCache()
  const startMutation = useMutation(
    createBusinessConsoleQualityInspectionRecordFromTaskMutationOptions(),
  )

  return {
    startInspection: (
      inspectionTaskId: string,
      body: BusinessConsoleCreateInspectionRecordFromTaskRequest,
    ) =>
      startMutation
        .mutateAsync({
          path: { inspectionTaskId },
          query: {
            organizationId: filters.organizationId,
            environmentId: filters.environmentId,
          },
          body,
        })
        .then(async (result) => {
          await queryCache
            .invalidateQueries({
              predicate: isBusinessQuery('listBusinessConsoleQualityInspectionTasks'),
            })
            .catch(ignoreBackgroundError)
          return result
        }),
    startInspectionError: startMutation.error,
    startInspectionPending: startMutation.isLoading,
  }
}
