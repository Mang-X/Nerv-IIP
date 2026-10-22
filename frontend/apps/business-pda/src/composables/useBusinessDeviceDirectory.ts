import {
  listBusinessConsoleDeviceAssetsQueryOptions,
  listBusinessConsoleMasterDataResources,
  type BusinessConsoleResourceItem,
  type BusinessConsoleResourceListEnvelope,
} from '@nerv-iip/api-client'
import { useAuthStore } from '@/stores/auth'
import { useQuery } from '@pinia/colada'
import { computed, reactive, type Ref } from 'vue'

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

/**
 * 按设备标识解析设备称呼（设备名（编码））。MES 工序读面只回 deviceAssetId，名称要回主数据按标识精确查；
 * 只查当前屏上出现的那几台，查不到就不给称呼，调用方决定怎么说。
 */
export function useDeviceAssetNames(deviceAssetIds: Readonly<Ref<string[]>>) {
  const auth = useAuthStore()
  const ids = computed(() =>
    [...new Set(deviceAssetIds.value.map((id) => id.trim()).filter(Boolean))].sort(),
  )
  const query = useQuery(() => {
    const organizationId = auth.principal?.organizationId ?? ''
    const environmentId = auth.principal?.environmentId ?? ''
    return {
      key: ['pda-device-asset-names', organizationId, environmentId, ...ids.value],
      enabled: Boolean(organizationId && environmentId && ids.value.length),
      query: async ({ signal }) => {
        const entries = await Promise.all(
          ids.value.map(async (deviceAssetId) => {
            const response = await listBusinessConsoleMasterDataResources({
              query: {
                organizationId,
                environmentId,
                resourceType: 'device-asset',
                deviceAssetId,
                includeDisabled: true,
                take: 1,
              },
              signal,
            })
            const item = response.data?.success ? response.data.data?.resources?.[0] : undefined
            const name = item?.displayName?.trim()
            const code = item?.code?.trim()
            const label = name && code && name !== code ? `${name}（${code}）` : name || code
            return [deviceAssetId, label] as const
          }),
        )
        return new Map(entries.filter((entry): entry is readonly [string, string] => !!entry[1]))
      },
    }
  })
  return {
    resolveDeviceName: (deviceAssetId?: string | null) =>
      deviceAssetId ? query.data.value?.get(deviceAssetId.trim()) : undefined,
  }
}
