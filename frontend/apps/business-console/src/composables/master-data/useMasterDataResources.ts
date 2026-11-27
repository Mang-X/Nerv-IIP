import {
  createBusinessConsoleDepartmentMutationOptions,
  createBusinessConsoleProductionLineMutationOptions,
  createBusinessConsoleShiftMutationOptions,
  createBusinessConsoleStationMutationOptions,
  createBusinessConsoleSiteMutationOptions,
  createBusinessConsoleTeamMutationOptions,
  createBusinessConsoleWorkCalendarMutationOptions,
  createBusinessConsoleWorkCenterMutationOptions,
  createBusinessConsoleWorkshopMutationOptions,
  disableBusinessConsoleMasterDataResourceMutationOptions,
  enableBusinessConsoleMasterDataResourceMutationOptions,
  getBusinessConsoleMasterDataResourceDetail,
  listBusinessConsoleMasterDataResourcesQueryOptions,
  listBusinessConsoleWorkshopsQueryOptions,
  registerBusinessConsoleDeviceAssetMutationOptions,
  updateBusinessConsoleMasterDataResourceMutationOptions,
  type BusinessConsoleCreateWorkshopRequest,
  type BusinessConsoleMasterDataResourceDetail,
  type BusinessConsoleResourceItem,
  type BusinessConsoleResourceItemEnvelope,
  type BusinessConsoleSetMasterDataResourceEnabledRequest,
  type BusinessConsoleUpdateMasterDataResourceRequest,
} from '@nerv-iip/api-client'
import { useMutation, useQuery, useQueryCache, type UseMutationOptions } from '@pinia/colada'
import { computed, reactive, toValue, type MaybeRefOrGetter } from 'vue'
import {
  bindBusinessContext,
  hasBusinessContext,
  refetchWithBusinessContext,
  withBusinessContextEnabled,
} from '../shared/businessContextBinding'
import {
  DEFAULT_TAKE,
  defaultContext,
  defaultListFilters,
  ignoreBackgroundError,
  invalidateAfterCreate,
  isBusinessQuery,
  newCreateIdempotencyKey,
  optionalQuery,
  resourceItems,
  resourceTotal,
  type MasterDataListFilters,
  withCreateIdempotency,
} from './masterDataShared'

/**
 * 停用 / 重新启用的请求补丁：**`reason` 必填**（#878）。
 *
 * 生成契约里 `reason` 已是必填字段，这里不能再用 `Partial<...>` 把它弱化回可省略——
 * 那正是本票要消除的原始漏传路径：`actions.disable(code)` 编译期通过、运行期被后端稳定拒绝。
 * 类型级契约由 `useBusinessMasterData.lifecycleReason.test.ts` 的 `@ts-expect-error` 钉住。
 *
 * `organizationId` / `environmentId` / `idempotencyKey` 由动作自己补齐，调用方不必传。
 */
export interface MasterDataLifecyclePatch extends Partial<
  Omit<BusinessConsoleSetMasterDataResourceEnabledRequest, 'reason'>
> {
  /** 用户填写的业务原因，随请求提交并进生命周期审计。 */
  reason: string
}

export interface MasterDataResourceFilters extends MasterDataListFilters {
  codeSet?: string
  resourceType: string
  // #375 通用列表过滤——按需透传到 query（服务端过滤，真分页）。
  parentCode?: string
  siteCode?: string
  lineCode?: string
  workCenterCode?: string
  category?: string
  partnerType?: string
  keyword?: string
}

export interface BusinessMasterDataGroupDefinition {
  key: string
  title: string
  resourceType?: string
}

export function defaultResourceFilters(
  resourceType: string,
  codeSet?: string,
): MasterDataResourceFilters {
  return bindBusinessContext(
    reactive({
      organizationId: '',
      environmentId: '',
      ...optionalQuery('codeSet', codeSet),
      resourceType,
      skip: 0,
      take: DEFAULT_TAKE,
    }),
  )
}

/**
 * 车间的「列表 + 新建」。车间是工厂下的组织 / 区域层（工厂 → 车间 → 产线 → 工作中心）。
 * 列表走车间专属端点（返回通用 resource 列表形状：含 code/displayName/active/siteCode），
 * 新建走车间专属端点（需 code/name/siteCode，managerUserId/description 可选）。
 * onSuccess 同时失效车间列表与通用 resources 列表（产线/工作中心归属读时复用）。
 */
export function useBusinessWorkshops(all = false) {
  const filters = defaultListFilters()
  const queryCache = useQueryCache()

  const workshopsQuery = useQuery(() =>
    withBusinessContextEnabled(
      all
        ? listBusinessConsoleMasterDataResourcesQueryOptions({
            query: {
              organizationId: filters.organizationId,
              environmentId: filters.environmentId,
              resourceType: 'workshop',
              ...optionalQuery('includeDisabled', filters.includeDisabled),
              all: true,
            },
          })
        : listBusinessConsoleWorkshopsQueryOptions({
            query: {
              organizationId: filters.organizationId,
              environmentId: filters.environmentId,
              ...optionalQuery('includeDisabled', filters.includeDisabled),
              skip: filters.skip,
              take: filters.take,
            },
          }),
      filters,
    ),
  )

  const createWorkshopMutation = useMutation({
    ...createBusinessConsoleWorkshopMutationOptions(),
    onSuccess: () =>
      invalidateAfterCreate(queryCache, [
        'listBusinessConsoleWorkshops',
        'listBusinessConsoleMasterDataResources',
      ]),
  })

  return {
    createWorkshop: (body: BusinessConsoleCreateWorkshopRequest) =>
      createWorkshopMutation.mutateAsync({ body: withCreateIdempotency('workshop', body) }),
    createWorkshopError: createWorkshopMutation.error,
    createWorkshopPending: createWorkshopMutation.isLoading,
    filters,
    refreshWorkshops: () => refetchWithBusinessContext(filters, workshopsQuery),
    workshops: computed<BusinessConsoleResourceItem[]>(() =>
      resourceItems(workshopsQuery.data.value),
    ),
    workshopsError: workshopsQuery.error,
    workshopsPending: workshopsQuery.isLoading,
    workshopsTotal: computed(() => resourceTotal(workshopsQuery.data.value)),
  }
}

export function useBusinessMasterDataResources(
  resourceType: string,
  options: { codeSet?: string; enabled?: () => boolean; all?: boolean } = {},
) {
  const filters = defaultResourceFilters(resourceType, options.codeSet)
  const enabled = options.enabled ?? (() => true)

  const resourcesQuery = useQuery(() => ({
    ...listBusinessConsoleMasterDataResourcesQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        resourceType: filters.resourceType,
        ...optionalQuery('codeSet', filters.codeSet),
        ...optionalQuery('includeDisabled', filters.includeDisabled),
        ...optionalQuery('keyword', filters.keyword),
        ...optionalQuery('all', options.all),
        ...optionalQuery('partnerType', filters.partnerType),
        skip: filters.skip,
        take: filters.take,
      },
    }),
    enabled: hasBusinessContext(filters) && enabled(),
  }))
  const visibleData = computed(() => (enabled() ? resourcesQuery.data.value : undefined))

  return {
    filters,
    refreshResources: () =>
      enabled() ? refetchWithBusinessContext(filters, resourcesQuery) : Promise.resolve(undefined),
    resources: computed<BusinessConsoleResourceItem[]>(() => resourceItems(visibleData.value)),
    resourcesError: resourcesQuery.error,
    resourcesPending: resourcesQuery.isLoading,
    resourcesTotal: computed(() => resourceTotal(visibleData.value)),
  }
}

export function useBusinessMasterDataGroups(definitions: BusinessMasterDataGroupDefinition[]) {
  const filters = defaultListFilters()
  const queries = definitions.map((definition) =>
    useQuery(() =>
      withBusinessContextEnabled(
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
        filters,
      ),
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
    refreshGroups: () =>
      hasBusinessContext(filters)
        ? Promise.all(queries.map((query) => query.refetch()))
        : Promise.resolve([]),
  }
}

// 各工厂/组织资源的「新建」mutation options（barrel 已接出，generated 提供）。
const RESOURCE_CREATE_OPTIONS = {
  site: createBusinessConsoleSiteMutationOptions,
  'production-line': createBusinessConsoleProductionLineMutationOptions,
  station: createBusinessConsoleStationMutationOptions,
  'work-center': createBusinessConsoleWorkCenterMutationOptions,
  'device-asset': registerBusinessConsoleDeviceAssetMutationOptions,
  shift: createBusinessConsoleShiftMutationOptions,
  'work-calendar': createBusinessConsoleWorkCalendarMutationOptions,
  team: createBusinessConsoleTeamMutationOptions,
  department: createBusinessConsoleDepartmentMutationOptions,
} as const

export type MasterDataResourceType = keyof typeof RESOURCE_CREATE_OPTIONS

/**
 * 单类基础数据资源的「列表 + 新建」。列表走通用 resources 端点（仅 5 字段，见
 * docs/product/master-data/design.md §0/§7），新建走各自 create 端点。
 * 编辑/停用待后端 #344；本 Phase 1 只做查 + 增。
 */
export function useMasterDataResource<TBody>(
  resourceType: MasterDataResourceType,
  options: { all?: boolean } = {},
) {
  const filters = defaultResourceFilters(resourceType)

  const listQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsoleMasterDataResourcesQueryOptions({
        query: {
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
          resourceType: filters.resourceType,
          ...optionalQuery('includeDisabled', filters.includeDisabled),
          ...optionalQuery('codeSet', filters.codeSet),
          // #375 服务端过滤——页面按需写入 filters.xxx，未设则不带（保持旧行为）。
          ...optionalQuery('parentCode', filters.parentCode),
          ...optionalQuery('siteCode', filters.siteCode),
          ...optionalQuery('lineCode', filters.lineCode),
          ...optionalQuery('workCenterCode', filters.workCenterCode),
          ...optionalQuery('category', filters.category),
          ...optionalQuery('partnerType', filters.partnerType),
          ...optionalQuery('keyword', filters.keyword),
          ...optionalQuery('all', options.all),
          skip: filters.skip,
          take: filters.take,
        },
      }),
      filters,
    ),
  )

  const creation = useCreateMasterDataResource<TBody>(resourceType)

  return {
    filters,
    items: computed<BusinessConsoleResourceItem[]>(() => resourceItems(listQuery.data.value)),
    total: computed(() => resourceTotal(listQuery.data.value)),
    error: listQuery.error,
    pending: listQuery.isLoading,
    refresh: () => refetchWithBusinessContext(filters, listQuery),
    create: creation.create,
    createError: creation.error,
    createPending: creation.pending,
  }
}

/** 单类基础数据资源的「新建」。与列表分开：新增弹窗只要新建，不必连带拉一份列表。 */
export function useCreateMasterDataResource<TBody>(resourceType: MasterDataResourceType) {
  const queryCache = useQueryCache()
  // 各实体 mutation options 仅 body 泛型不同，统一经本工厂收敛，故此处收窄类型。
  const mutation = useMutation({
    ...RESOURCE_CREATE_OPTIONS[resourceType](),
    onSuccess: () => invalidateAfterCreate(queryCache, ['listBusinessConsoleMasterDataResources']),
  } as unknown as UseMutationOptions)

  return {
    create: (body: TBody) =>
      (
        mutation.mutateAsync as unknown as (vars: {
          body: TBody
        }) => Promise<BusinessConsoleResourceItemEnvelope>
      )({
        body: withCreateIdempotency(resourceType, body),
      }),
    error: mutation.error,
    pending: mutation.isLoading,
  }
}

/**
 * 任一基础数据资源的「编辑 / 停用 / 启用」——走 #344 的通用端点
 * `PATCH|POST /master-data/resources/{resourceType}/{code}[/disable|/enable]`。
 * 与列表 hook 解耦,页面在 RowActions 里组合使用;成功后失效相关列表查询。
 *
 * `codeSet` 只对身份是两段的资源有意义（当前是 `reference-data`：身份 = `{codeSet}:{code}`）。
 * 后端 `RequireReferenceDataCodeSet` 对空 codeSet 直接拒绝，缺它则停用/启用/详情一律 400
 * （#1593）。传 ref/getter 以跟随页面当前选中的分组；其余资源类型不传，请求里也不会多出这个
 * 字段（由 `optionalQuery` 保证）。
 */
export function useMasterDataResourceActions(
  resourceType: string,
  codeSet?: MaybeRefOrGetter<string | undefined>,
) {
  const ctx = defaultContext()
  const queryCache = useQueryCache()
  function invalidate() {
    for (const id of ['listBusinessConsoleMasterDataResources', 'listBusinessConsoleSkus']) {
      void queryCache
        .invalidateQueries({ predicate: isBusinessQuery(id) })
        .catch(ignoreBackgroundError)
    }
  }
  const updateMutation = useMutation({
    ...updateBusinessConsoleMasterDataResourceMutationOptions(),
    onSuccess: invalidate,
  } as unknown as UseMutationOptions)
  const disableMutation = useMutation({
    ...disableBusinessConsoleMasterDataResourceMutationOptions(),
    onSuccess: invalidate,
  } as unknown as UseMutationOptions)
  const enableMutation = useMutation({
    ...enableBusinessConsoleMasterDataResourceMutationOptions(),
    onSuccess: invalidate,
  } as unknown as UseMutationOptions)
  const withCtx = (extra: Record<string, unknown>) => ({
    organizationId: ctx.organizationId,
    environmentId: ctx.environmentId,
    // 每次调用现取：页面切换分组后，后续动作必须打在新分组上。
    ...optionalQuery('codeSet', toValue(codeSet)),
    ...extra,
  })
  const callPathBody = (m: typeof updateMutation, code: string, extra: Record<string, unknown>) =>
    (m.mutateAsync as unknown as (vars: unknown) => Promise<unknown>)({
      path: { resourceType, code },
      body: withCtx(extra),
    })

  // 编辑前拉全字段详情用于回填(列表项只含部分 typed 字段)。
  async function fetchDetail(
    code: string,
  ): Promise<BusinessConsoleMasterDataResourceDetail | undefined> {
    const res = await getBusinessConsoleMasterDataResourceDetail({
      path: { resourceType, code },
      query: {
        organizationId: ctx.organizationId,
        environmentId: ctx.environmentId,
        ...optionalQuery('codeSet', toValue(codeSet)),
      },
    })
    const envelope = (
      res as { data?: { success?: boolean; data?: BusinessConsoleMasterDataResourceDetail | null } }
    ).data
    return envelope?.success ? (envelope.data ?? undefined) : undefined
  }

  return {
    update: (code: string, patch: Partial<BusinessConsoleUpdateMasterDataResourceRequest>) =>
      callPathBody(updateMutation, code, patch),
    // patch 无默认值：漏传 reason 必须编译失败，而不是留到运行期被后端拒。
    disable: (code: string, patch: MasterDataLifecyclePatch) =>
      callPathBody(disableMutation, code, {
        idempotencyKey: newCreateIdempotencyKey(`disable-${resourceType}-${code}`),
        ...patch,
      }),
    enable: (code: string, patch: MasterDataLifecyclePatch) =>
      callPathBody(enableMutation, code, {
        idempotencyKey: newCreateIdempotencyKey(`enable-${resourceType}-${code}`),
        ...patch,
      }),
    fetchDetail,
    updatePending: updateMutation.isLoading,
    disablePending: disableMutation.isLoading,
    enablePending: enableMutation.isLoading,
    actionError: computed(
      () => updateMutation.error.value ?? disableMutation.error.value ?? enableMutation.error.value,
    ),
  }
}
