import { getBusinessConsoleQualityNcrQueryOptions } from '@nerv-iip/api-client'
import type { BusinessConsoleQualityItem } from '@nerv-iip/api-client'
import { useAuthStore } from '@/stores/auth'
import { useQuery } from '@pinia/colada'
import { computed, toValue, type MaybeRefOrGetter } from 'vue'

/**
 * 按 id 读单条 NCR（PDA 检验结果页「已触发 NCR」→ 打开 NCR 详情的互链数据源）。
 *
 * - org/env 取登录主体；`ncrId` 为空时不发请求。
 * - 网关 facade `GET /quality/ncrs/{ncrId}` 按 inspection-records.read 门控（检验员可读其检验触发的
 *   NCR），并复用 NCR 列表读做租户隔离——越权/不存在的 id 返回 404，页面呈错误态。
 */
export function useNonconformanceReport(ncrId: MaybeRefOrGetter<string | undefined>) {
  const auth = useAuthStore()
  const organizationId = computed(() => auth.principal?.organizationId ?? '')
  const environmentId = computed(() => auth.principal?.environmentId ?? '')
  const resolvedNcrId = computed(() => (toValue(ncrId) ?? '').trim())
  const enabled = computed(() =>
    Boolean(organizationId.value && environmentId.value && resolvedNcrId.value),
  )

  const query = useQuery(() => ({
    ...getBusinessConsoleQualityNcrQueryOptions({
      path: { ncrId: resolvedNcrId.value },
      query: {
        organizationId: organizationId.value,
        environmentId: environmentId.value,
      },
    }),
    enabled: enabled.value,
  }))

  const ncr = computed<BusinessConsoleQualityItem | null>(() => {
    const envelope = query.data.value
    return envelope?.success ? (envelope.data ?? null) : null
  })

  return {
    ncr,
    pending: query.isLoading,
    error: query.error,
    refresh: () => (enabled.value ? query.refetch() : Promise.resolve()),
  }
}
