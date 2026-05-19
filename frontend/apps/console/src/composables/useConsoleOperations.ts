import {
  getConsoleInstanceDetailQueryOptions,
  getConsoleOperationTaskQueryOptions,
  listConsoleInstancesQueryOptions,
  restartConsoleInstanceMutationOptions,
  type InstanceDetailResponse,
  type InstanceListItem,
  type InstanceListResponse,
  type OperationTaskResponse,
} from '@nerv-iip/api-client'
import { useMutation, useQuery, useQueryCache, type UseQueryEntry } from '@pinia/colada'
import { useAuthStore } from '@/stores/auth'
import { storeToRefs } from 'pinia'
import { computed, shallowRef, toValue, type MaybeRefOrGetter } from 'vue'

const PAGE_NUMBER = 1
const PAGE_SIZE = 20
const CONTEXT_UNAVAILABLE_MESSAGE = 'Console organization and environment context is unavailable.'
const ignoreBackgroundError = (_error: unknown) => {}

interface ConsoleContext {
  environmentId: string
  organizationId: string
}

function useConsoleContext() {
  const auth = useAuthStore()
  const { principal } = storeToRefs(auth)

  return computed<ConsoleContext | undefined>(() => {
    const organizationId = principal.value?.organizationId
    const environmentId = principal.value?.environmentId

    if (!isNonEmptyString(organizationId) || !isNonEmptyString(environmentId)) {
      return undefined
    }

    return {
      environmentId,
      organizationId,
    }
  })
}

function getRequiredConsoleContext(context: ConsoleContext | undefined) {
  if (!context) {
    throw new Error(CONTEXT_UNAVAILABLE_MESSAGE)
  }

  return context
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0
}

function isListConsoleInstancesEntry(entry: UseQueryEntry) {
  const keyParts = Array.isArray(entry.key) ? entry.key : [entry.key]

  return keyParts.some((part) => {
    return (
      typeof part === 'object' &&
      part !== null &&
      '_id' in part &&
      part._id === 'listConsoleInstances'
    )
  })
}

export function useConsoleInstances() {
  const selectedInstanceKey = shallowRef<string>()
  const consoleContext = useConsoleContext()

  const listQuery = useQuery(() => {
    const context = consoleContext.value

    if (!context) {
      return disabledConsoleQueryOptions<InstanceListResponse>('listConsoleInstances')
    }

    return listConsoleInstancesQueryOptions({
      query: {
        ...context,
        pageNumber: PAGE_NUMBER,
        pageSize: PAGE_SIZE,
      },
    } as Parameters<typeof listConsoleInstancesQueryOptions>[0])
  })

  const instances = computed<InstanceListItem[]>(() => listQuery.data.value?.items ?? [])
  const effectiveInstanceKey = computed(
    () => selectedInstanceKey.value ?? instances.value[0]?.instanceKey ?? '',
  )

  const detailQuery = useQuery(() => {
    const context = consoleContext.value
    const instanceKey = effectiveInstanceKey.value

    if (!context || instanceKey.length === 0) {
      return disabledConsoleQueryOptions<InstanceDetailResponse>('getConsoleInstanceDetail')
    }

    return getConsoleInstanceDetailQueryOptions({
      path: {
        instanceKey,
      },
      query: {
        ...context,
      },
    } as Parameters<typeof getConsoleInstanceDetailQueryOptions>[0])
  })

  function selectInstance(instanceKey: string) {
    selectedInstanceKey.value = instanceKey
  }

  return {
    detail: computed<InstanceDetailResponse | undefined>(() => detailQuery.data.value),
    detailError: detailQuery.error,
    detailPending: detailQuery.isLoading,
    effectiveInstanceKey,
    instances,
    listError: listQuery.error,
    listPending: listQuery.isLoading,
    refreshInstances: listQuery.refetch,
    selectInstance,
    selectedInstanceKey,
  }
}

export function useRestartOperation() {
  const latestOperationTask = shallowRef<OperationTaskResponse>()
  const restartContextError = shallowRef<Error>()
  const consoleContext = useConsoleContext()
  const queryCache = useQueryCache()

  const restartMutation = useMutation({
    ...restartConsoleInstanceMutationOptions(),
    onSuccess(task) {
      latestOperationTask.value = task
      void queryCache
        .invalidateQueries({ predicate: isListConsoleInstancesEntry })
        .catch(ignoreBackgroundError)
    },
  })

  async function restartInstance(instanceKey: string) {
    let context: ConsoleContext

    try {
      context = getRequiredConsoleContext(consoleContext.value)
      restartContextError.value = undefined
    } catch (error) {
      restartContextError.value =
        error instanceof Error ? error : new Error(CONTEXT_UNAVAILABLE_MESSAGE)
      return undefined
    }

    try {
      const task = await restartMutation.mutateAsync({
        body: {
          ...context,
          reason: 'Console restart requested',
          idempotencyKey: globalThis.crypto?.randomUUID?.() ?? `${Date.now()}-${instanceKey}`,
        },
        path: {
          instanceKey,
        },
      } as Parameters<typeof restartMutation.mutateAsync>[0])

      latestOperationTask.value = task
      return task
    } catch (error) {
      restartContextError.value =
        error instanceof Error ? error : new Error('Unable to restart the instance.')
      return undefined
    }
  }

  return {
    latestOperationTask,
    restartError: computed(() => restartContextError.value ?? restartMutation.error.value),
    restartInstance,
    restartPending: restartMutation.isLoading,
  }
}

export function useOperationTask(operationTaskId: MaybeRefOrGetter<string>) {
  const consoleContext = useConsoleContext()

  const taskQuery = useQuery(() => {
    const id = toValue(operationTaskId)
    const context = consoleContext.value

    if (!context || id.length === 0) {
      return disabledConsoleQueryOptions<OperationTaskResponse>('getConsoleOperationTask')
    }

    return {
      ...getConsoleOperationTaskQueryOptions({
        path: {
          operationTaskId: id,
        },
        query: {
          ...context,
        },
      } as Parameters<typeof getConsoleOperationTaskQueryOptions>[0]),
      autoRefetch: 1000,
      staleTime: 1000,
    }
  })

  return {
    operationError: taskQuery.error,
    operationPending: taskQuery.isLoading,
    operationTask: taskQuery.data,
    refreshOperation: taskQuery.refetch,
  }
}

function disabledConsoleQueryOptions<TData>(id: string) {
  return {
    key: [{ _id: id, disabledReason: 'missing-console-context' }],
    query: async (): Promise<TData> => {
      throw new Error(CONTEXT_UNAVAILABLE_MESSAGE)
    },
    enabled: false,
  }
}
