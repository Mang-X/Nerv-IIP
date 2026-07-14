import { getBusinessConsoleQualityInspectionRecordQueryOptions } from '@nerv-iip/api-client'
import type { BusinessConsoleInspectionRecordDetailResponse } from '@nerv-iip/api-client'
import { useAuthStore } from '@/stores/auth'
import { useQuery } from '@pinia/colada'
import { computed, toValue, type MaybeRefOrGetter } from 'vue'

/**
 * 按 id 读单条检验记录（PDA NCR 详情「来源检验记录」→ 打开记录详情的互链数据源）。
 *
 * - org/env 取登录主体；`recordId` 为空时不发请求。
 * - 网关 facade `GET /quality/inspection-records/{id}` 按 inspection-records.read 门控，代理真实
 *   详情端点并由 Quality 服务端做租户过滤——越权/不存在的 id 返回 not found，页面呈错误态。
 * - 响应含回链的 `nonconformanceReportId`，支撑记录 → NCR 反向导航（双向互查）。
 */
export function useInspectionRecordDetail(recordId: MaybeRefOrGetter<string | undefined>) {
  const auth = useAuthStore()
  const organizationId = computed(() => auth.principal?.organizationId ?? '')
  const environmentId = computed(() => auth.principal?.environmentId ?? '')
  const resolvedRecordId = computed(() => (toValue(recordId) ?? '').trim())
  const enabled = computed(() =>
    Boolean(organizationId.value && environmentId.value && resolvedRecordId.value),
  )

  const query = useQuery(() => ({
    ...getBusinessConsoleQualityInspectionRecordQueryOptions({
      path: { inspectionRecordId: resolvedRecordId.value },
      query: {
        organizationId: organizationId.value,
        environmentId: environmentId.value,
      },
    }),
    enabled: enabled.value,
  }))

  const record = computed<BusinessConsoleInspectionRecordDetailResponse | null>(() => {
    const envelope = query.data.value
    return envelope?.success ? (envelope.data ?? null) : null
  })

  return {
    record,
    pending: query.isLoading,
    error: query.error,
    refresh: () => (enabled.value ? query.refetch() : Promise.resolve()),
  }
}
