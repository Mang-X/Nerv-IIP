import {
  createOrUpdateBusinessConsoleTelemetryDeviceControlBindingMutationOptions,
  disableBusinessConsoleTelemetryDeviceControlBindingMutationOptions,
  listBusinessConsoleTelemetryDeviceControlBindingsQueryOptions,
  type BusinessConsoleTelemetryDeviceControlBindingItem,
  type BusinessConsoleTelemetryDeviceControlBindingListEnvelope,
} from '@nerv-iip/api-client'
import { useMutation, useQuery, useQueryCache } from '@pinia/colada'
import { computed, reactive } from 'vue'
import { useBusinessContextStore } from '@/stores/businessContext'
import { hasBusinessContext } from './businessContextBinding'

const DEFAULT_TAKE = 100

export interface DeviceControlBindingFilters {
  deviceAssetId: string
  skip: number
  take: number
}

export interface SaveDeviceControlBindingInput {
  deviceAssetId: string
  connectorHostId: string
  instanceKey: string
}

function toContextQuery(businessContext: ReturnType<typeof useBusinessContextStore>) {
  return {
    organizationId: businessContext.organizationId,
    environmentId: businessContext.environmentId,
  }
}

function isBindingListQuery(id: string) {
  return (entry: { key: unknown }) => {
    const keyParts = Array.isArray(entry.key) ? entry.key : [entry.key]
    return keyParts.some(
      (part) =>
        typeof part === 'object' &&
        part !== null &&
        '_id' in part &&
        (part as { _id: string })._id === id,
    )
  }
}

/**
 * 设备控制通道绑定维护组合式：列表 + 新建/编辑（upsert，按 deviceAssetId）+ 停用（软停用，带原因）。
 * 命令下发的连接器主机/实例路由目标以此绑定为准，操作员不在下发时手输。
 */
export function useBusinessDeviceControlBindings(
  initialFilters: Partial<DeviceControlBindingFilters> = {},
) {
  const businessContext = useBusinessContextStore()
  const queryCache = useQueryCache()
  const filters = reactive<DeviceControlBindingFilters>({
    deviceAssetId: '',
    skip: 0,
    take: DEFAULT_TAKE,
    ...initialFilters,
  })

  const bindingsQuery = useQuery(() => ({
    ...listBusinessConsoleTelemetryDeviceControlBindingsQueryOptions({
      query: {
        ...toContextQuery(businessContext),
        deviceAssetId: filters.deviceAssetId.trim() ? filters.deviceAssetId.trim() : undefined,
        skip: filters.skip,
        take: filters.take,
      },
    }),
    enabled: hasBusinessContext(businessContext),
  }))
  const envelope = computed(
    () =>
      bindingsQuery.data.value as
        | BusinessConsoleTelemetryDeviceControlBindingListEnvelope
        | undefined,
  )

  function invalidateBindings() {
    void queryCache.invalidateQueries({
      predicate: isBindingListQuery('listBusinessConsoleTelemetryDeviceControlBindings'),
    })
  }

  const saveMutation = useMutation({
    ...createOrUpdateBusinessConsoleTelemetryDeviceControlBindingMutationOptions(),
    onSuccess() {
      invalidateBindings()
    },
  })
  const disableMutation = useMutation({
    ...disableBusinessConsoleTelemetryDeviceControlBindingMutationOptions(),
    onSuccess() {
      invalidateBindings()
    },
  })

  return {
    bindings: computed<BusinessConsoleTelemetryDeviceControlBindingItem[]>(
      () => envelope.value?.data?.items ?? [],
    ),
    bindingsError: bindingsQuery.error,
    bindingsPending: bindingsQuery.isLoading,
    bindingsTotal: computed(() => envelope.value?.data?.total ?? 0),
    filters,
    refreshBindings: () =>
      hasBusinessContext(businessContext) ? bindingsQuery.refetch() : Promise.resolve(),
    saveBinding: (input: SaveDeviceControlBindingInput) =>
      saveMutation.mutateAsync({
        body: {
          ...toContextQuery(businessContext),
          deviceAssetId: input.deviceAssetId,
          connectorHostId: input.connectorHostId,
          instanceKey: input.instanceKey,
        },
      }),
    saveBindingError: saveMutation.error,
    saveBindingPending: saveMutation.isLoading,
    disableBinding: (deviceAssetId: string, reason?: string) =>
      disableMutation.mutateAsync({
        path: { deviceAssetId },
        body: {
          ...toContextQuery(businessContext),
          reason,
        },
      }),
    disableBindingError: disableMutation.error,
    disableBindingPending: disableMutation.isLoading,
  }
}
