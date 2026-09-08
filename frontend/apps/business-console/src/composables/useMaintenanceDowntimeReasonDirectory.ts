import {
  listBusinessConsoleSearchableDirectoryQueryOptions,
  type BusinessConsoleSearchableDirectoryEnvelope,
} from '@nerv-iip/api-client'
import { useQuery } from '@pinia/colada'
import { computed, ref } from 'vue'
import { inlineErrorMessage, isForbiddenError } from '@/utils/notify'
import { hasBusinessContext, type BusinessContextFields } from './businessContextBinding'
import { useScopeBoundListResponse } from './useListFreshness'

/** Console 建单使用的 Maintenance 权威目录，码值原样进入 v2 请求。 */
export function useMaintenanceDowntimeReasonDirectory(scope: BusinessContextFields) {
  const keyword = ref('')
  const enabled = computed(() => hasBusinessContext(scope))
  const query = useQuery(() => ({
    ...listBusinessConsoleSearchableDirectoryQueryOptions({
      path: { directoryType: 'downtime-reason' },
      query: {
        organizationId: scope.organizationId,
        environmentId: scope.environmentId,
        pageIndex: 1,
        pageSize: 100,
        rankingMode: 'default',
        ...(keyword.value.trim() ? { keyword: keyword.value.trim() } : {}),
      },
    }),
    enabled: enabled.value,
  }))
  const response = useScopeBoundListResponse(
    () => query.data.value as BusinessConsoleSearchableDirectoryEnvelope | undefined,
    () => JSON.stringify([scope.organizationId, scope.environmentId, keyword.value.trim()]),
    enabled,
  )
  const state = computed(() => {
    if (!enabled.value) return 'scope-pending'
    if (query.error.value) {
      return isForbiddenError(query.error.value) ? 'forbidden' : 'failed'
    }
    if (query.isLoading.value || response.value === undefined) return 'loading'
    if (response.value.success !== true) return 'failed'
    return response.value.data?.items?.length ? 'ok' : 'empty'
  })
  const options = computed(() =>
    state.value === 'ok'
      ? (response.value?.data?.items ?? []).flatMap((item) =>
          item.code ? [{ value: item.code, label: item.displayName || item.code }] : [],
        )
      : [],
  )
  const message = computed(() => {
    switch (state.value) {
      case 'scope-pending':
        return '登录范围尚未就绪，暂不能读取停机原因'
      case 'loading':
        return '正在读取停机原因…'
      case 'forbidden':
        return '没有停机原因读取权限，请联系管理员开通'
      case 'failed':
        return inlineErrorMessage(query.error.value ?? response.value, '停机原因读取失败，请重试')
      case 'empty':
        return keyword.value.trim()
          ? '没有匹配的停机原因，请调整搜索词'
          : '尚未配置可用停机原因，请联系管理员配置'
      default:
        return ''
    }
  })
  return {
    keyword,
    options,
    state,
    message,
    total: computed(() => response.value?.data?.total ?? 0),
    refresh: () => query.refetch(),
  }
}
