import {
  listBusinessConsoleSearchableDirectory,
  listBusinessConsoleSearchableDirectoryQueryKey,
  type BusinessConsoleSearchableDirectoryEnvelope,
  type ListBusinessConsoleSearchableDirectoryData,
} from '@nerv-iip/api-client'
import type { EntityPickerOption } from '@nerv-iip/ui'
import { useInfiniteQuery } from '@pinia/colada'
import { refDebounced } from '@vueuse/core'
import { computed, ref, shallowRef, toValue, watch, type MaybeRefOrGetter } from 'vue'
import { useBusinessContextStore } from '@/stores/businessContext'
import { hasBusinessContext } from './businessContextBinding'
import { useBusinessMasterDataResources } from './useBusinessMasterData'

export type SearchableDirectoryType =
  ListBusinessConsoleSearchableDirectoryData['path']['directoryType']

/** 目录按页取：先列第一页，滚到底再取下一页；匹配总数如实交给选择器提示。 */
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

  const directoryQuery = computed(() => {
    const skuCode = toValue(options.skuCode)?.trim()
    return {
      organizationId: context.organizationId,
      environmentId: context.environmentId,
      pageSize: PAGE_SIZE,
      rankingMode: 'default' as const,
      ...(keyword.value ? { keyword: keyword.value } : {}),
      ...(skuCode ? { skuCode } : {}),
    }
  })

  const query = useInfiniteQuery({
    key: () =>
      listBusinessConsoleSearchableDirectoryQueryKey({
        path: { directoryType },
        query: { ...directoryQuery.value, pageIndex: 1 },
      }),
    query: async ({ pageParam, signal }) => {
      const { data } = await listBusinessConsoleSearchableDirectory({
        path: { directoryType },
        query: { ...directoryQuery.value, pageIndex: pageParam },
        signal,
        throwOnError: true,
      })
      return data as BusinessConsoleSearchableDirectoryEnvelope
    },
    initialPageParam: 1,
    getNextPageParam: (lastPage, allPages, lastPageParam) => {
      const loaded = allPages.reduce((count, page) => count + (page.data?.items?.length ?? 0), 0)
      return loaded < (lastPage.data?.total ?? 0) ? lastPageParam + 1 : null
    },
    enabled: () => hasBusinessContext(context),
  })

  const pages = computed(() => query.data.value?.pages ?? [])

  const results = computed<EntityPickerOption[]>(() => {
    const seen = new Set<string>()
    const rows: EntityPickerOption[] = []
    for (const item of pages.value.flatMap((page) => page.data?.items ?? [])) {
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
    // 只有第一页还没回来时算加载中；取下一页时列表照常显示、继续滚动。
    pending: computed(() => query.isPending.value && query.isLoading.value),
    total: computed(() => pages.value.at(-1)?.data?.total ?? 0),
    /** 选择器滚到底部时取下一页；上一页还在路上时不重复发。 */
    loadMore() {
      if (query.hasNextPage.value && !query.isLoading.value) void query.loadNextPage()
    },
    /** 就地新建的项：目录刷新回来之前（或不在第一页时）已选项也显示名称。 */
    remember(option: EntityPickerOption) {
      known.value = new Map(known.value).set(option.value, option)
    },
  }
}

/** 按上级收窄候选：给了哪一级就只留挂在这一级下的项，空值表示这一级不限。 */
export interface DirectoryParent {
  siteCode?: string
  workshopCode?: string
  lineCode?: string
}

export type MasterDataListPickerType =
  | 'shift'
  | 'site'
  | 'workshop'
  | 'production-line'
  | 'work-center'
  | 'station'

const PARENT_KEYS = ['siteCode', 'workshopCode', 'lineCode'] as const

/**
 * 基础数据资源列表 → 选择器候选，整表取回，搜索交给选择器自带的本地过滤。两种用法：
 * - 班次、产线、工厂不在可搜目录里（owner 裁定不给目录端点加类型）；
 * - 表单里的层级字段要按已选上级收窄（`parent`）。可搜目录的 scope 是授权范围，
 *   不是层级过滤，也没有按产线收窄的参数，所以这类字段也走这里。
 * 这些类型在一个组织内是几条到几十条，整表取回在本地收窄。
 */
export function useMasterDataListPicker(
  resourceType: MasterDataListPickerType,
  parent?: MaybeRefOrGetter<DirectoryParent | undefined>,
) {
  const catalog = useBusinessMasterDataResources(resourceType)
  catalog.filters.take = 500
  const created = shallowRef<EntityPickerOption>()
  const rows = computed<EntityPickerOption[]>(() => {
    const within = toValue(parent)
    return catalog.resources.value
      .filter((row) => row.active !== false)
      .filter((row) => PARENT_KEYS.every((key) => !within?.[key] || row[key] === within[key]))
      .flatMap((row) => {
        const value = row.code?.trim()
        return value ? [{ value, label: row.displayName?.trim() || value }] : []
      })
  })
  return {
    serverSearch: false as const,
    // 新建项只在列表还没刷新回它时补进来；刷新回来后按上级收窄，换了上级就不再出现。
    options: computed(() => {
      const extra = created.value
      return extra && !catalog.resources.value.some((row) => row.code?.trim() === extra.value)
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
