import {
  createBusinessConsoleDepartmentMutationOptions,
  createBusinessConsoleProductionLineMutationOptions,
  createBusinessConsoleShiftMutationOptions,
  createBusinessConsoleSiteMutationOptions,
  createBusinessConsoleSkuMutationOptions,
  createBusinessConsoleTeamMutationOptions,
  createBusinessConsoleWorkCalendarMutationOptions,
  createBusinessConsoleWorkCenterMutationOptions,
  disableBusinessConsoleMasterDataResourceMutationOptions,
  enableBusinessConsoleMasterDataResourceMutationOptions,
  listBusinessConsoleMasterDataResourcesQueryOptions,
  listBusinessConsoleSkusQueryOptions,
  registerBusinessConsoleDeviceAssetMutationOptions,
  updateBusinessConsoleMasterDataResourceMutationOptions,
  type BusinessConsoleCreateSkuRequest,
  type BusinessConsoleResourceItem,
  type BusinessConsoleResourceListEnvelope,
  type BusinessConsoleUpdateMasterDataResourceRequest,
} from '@nerv-iip/api-client'
import { useMutation, useQuery, useQueryCache, type UseMutationOptions, type UseQueryEntry } from '@pinia/colada'
import { computed, reactive } from 'vue'

const DEFAULT_TAKE = 100

export interface BusinessContextFilters {
  organizationId: string
  environmentId: string
}

export interface MasterDataListFilters extends BusinessContextFilters {
  includeDisabled?: boolean
  skip: number
  take: number
}

export interface MasterDataResourceFilters extends MasterDataListFilters {
  resourceType: string
}

export interface BusinessMasterDataGroupDefinition {
  key: string
  title: string
  resourceType?: string
}

function defaultContext(): BusinessContextFilters {
  return reactive({
    organizationId: 'org-001',
    environmentId: 'env-dev',
  })
}

function defaultListFilters(): MasterDataListFilters {
  return reactive({
    ...defaultContext(),
    skip: 0,
    take: DEFAULT_TAKE,
  })
}

function defaultResourceFilters(resourceType: string): MasterDataResourceFilters {
  return reactive({
    ...defaultContext(),
    resourceType,
    skip: 0,
    take: DEFAULT_TAKE,
  })
}

function optionalQuery<TKey extends string, TValue>(key: TKey, value: TValue | undefined) {
  return value === undefined ? {} : { [key]: value }
}

function resourceItems(envelope: BusinessConsoleResourceListEnvelope | undefined) {
  if (!envelope?.success) {
    return []
  }

  return envelope.data?.resources ?? []
}

function resourceTotal(envelope: BusinessConsoleResourceListEnvelope | undefined) {
  if (!envelope?.success) {
    return 0
  }

  return envelope.data?.total ?? 0
}

function isBusinessQuery(id: string) {
  return (entry: UseQueryEntry) => {
    const keyParts = Array.isArray(entry.key) ? entry.key : [entry.key]

    return keyParts.some((part) => {
      return typeof part === 'object' && part !== null && '_id' in part && part._id === id
    })
  }
}

function ignoreBackgroundError(_error: unknown) {}

export function useBusinessSkus() {
  const filters = defaultListFilters()
  const queryCache = useQueryCache()

  const skusQuery = useQuery(() =>
    listBusinessConsoleSkusQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        ...optionalQuery('includeDisabled', filters.includeDisabled),
        skip: filters.skip,
        take: filters.take,
      },
    }),
  )

  const createSkuMutation = useMutation({
    ...createBusinessConsoleSkuMutationOptions(),
    onSuccess() {
      void queryCache
        .invalidateQueries({ predicate: isBusinessQuery('listBusinessConsoleSkus') })
        .catch(ignoreBackgroundError)
    },
  })

  return {
    createSku: (body: BusinessConsoleCreateSkuRequest) => createSkuMutation.mutateAsync({ body }),
    createSkuError: createSkuMutation.error,
    createSkuPending: createSkuMutation.isLoading,
    filters,
    refreshSkus: skusQuery.refetch,
    skus: computed<BusinessConsoleResourceItem[]>(() => resourceItems(skusQuery.data.value)),
    skusError: skusQuery.error,
    skusPending: skusQuery.isLoading,
    skusTotal: computed(() => resourceTotal(skusQuery.data.value)),
  }
}

export function useBusinessMasterDataResources(resourceType: string) {
  const filters = defaultResourceFilters(resourceType)

  const resourcesQuery = useQuery(() =>
    listBusinessConsoleMasterDataResourcesQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        resourceType: filters.resourceType,
        ...optionalQuery('includeDisabled', filters.includeDisabled),
        skip: filters.skip,
        take: filters.take,
      },
    }),
  )

  return {
    filters,
    refreshResources: resourcesQuery.refetch,
    resources: computed<BusinessConsoleResourceItem[]>(() =>
      resourceItems(resourcesQuery.data.value),
    ),
    resourcesError: resourcesQuery.error,
    resourcesPending: resourcesQuery.isLoading,
    resourcesTotal: computed(() => resourceTotal(resourcesQuery.data.value)),
  }
}

export function useBusinessMasterDataGroups(definitions: BusinessMasterDataGroupDefinition[]) {
  const filters = defaultListFilters()
  const queries = definitions.map((definition) =>
    useQuery(() =>
      listBusinessConsoleMasterDataResourcesQueryOptions({
        query: {
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
          resourceType: definition.resourceType ?? definition.key,
          ...optionalQuery('includeDisabled', filters.includeDisabled),
          skip: filters.skip,
          take: filters.take,
        },
      }),
    ),
  )

  return {
    filters,
    groups: computed(() =>
      definitions.map((definition, index) => ({
        ...definition,
        resourceType: definition.resourceType ?? definition.key,
        rows: resourceItems(queries[index]?.data.value),
        total: resourceTotal(queries[index]?.data.value),
      })),
    ),
    groupsError: computed(() => queries.map((query) => query.error.value).find(Boolean)),
    groupsPending: computed(() => queries.some((query) => query.isLoading.value)),
    groupsTotal: computed(() =>
      queries.reduce((total, query) => total + resourceTotal(query.data.value), 0),
    ),
    refreshGroups: () => Promise.all(queries.map((query) => query.refetch())),
  }
}

// 各工厂/组织资源的「新建」mutation options（barrel 已接出，generated 提供）。
const RESOURCE_CREATE_OPTIONS = {
  'site': createBusinessConsoleSiteMutationOptions,
  'production-line': createBusinessConsoleProductionLineMutationOptions,
  'work-center': createBusinessConsoleWorkCenterMutationOptions,
  'device-asset': registerBusinessConsoleDeviceAssetMutationOptions,
  'shift': createBusinessConsoleShiftMutationOptions,
  'work-calendar': createBusinessConsoleWorkCalendarMutationOptions,
  'team': createBusinessConsoleTeamMutationOptions,
  'department': createBusinessConsoleDepartmentMutationOptions,
} as const

export type MasterDataResourceType = keyof typeof RESOURCE_CREATE_OPTIONS

/**
 * 单类基础数据资源的「列表 + 新建」。列表走通用 resources 端点（仅 5 字段，见
 * docs/architecture/master-data-module-product-design.md §0/§7），新建走各自 create 端点。
 * 编辑/停用待后端 #344；本 Phase 1 只做查 + 增。
 */
export function useMasterDataResource<TBody>(resourceType: MasterDataResourceType) {
  const filters = defaultResourceFilters(resourceType)
  const queryCache = useQueryCache()

  const listQuery = useQuery(() =>
    listBusinessConsoleMasterDataResourcesQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        resourceType: filters.resourceType,
        ...optionalQuery('includeDisabled', filters.includeDisabled),
        skip: filters.skip,
        take: filters.take,
      },
    }),
  )

  // 各实体 mutation options 仅 body 泛型不同，统一经本工厂收敛，故此处收窄类型。
  const createMutation = useMutation({
    ...RESOURCE_CREATE_OPTIONS[resourceType](),
    onSuccess() {
      void queryCache
        .invalidateQueries({ predicate: isBusinessQuery('listBusinessConsoleMasterDataResources') })
        .catch(ignoreBackgroundError)
    },
  } as unknown as UseMutationOptions)

  return {
    filters,
    items: computed<BusinessConsoleResourceItem[]>(() => resourceItems(listQuery.data.value)),
    total: computed(() => resourceTotal(listQuery.data.value)),
    error: listQuery.error,
    pending: listQuery.isLoading,
    refresh: listQuery.refetch,
    create: (body: TBody) =>
      (createMutation.mutateAsync as unknown as (vars: { body: TBody }) => Promise<unknown>)({ body }),
    createError: createMutation.error,
    createPending: createMutation.isLoading,
  }
}

/**
 * 任一基础数据资源的「编辑 / 停用 / 启用」——走 #344 的通用端点
 * `PATCH|POST /master-data/resources/{resourceType}/{code}[/disable|/enable]`。
 * 与列表 hook 解耦,页面在 RowActions 里组合使用;成功后失效相关列表查询。
 */
export function useMasterDataResourceActions(resourceType: string) {
  const ctx = defaultContext()
  const queryCache = useQueryCache()
  function invalidate() {
    for (const id of ['listBusinessConsoleMasterDataResources', 'listBusinessConsoleSkus']) {
      void queryCache.invalidateQueries({ predicate: isBusinessQuery(id) }).catch(ignoreBackgroundError)
    }
  }
  const updateMutation = useMutation({ ...updateBusinessConsoleMasterDataResourceMutationOptions(), onSuccess: invalidate } as unknown as UseMutationOptions)
  const disableMutation = useMutation({ ...disableBusinessConsoleMasterDataResourceMutationOptions(), onSuccess: invalidate } as unknown as UseMutationOptions)
  const enableMutation = useMutation({ ...enableBusinessConsoleMasterDataResourceMutationOptions(), onSuccess: invalidate } as unknown as UseMutationOptions)
  const withCtx = (extra: Record<string, unknown>) => ({ organizationId: ctx.organizationId, environmentId: ctx.environmentId, ...extra })
  const callPathBody = (m: typeof updateMutation, code: string, extra: Record<string, unknown>) =>
    (m.mutateAsync as unknown as (vars: unknown) => Promise<unknown>)({ path: { resourceType, code }, body: withCtx(extra) })

  return {
    update: (code: string, patch: Partial<BusinessConsoleUpdateMasterDataResourceRequest>) => callPathBody(updateMutation, code, patch),
    disable: (code: string) => callPathBody(disableMutation, code, {}),
    enable: (code: string) => callPathBody(enableMutation, code, {}),
    updatePending: updateMutation.isLoading,
    disablePending: disableMutation.isLoading,
    enablePending: enableMutation.isLoading,
    actionError: computed(() => updateMutation.error.value ?? disableMutation.error.value ?? enableMutation.error.value),
  }
}
