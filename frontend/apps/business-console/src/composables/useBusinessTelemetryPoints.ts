import {
  createOrUpdateBusinessConsoleTelemetryTagMutationOptions,
  disableBusinessConsoleTelemetryTagMutationOptions,
  listBusinessConsoleTelemetryTagsQueryOptions,
  type BusinessConsoleTelemetryTagItem,
  type BusinessConsoleTelemetryTagListEnvelope,
} from '@nerv-iip/api-client'
import { useMutation, useQuery, useQueryCache } from '@pinia/colada'
import { computed, toValue, type MaybeRefOrGetter } from 'vue'
import { useBusinessContextStore } from '@/stores/businessContext'
import { hasBusinessContext } from './businessContextBinding'

/** 单台设备的采集点位一般几个到几十个，一页取全。 */
const POINTS_TAKE = 500

/** 采集点位新建 / 编辑的输入：按「设备 + 点位编码」upsert，控制配置整体回传。 */
export interface SaveTelemetryPointInput {
  tagKey: string
  displayName?: string | null
  valueType: string
  unitCode: string
  samplingPolicy: string
  isWritable: boolean
  controlMinValue?: number | null
  controlMaxValue?: number | null
  controlAllowedValues?: string[] | null
}

function isTagListQuery(entry: { key: unknown }) {
  const keyParts = Array.isArray(entry.key) ? entry.key : [entry.key]
  return keyParts.some(
    (part) =>
      typeof part === 'object' &&
      part !== null &&
      '_id' in part &&
      (part as { _id: string })._id === 'listBusinessConsoleTelemetryTags',
  )
}

/**
 * 设备采集点位维护（#3870）：列出这台设备的全部点位（含已停用），新建 / 编辑 / 停用。
 *
 * 点位只停用不删除：停用后不再计数、不出现在选择器里，历史采样仍可追溯。保存或停用后
 * 让所有采集点位目录失效，报警规则、历史趋势、设备控制里的点位选择器随之刷新。
 */
export function useBusinessTelemetryPoints(deviceAssetId: MaybeRefOrGetter<string>) {
  const businessContext = useBusinessContextStore()
  const queryCache = useQueryCache()
  const device = computed(() => toValue(deviceAssetId).trim())

  const pointsQuery = useQuery(() => ({
    ...listBusinessConsoleTelemetryTagsQueryOptions({
      query: {
        organizationId: businessContext.organizationId,
        environmentId: businessContext.environmentId,
        deviceAssetId: device.value,
        includeDisabled: true,
        skip: 0,
        take: POINTS_TAKE,
      },
    }),
    enabled: hasBusinessContext(businessContext) && device.value.length > 0,
  }))

  function invalidatePoints() {
    void queryCache.invalidateQueries({ predicate: isTagListQuery })
  }

  const saveMutation = useMutation({
    ...createOrUpdateBusinessConsoleTelemetryTagMutationOptions(),
    onSuccess: invalidatePoints,
  })
  const disableMutation = useMutation({
    ...disableBusinessConsoleTelemetryTagMutationOptions(),
    onSuccess: invalidatePoints,
  })

  const points = computed<BusinessConsoleTelemetryTagItem[]>(() => {
    const envelope = pointsQuery.data.value as BusinessConsoleTelemetryTagListEnvelope | undefined
    const items = envelope?.data?.items ?? []
    // 在用的排前面，同状态按编码排，停用的沉底但仍可见。
    return [...items].sort((left, right) => {
      const enabledOrder = Number(right.isEnabled !== false) - Number(left.isEnabled !== false)
      return enabledOrder !== 0
        ? enabledOrder
        : (left.tagKey ?? '').localeCompare(right.tagKey ?? '')
    })
  })

  return {
    points,
    pointsError: pointsQuery.error,
    pointsPending: pointsQuery.isLoading,
    refreshPoints: () =>
      hasBusinessContext(businessContext) && device.value
        ? pointsQuery.refetch()
        : Promise.resolve(),
    savePoint: (input: SaveTelemetryPointInput) =>
      saveMutation.mutateAsync({
        body: {
          organizationId: businessContext.organizationId,
          environmentId: businessContext.environmentId,
          deviceAssetId: device.value,
          tagKey: input.tagKey,
          displayName: input.displayName ?? null,
          valueType: input.valueType,
          unitCode: input.unitCode,
          samplingPolicy: input.samplingPolicy,
          isWritable: input.isWritable,
          controlMinValue: input.controlMinValue ?? null,
          controlMaxValue: input.controlMaxValue ?? null,
          controlAllowedValues: input.controlAllowedValues ?? [],
        },
      }),
    savePointPending: saveMutation.isLoading,
    disablePoint: (tagKey: string) =>
      disableMutation.mutateAsync({
        body: {
          organizationId: businessContext.organizationId,
          environmentId: businessContext.environmentId,
          deviceAssetId: device.value,
          tagKey,
        },
      }),
    disablePointPending: disableMutation.isLoading,
  }
}
