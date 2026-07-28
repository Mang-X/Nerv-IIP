import {
  listBusinessConsoleDeviceAssetsQueryOptions,
  type BusinessConsoleResourceItem,
  type BusinessConsoleResourceListEnvelope,
} from '@nerv-iip/api-client'
import { useAuthStore } from '@/stores/auth'
import { useQuery } from '@pinia/colada'
import { computed, reactive } from 'vue'

const PAGE_SIZE = 20

export interface DeviceAssetDirectoryFilters {
  keyword: string
  skip: number
  take: number
}

function listDeviceAssets(envelope: BusinessConsoleResourceListEnvelope | undefined) {
  if (!envelope?.success) return []
  return (envelope.data?.resources ?? [])
    .filter(
      (item): item is BusinessConsoleResourceItem & { deviceAssetId: string } =>
        item.active !== false &&
        typeof item.deviceAssetId === 'string' &&
        item.deviceAssetId.trim().length > 0,
    )
    .map((item) => ({ ...item, deviceAssetId: item.deviceAssetId.trim() }))
}

function listTotal(envelope: BusinessConsoleResourceListEnvelope | undefined) {
  if (!envelope?.success) return 0
  return envelope.data?.total ?? 0
}

/**
 * PDA 设备目录查询：只负责 principal scope、服务端关键词和有界分页。
 * 选择展示由 DeviceAssetPicker 负责，业务表单只接收稳定 deviceAssetId。
 */
export function useBusinessDeviceDirectory() {
  const auth = useAuthStore()
  const organizationId = computed(() => auth.principal?.organizationId ?? '')
  const environmentId = computed(() => auth.principal?.environmentId ?? '')
  const scopeReady = computed(() => Boolean(organizationId.value && environmentId.value))
  const deviceAssetFilters = reactive<DeviceAssetDirectoryFilters>({
    keyword: '',
    skip: 0,
    take: PAGE_SIZE,
  })

  const directoryQuery = useQuery(() => {
    const keyword = deviceAssetFilters.keyword.trim()
    return {
      ...listBusinessConsoleDeviceAssetsQueryOptions({
        query: {
          organizationId: organizationId.value,
          environmentId: environmentId.value,
          includeDisabled: false,
          skip: deviceAssetFilters.skip,
          take: deviceAssetFilters.take,
          ...(keyword ? { keyword } : {}),
        },
      }),
      enabled: scopeReady.value,
    }
  })

  const response = computed(
    () => directoryQuery.data.value as BusinessConsoleResourceListEnvelope | undefined,
  )
  const deviceAssetsTotal = computed(() => listTotal(response.value))
  const canPreviousPage = computed(() => deviceAssetFilters.skip > 0)
  const canNextPage = computed(
    () => deviceAssetFilters.skip + deviceAssetFilters.take < deviceAssetsTotal.value,
  )

  function search(keyword: string) {
    deviceAssetFilters.keyword = keyword.trim()
    deviceAssetFilters.skip = 0
  }

  function previousPage() {
    deviceAssetFilters.skip = Math.max(0, deviceAssetFilters.skip - deviceAssetFilters.take)
  }

  function nextPage() {
    if (canNextPage.value) {
      deviceAssetFilters.skip += deviceAssetFilters.take
    }
  }

  return {
    deviceAssets: computed(() => listDeviceAssets(response.value)),
    deviceAssetsTotal,
    deviceAssetsPending: directoryQuery.isLoading,
    deviceAssetsError: directoryQuery.error,
    deviceAssetFilters,
    scopeReady,
    canPreviousPage,
    canNextPage,
    search,
    previousPage,
    nextPage,
    refreshDeviceAssets: () =>
      scopeReady.value ? directoryQuery.refetch() : Promise.resolve(undefined),
  }
}
