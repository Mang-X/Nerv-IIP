import {
  getConsoleInstanceDetailQueryOptions,
  getConsoleOperationTaskQueryOptions,
  listConsoleInstancesQueryOptions,
  restartConsoleInstanceMutationOptions,
  type InstanceDetailResponse,
  type InstanceListItem,
  type OperationTaskResponse,
} from '@nerv-iip/api-client'
import { useMutation, useQuery, useQueryCache, type UseQueryEntry } from '@pinia/colada'
import { useAuthStore } from '@/stores/auth'
import { storeToRefs } from 'pinia'
import { computed, shallowRef, toValue, type MaybeRefOrGetter } from 'vue'

const PAGE_NUMBER = 1
const PAGE_SIZE = 20
const ignoreBackgroundError = (_error: unknown) => {}

interface ConsoleContext {
  environmentId: string
  organizationId: string
}

const EMPTY_CONSOLE_CONTEXT: ConsoleContext = {
  environmentId: '',
  organizationId: '',
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
    throw new Error('Console organization and environment context is unavailable.')
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

  const listQuery = useQuery(() => ({
    ...listConsoleInstancesQueryOptions({
      query: {
        ...toConsoleQueryContext(consoleContext.value),
        pageNumber: PAGE_NUMBER,
        pageSize: PAGE_SIZE,
      },
    } as Parameters<typeof listConsoleInstancesQueryOptions>[0]),
    enabled: Boolean(consoleContext.value),
  }))

  const instances = computed<InstanceListItem[]>(() => listQuery.data.value?.items ?? [])
  const effectiveInstanceKey = computed(
    () => selectedInstanceKey.value ?? instances.value[0]?.instanceKey ?? '',
  )

  const detailQuery = useQuery(() => ({
    ...getConsoleInstanceDetailQueryOptions({
      path: {
        instanceKey: effectiveInstanceKey.value,
      },
      query: {
        ...toConsoleQueryContext(consoleContext.value),
      },
    } as Parameters<typeof getConsoleInstanceDetailQueryOptions>[0]),
    enabled: Boolean(consoleContext.value) && effectiveInstanceKey.value.length > 0,
  }))

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
    const context = getRequiredConsoleContext(consoleContext.value)

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
  }

  return {
    latestOperationTask,
    restartError: restartMutation.error,
    restartInstance,
    restartPending: restartMutation.isLoading,
  }
}

export function useOperationTask(operationTaskId: MaybeRefOrGetter<string>) {
  const consoleContext = useConsoleContext()

  const taskQuery = useQuery(() => {
    const id = toValue(operationTaskId)

    return {
      ...getConsoleOperationTaskQueryOptions({
        path: {
          operationTaskId: id,
        },
        query: {
          ...toConsoleQueryContext(consoleContext.value),
        },
      } as Parameters<typeof getConsoleOperationTaskQueryOptions>[0]),
      autoRefetch: 1000,
      enabled: Boolean(consoleContext.value) && id.length > 0,
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

function toConsoleQueryContext(context: ConsoleContext | undefined) {
  return context ?? EMPTY_CONSOLE_CONTEXT
}
