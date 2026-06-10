import {
  listBusinessConsoleEquipmentAlarmsQueryOptions,
  type BusinessConsoleEquipmentAlarmListEnvelope,
  type EquipmentRuntimeAlarmSummary,
} from '@nerv-iip/api-client'
import { useAuthStore } from '@/stores/auth'
import { useQuery } from '@pinia/colada'
import { computed, reactive } from 'vue'

const DEFAULT_TAKE = 100

export interface EquipmentAlarmFilters {
  organizationId: string
  environmentId: string
  skip: number
  take: number
  deviceAssetId?: string
}

function optionalQuery<TKey extends string, TValue>(key: TKey, value: TValue | undefined) {
  return value === undefined || value === '' ? {} : { [key]: value }
}

function listItems<TItem>(envelope: { success?: boolean, data?: { items?: TItem[] } | null } | undefined) {
  if (!envelope?.success) {
    return []
  }
  return envelope.data?.items ?? []
}

function listTotal(envelope: { success?: boolean, data?: { total?: number } | null } | undefined) {
  if (!envelope?.success) {
    return 0
  }
  return envelope.data?.total ?? 0
}

/**
 * 设备报警（只读）数据封装：镜像 business-console `useBusinessEquipment` 的报警映射，
 * 但 org/env 取登录主体 `useAuthStore().principal`（PDA 无 business-context store）。
 * scope 为空（未登录 / 主体缺 org/env）时不发请求（`enabled:false`）。
 */
export function useBusinessEquipmentAlarms(initialFilters: Partial<EquipmentAlarmFilters> = {}) {
  const auth = useAuthStore()
  const filters = reactive<EquipmentAlarmFilters>({
    organizationId: auth.principal?.organizationId ?? '',
    environmentId: auth.principal?.environmentId ?? '',
    skip: 0,
    take: DEFAULT_TAKE,
    ...initialFilters,
  })

  const organizationId = computed(() => auth.principal?.organizationId ?? '')
  const environmentId = computed(() => auth.principal?.environmentId ?? '')
  const scopeReady = computed(() => Boolean(organizationId.value && environmentId.value))

  const alarmsQuery = useQuery(() => ({
    ...listBusinessConsoleEquipmentAlarmsQueryOptions({
      query: {
        organizationId: organizationId.value,
        environmentId: environmentId.value,
        skip: filters.skip,
        take: filters.take,
        ...optionalQuery('deviceAssetId', filters.deviceAssetId),
      },
    }),
    enabled: scopeReady.value,
  }))

  return {
    filters,
    alarms: computed<EquipmentRuntimeAlarmSummary[]>(() =>
      listItems<EquipmentRuntimeAlarmSummary>(
        alarmsQuery.data.value as BusinessConsoleEquipmentAlarmListEnvelope | undefined,
      ),
    ),
    total: computed(() => listTotal(alarmsQuery.data.value as BusinessConsoleEquipmentAlarmListEnvelope | undefined)),
    pending: alarmsQuery.isLoading,
    error: alarmsQuery.error,
    refresh: () => (scopeReady.value ? alarmsQuery.refetch() : Promise.resolve()),
  }
}
