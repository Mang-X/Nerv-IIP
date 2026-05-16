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
import { computed, shallowRef, toValue, type MaybeRefOrGetter } from 'vue'

const ORGANIZATION_ID = 'org-001'
const ENVIRONMENT_ID = 'env-dev'
const PAGE_NUMBER = 1
const PAGE_SIZE = 20

function isListConsoleInstancesEntry(entry: UseQueryEntry) {
  const keyParts = Array.isArray(entry.key) ? entry.key : [entry.key]

  return keyParts.some((part) => {
    return typeof part === 'object'
      && part !== null
      && '_id' in part
      && part._id === 'listConsoleInstances'
  })
}

export function useConsoleInstances() {
  const selectedInstanceKey = shallowRef<string>()

  const listQuery = useQuery(() => listConsoleInstancesQueryOptions({
    query: {
      organizationId: ORGANIZATION_ID,
      environmentId: ENVIRONMENT_ID,
      pageNumber: PAGE_NUMBER,
      pageSize: PAGE_SIZE,
    },
  } as Parameters<typeof listConsoleInstancesQueryOptions>[0]))

  const instances = computed<InstanceListItem[]>(() => listQuery.data.value?.items ?? [])
  const effectiveInstanceKey = computed(() => selectedInstanceKey.value ?? instances.value[0]?.instanceKey ?? '')

  const detailQuery = useQuery(() => ({
    ...getConsoleInstanceDetailQueryOptions({
      path: {
        instanceKey: effectiveInstanceKey.value,
      },
      query: {
        organizationId: ORGANIZATION_ID,
        environmentId: ENVIRONMENT_ID,
      },
    } as Parameters<typeof getConsoleInstanceDetailQueryOptions>[0]),
    enabled: effectiveInstanceKey.value.length > 0,
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
  const queryCache = useQueryCache()

  const restartMutation = useMutation({
    ...restartConsoleInstanceMutationOptions(),
    async onSuccess(task) {
      latestOperationTask.value = task
      await queryCache.invalidateQueries({ predicate: isListConsoleInstancesEntry })
    },
  })

  async function restartInstance(instanceKey: string) {
    const task = await restartMutation.mutateAsync({
      body: {
        organizationId: ORGANIZATION_ID,
        environmentId: ENVIRONMENT_ID,
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
  const taskQuery = useQuery(() => {
    const id = toValue(operationTaskId)

    return {
      ...getConsoleOperationTaskQueryOptions({
        path: {
          operationTaskId: id,
        },
      } as Parameters<typeof getConsoleOperationTaskQueryOptions>[0]),
      autoRefetch: 1000,
      enabled: id.length > 0,
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
