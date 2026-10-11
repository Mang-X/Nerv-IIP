import { listBusinessConsoleErpWorkOrderCostsQueryOptions } from '@nerv-iip/api-client'
import type { EntityPickerOption } from '@nerv-iip/ui'
import { useQuery } from '@pinia/colada'
import { refDebounced } from '@vueuse/core'
import { computed, ref, toValue, type MaybeRefOrGetter } from 'vue'
import { useBusinessContextStore } from '@/stores/businessContext'
import { hasBusinessContext } from './businessContextBinding'

/** 一次取回的候选条数；更多的靠搜索收窄，匹配总数如实交给选择器提示。 */
const PAGE_SIZE = 50
const GUID_SHAPED = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

/** MES 公开 workOrderId 是业务编号；内部 GUID 与缺失编号不作为展示兜底。 */
function workOrderLabel(workOrderId: string) {
  const value = workOrderId.trim()
  return value && !GUID_SHAPED.test(value) ? value : '—'
}

/**
 * 财务页的工单候选：取 ERP 自己归集过成本的工单，财务读权限即可搜到，不依赖制造执行的工单权限。
 * 服务端按工单号 / 物料编码模糊搜索；已选工单不在当前结果里时补一条占位项，避免显示成「未选择」。
 */
export function useErpWorkOrderCostPicker(selected: MaybeRefOrGetter<string>) {
  const context = useBusinessContextStore()
  const search = ref('')
  const keyword = refDebounced(
    computed(() => search.value.trim()),
    300,
  )
  const query = useQuery(() => ({
    ...listBusinessConsoleErpWorkOrderCostsQueryOptions({
      query: {
        organizationId: context.organizationId,
        environmentId: context.environmentId,
        take: PAGE_SIZE,
        ...(keyword.value ? { keyword: keyword.value } : {}),
      },
    }),
    enabled: hasBusinessContext(context),
  }))
  const response = computed(() => (query.data.value?.success ? query.data.value.data : undefined))
  const options = computed<EntityPickerOption[]>(() => {
    const rows = (response.value?.items ?? []).map((item) => ({
      value: item.workOrderId,
      label: workOrderLabel(item.workOrderId),
      hint: item.costKind === 'rework' ? `${item.skuCode} · 返工` : item.skuCode,
    }))
    const current = toValue(selected).trim()
    if (!current || rows.some((row) => row.value === current)) return rows
    return [{ value: current, label: '—' }, ...rows]
  })
  return {
    search,
    options,
    pending: query.isLoading,
    total: computed(() => response.value?.total ?? 0),
  }
}
