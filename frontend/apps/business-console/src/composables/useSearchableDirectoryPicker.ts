import {
  listBusinessConsoleSearchableDirectoryQueryOptions,
  type BusinessConsoleSearchableDirectoryEnvelope,
  type ListBusinessConsoleSearchableDirectoryData,
} from '@nerv-iip/api-client'
import type { EntityPickerOption } from '@nerv-iip/ui'
import { useQuery } from '@pinia/colada'
import { refDebounced } from '@vueuse/core'
import { computed, ref, shallowRef, toValue, watch, type MaybeRefOrGetter } from 'vue'
import { useBusinessContextStore } from '@/stores/businessContext'
import { hasBusinessContext } from './businessContextBinding'
import { useBusinessMasterDataResources } from './useBusinessMasterData'

export type SearchableDirectoryType =
  ListBusinessConsoleSearchableDirectoryData['path']['directoryType']

/** 目录端点单页上限内取一页；更多的靠搜索收窄，匹配总数如实交给选择器提示。 */
const PAGE_SIZE = 50

export interface SearchableDirectoryPickerOptions {
  /** 已选值：不在当前搜索结果里时补一条占位项，避免选择器显示成「未选择」。 */
  selected: MaybeRefOrGetter<string | undefined>
  /** 批次 / 序列号按物料收窄。 */
  skuCode?: MaybeRefOrGetter<string | undefined>
}

/**
 * 网关可搜目录（`/directories/{directoryType}`）→ `NvEntityPicker` 服务端搜索候选。
 *
 * 口径：选项 `value` 取目录项的**人读编码**（`code`），不取 `id`——设备目录的 `id` 是设备 GUID、
 * 批次 / 序列号目录的 `id` 是库存拼出来的稳定键，而页面过去手填、提交给后端的都是编码。
 */
export function useSearchableDirectoryPicker(
  directoryType: SearchableDirectoryType,
  options: SearchableDirectoryPickerOptions,
) {
  const context = useBusinessContextStore()
  const search = ref('')
  const keyword = refDebounced(
    computed(() => search.value.trim()),
    300,
  )

  const query = useQuery(() => {
    const skuCode = toValue(options.skuCode)?.trim()
    return {
      ...listBusinessConsoleSearchableDirectoryQueryOptions({
        path: { directoryType },
        query: {
          organizationId: context.organizationId,
          environmentId: context.environmentId,
          pageIndex: 1,
          pageSize: PAGE_SIZE,
          rankingMode: 'default',
          ...(keyword.value ? { keyword: keyword.value } : {}),
          ...(skuCode ? { skuCode } : {}),
        },
      }),
      enabled: hasBusinessContext(context),
    }
  })

  const response = computed(
    () => query.data.value as BusinessConsoleSearchableDirectoryEnvelope | undefined,
  )

  const results = computed<EntityPickerOption[]>(() => {
    const seen = new Set<string>()
    const rows: EntityPickerOption[] = []
    for (const item of response.value?.data?.items ?? []) {
      const value = item.code?.trim()
      // 批次 / 序列号按「物料 + 编码」分组，同一编码可能在不同物料下各出现一次。
      if (!value || seen.has(value)) continue
      seen.add(value)
      const hint = directoryHint(value, item.context)
      rows.push({ value, label: item.displayName?.trim() || value, ...(hint ? { hint } : {}) })
    }
    return rows
  })

  // 服务端搜索会替换结果窗；记住见过的项，换词后已选项仍能显示名称。
  const known = shallowRef(new Map<string, EntityPickerOption>())
  watch(results, (rows) => {
    if (!rows.length) return
    const next = new Map(known.value)
    for (const row of rows) next.set(row.value, row)
    known.value = next
  })

  const pickerOptions = computed<EntityPickerOption[]>(() => {
    const selected = toValue(options.selected)?.trim()
    if (!selected || results.value.some((row) => row.value === selected)) return results.value
    return [known.value.get(selected) ?? { value: selected, label: selected }, ...results.value]
  })

  return {
    serverSearch: true as const,
    search,
    options: pickerOptions,
    pending: query.isLoading,
    total: computed(() => response.value?.data?.total ?? 0),
    /** 就地新建的项：目录刷新回来之前（或不在第一页时）已选项也显示名称。 */
    remember(option: EntityPickerOption) {
      known.value = new Map(known.value).set(option.value, option)
    },
  }
}

/**
 * 班次、产线不在可搜目录里（owner 裁定不给目录端点加类型），改取基础数据资源列表。
 * 这两类在一个组织内是几条到几十条，整表取回，搜索交给选择器自带的本地过滤。
 */
export function useMasterDataListPicker(resourceType: 'shift' | 'production-line') {
  const catalog = useBusinessMasterDataResources(resourceType)
  catalog.filters.take = 500
  const created = shallowRef<EntityPickerOption>()
  const rows = computed<EntityPickerOption[]>(() =>
    catalog.resources.value
      .filter((row) => row.active !== false)
      .flatMap((row) => {
        const value = row.code?.trim()
        return value ? [{ value, label: row.displayName?.trim() || value }] : []
      }),
  )
  return {
    serverSearch: false as const,
    options: computed(() => {
      const extra = created.value
      return extra && !rows.value.some((row) => row.value === extra.value)
        ? [extra, ...rows.value]
        : rows.value
    }),
    pending: catalog.resourcesPending,
    /** 就地新建的项：列表刷新回来之前已选项也显示名称。 */
    remember(option: EntityPickerOption) {
      created.value = option
    },
  }
}

/** 辅助识别：所属工作中心 / 车间 / 工厂（不重复自身编码；批次、序列号的名称里已带物料）。 */
function directoryHint(code: string, context: Record<string, string | null> | undefined) {
  if (!context) return ''
  return (
    [context.workCenterCode, context.workshopCode, context.siteCode]
      .map((part) => part?.trim())
      .find((part) => part && part !== code) ?? ''
  )
}
