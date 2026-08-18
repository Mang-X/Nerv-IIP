import {
  addBusinessConsoleTeamMemberMutationOptions,
  assignBusinessConsolePersonnelSkillMutationOptions,
  createBusinessConsoleDepartmentMutationOptions,
  createBusinessConsoleProductionLineMutationOptions,
  createBusinessConsoleShiftMutationOptions,
  createBusinessConsoleSiteMutationOptions,
  createBusinessConsoleBusinessPartnerMutationOptions,
  createBusinessConsoleReferenceDataCodeMutationOptions,
  createBusinessConsoleSkuMutationOptions,
  createBusinessConsoleUnitOfMeasureMutationOptions,
  createBusinessConsoleUomConversionMutationOptions,
  createBusinessConsoleTeamMutationOptions,
  createBusinessConsoleWorkCalendarMutationOptions,
  createBusinessConsoleWorkCenterMutationOptions,
  createBusinessConsoleWorkerMutationOptions,
  createBusinessConsoleWorkshopMutationOptions,
  disableBusinessConsoleMasterDataResourceMutationOptions,
  enableBusinessConsoleMasterDataResourceMutationOptions,
  getBusinessConsoleMasterDataResourceDetail,
  listBusinessConsoleMasterDataResourcesQueryOptions,
  listBusinessConsolePersonnelSkillMatrixQueryOptions,
  listBusinessConsoleSkusQueryOptions,
  listBusinessConsoleTeamMembersQueryOptions,
  listBusinessConsoleWorkersQueryOptions,
  listBusinessConsoleWorkshopsQueryOptions,
  registerBusinessConsoleDeviceAssetMutationOptions,
  removeBusinessConsoleTeamMemberMutationOptions,
  updateBusinessConsoleMasterDataResourceMutationOptions,
  type BusinessConsoleCreateBusinessPartnerRequest,
  type BusinessConsoleCreateReferenceDataCodeRequest,
  type BusinessConsoleCreateSkuRequest,
  type BusinessConsoleCreateUnitOfMeasureRequest,
  type BusinessConsoleCreateUomConversionRequest,
  type BusinessConsoleCreateWorkerRequest,
  type BusinessConsoleCreateWorkshopRequest,
  type BusinessConsoleMasterDataResourceDetail,
  type BusinessConsolePersonnelSkillMatrixEnvelope,
  type BusinessConsolePersonnelSkillMatrixRow,
  type BusinessConsoleResourceItem,
  type BusinessConsoleResourceListEnvelope,
  type BusinessConsoleSetMasterDataResourceEnabledRequest,
  type BusinessConsoleTeamMemberItem,
  type BusinessConsoleTeamMemberListEnvelope,
  type BusinessConsoleUpdateMasterDataResourceRequest,
  type BusinessConsoleWorkerDirectoryEnvelope,
  type BusinessConsoleWorkerDirectoryItem,
} from '@nerv-iip/api-client'
import {
  useMutation,
  useQuery,
  useQueryCache,
  type UseMutationOptions,
  type UseQueryEntry,
} from '@pinia/colada'
import { computed, reactive, toValue, type MaybeRefOrGetter } from 'vue'
import {
  bindBusinessContext,
  hasBusinessContext,
  refetchWithBusinessContext,
  withBusinessContextEnabled,
  type BusinessContextFields,
} from './businessContextBinding'

const DEFAULT_TAKE = 100

/**
 * 人员目录单页上限，由网关校验器定死：
 * `BusinessConsoleMasterDataEndpoints.cs` 的 `RuleFor(x => x.PageSize).InclusiveBetween(1, 200)`。
 *
 * 超了就是 400，而调用方大多把人员目录当查表用（userId → 姓名），失败时整列回落成占位符——
 * **界面看着「没数据」，其实是请求被拒**。第五轮走查在待检工作台实际踩到（`pageSize: 500`，
 * 整列「当前持有人」变 `—`，连「已被他人认领」都看不出来）。
 *
 * 所以这里既导出常量给调用方引用，也在 composable 内部夹紧——下一个调用方就算随手写个大数
 * 也不会把页面打成静默空态。
 */
export const WORKER_DIRECTORY_MAX_PAGE_SIZE = 200

export interface BusinessContextFilters extends BusinessContextFields {}

export interface MasterDataListFilters extends BusinessContextFilters {
  includeDisabled?: boolean
  skip: number
  take: number
}

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

function defaultContext(): BusinessContextFilters {
  return bindBusinessContext(
    reactive({
      organizationId: '',
      environmentId: '',
    }),
  )
}

function defaultListFilters(): MasterDataListFilters {
  return bindBusinessContext(
    reactive({
      organizationId: '',
      environmentId: '',
      skip: 0,
      take: DEFAULT_TAKE,
    }),
  )
}

function defaultResourceFilters(resourceType: string, codeSet?: string): MasterDataResourceFilters {
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

function newCreateIdempotencyKey(resourceType: string) {
  const cryptoApi = globalThis.crypto
  if (cryptoApi && typeof cryptoApi.randomUUID === 'function') {
    return `${resourceType}-${cryptoApi.randomUUID()}`
  }

  return `${resourceType}-${Date.now()}-${Math.random().toString(16).slice(2)}`
}

function withCreateIdempotency<TBody>(resourceType: string, body: TBody): TBody {
  if (typeof body !== 'object' || body === null) {
    return body
  }

  const current = 'idempotencyKey' in body ? body.idempotencyKey : undefined
  if (typeof current === 'string' && current.trim().length > 0) {
    return body
  }

  return {
    ...body,
    idempotencyKey: newCreateIdempotencyKey(resourceType),
  }
}

export function useBusinessSkus() {
  const filters = defaultListFilters()
  const queryCache = useQueryCache()

  const skusQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsoleSkusQueryOptions({
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
    refreshSkus: () => refetchWithBusinessContext(filters, skusQuery),
    skus: computed<BusinessConsoleResourceItem[]>(() => resourceItems(skusQuery.data.value)),
    skusError: skusQuery.error,
    skusPending: skusQuery.isLoading,
    skusTotal: computed(() => resourceTotal(skusQuery.data.value)),
  }
}

/**
 * 业务伙伴的「列表 + 新建」。列表走通用 resources 端点（含 typed partnerType/partnerRoles/taxId），
 * 新建走 business-partner 专属端点（需显式 partnerType 主角色 + 可选 partnerRoles 附加角色）。
 * 角色一律取真实 typed 字段，绝不靠 code 子串推断。
 */
export function useBusinessPartners() {
  const filters = defaultResourceFilters('business-partner')
  const queryCache = useQueryCache()

  const partnersQuery = useQuery(() =>
    withBusinessContextEnabled(
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
      filters,
    ),
  )

  const createPartnerMutation = useMutation({
    ...createBusinessConsoleBusinessPartnerMutationOptions(),
    onSuccess() {
      void queryCache
        .invalidateQueries({ predicate: isBusinessQuery('listBusinessConsoleMasterDataResources') })
        .catch(ignoreBackgroundError)
    },
  })

  return {
    createPartner: (body: BusinessConsoleCreateBusinessPartnerRequest) =>
      createPartnerMutation.mutateAsync({ body: withCreateIdempotency('business-partner', body) }),
    createPartnerError: createPartnerMutation.error,
    createPartnerPending: createPartnerMutation.isLoading,
    filters,
    refreshPartners: () => refetchWithBusinessContext(filters, partnersQuery),
    partners: computed<BusinessConsoleResourceItem[]>(() =>
      resourceItems(partnersQuery.data.value),
    ),
    partnersError: partnersQuery.error,
    partnersPending: partnersQuery.isLoading,
    partnersTotal: computed(() => resourceTotal(partnersQuery.data.value)),
  }
}

/**
 * 计量单位（UoM）的「列表 + 新建」。UoM 是独立实体：列表走通用 resources 端点
 * （resourceType=`unit-of-measure`，返回 code/displayName/active/dimensionType 等），
 * 新建走 unit-of-measure 专属端点（需 code/name/dimensionType/roundingMode，precision 可空）。
 * 停用 / 启用 / 改名 / 详情用现成 `useMasterDataResourceActions('unit-of-measure')`。
 */
export function useBusinessUoms() {
  const filters = defaultResourceFilters('unit-of-measure')
  const queryCache = useQueryCache()

  const uomsQuery = useQuery(() =>
    withBusinessContextEnabled(
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
      filters,
    ),
  )

  const createUomMutation = useMutation({
    ...createBusinessConsoleUnitOfMeasureMutationOptions(),
    onSuccess() {
      void queryCache
        .invalidateQueries({ predicate: isBusinessQuery('listBusinessConsoleMasterDataResources') })
        .catch(ignoreBackgroundError)
    },
  })

  return {
    createUom: (body: BusinessConsoleCreateUnitOfMeasureRequest) =>
      createUomMutation.mutateAsync({ body }),
    createUomError: createUomMutation.error,
    createUomPending: createUomMutation.isLoading,
    filters,
    refreshUoms: () => refetchWithBusinessContext(filters, uomsQuery),
    uoms: computed<BusinessConsoleResourceItem[]>(() => resourceItems(uomsQuery.data.value)),
    uomsError: uomsQuery.error,
    uomsPending: uomsQuery.isLoading,
    uomsTotal: computed(() => resourceTotal(uomsQuery.data.value)),
  }
}

/**
 * 车间的「列表 + 新建」。车间是工厂下的组织 / 区域层（工厂 → 车间 → 产线 → 工作中心）。
 * 列表走车间专属端点（返回通用 resource 列表形状：含 code/displayName/active/siteCode），
 * 新建走车间专属端点（需 code/name/siteCode，managerUserId/description 可选）。
 * onSuccess 同时失效车间列表与通用 resources 列表（产线/工作中心归属读时复用）。
 */
export function useBusinessWorkshops() {
  const filters = defaultListFilters()
  const queryCache = useQueryCache()

  const workshopsQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsoleWorkshopsQueryOptions({
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
    onSuccess() {
      for (const id of ['listBusinessConsoleWorkshops', 'listBusinessConsoleMasterDataResources']) {
        void queryCache
          .invalidateQueries({ predicate: isBusinessQuery(id) })
          .catch(ignoreBackgroundError)
      }
    },
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

/**
 * 数据字典的「按 CodeSet 列出 + 新增码值」。字典是平台受控值来源（物料分类 / 单位量纲 /
 * 仓储条件等下拉取自这里）。列表走通用 resources 端点并带 codeSet 服务端过滤（真分页），
 * 新增走 reference-data 专属端点（需 codeSet/code/name + org/env）。
 */
export function useReferenceDataCodes() {
  const filters = defaultResourceFilters('reference-data')
  const queryCache = useQueryCache()

  const codesQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsoleMasterDataResourcesQueryOptions({
        query: {
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
          resourceType: filters.resourceType,
          ...optionalQuery('includeDisabled', filters.includeDisabled),
          ...optionalQuery('codeSet', filters.codeSet),
          skip: filters.skip,
          take: filters.take,
        },
      }),
      filters,
    ),
  )

  const createCodeMutation = useMutation({
    ...createBusinessConsoleReferenceDataCodeMutationOptions(),
    onSuccess() {
      void queryCache
        .invalidateQueries({ predicate: isBusinessQuery('listBusinessConsoleMasterDataResources') })
        .catch(ignoreBackgroundError)
    },
  })

  return {
    createCode: (body: BusinessConsoleCreateReferenceDataCodeRequest) =>
      createCodeMutation.mutateAsync({ body }),
    createCodeError: createCodeMutation.error,
    createCodePending: createCodeMutation.isLoading,
    filters,
    refreshCodes: () => refetchWithBusinessContext(filters, codesQuery),
    codes: computed<BusinessConsoleResourceItem[]>(() => resourceItems(codesQuery.data.value)),
    codesError: codesQuery.error,
    codesPending: codesQuery.isLoading,
    codesTotal: computed(() => resourceTotal(codesQuery.data.value)),
  }
}

export function useBusinessMasterDataResources(
  resourceType: string,
  options: { codeSet?: string } = {},
) {
  const filters = defaultResourceFilters(resourceType, options.codeSet)

  const resourcesQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsoleMasterDataResourcesQueryOptions({
        query: {
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
          resourceType: filters.resourceType,
          ...optionalQuery('codeSet', filters.codeSet),
          ...optionalQuery('includeDisabled', filters.includeDisabled),
          skip: filters.skip,
          take: filters.take,
        },
      }),
      filters,
    ),
  )

  return {
    filters,
    refreshResources: () => refetchWithBusinessContext(filters, resourcesQuery),
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
 * docs/architecture/master-data-module-product-design.md §0/§7），新建走各自 create 端点。
 * 编辑/停用待后端 #344；本 Phase 1 只做查 + 增。
 */
export function useMasterDataResource<TBody>(resourceType: MasterDataResourceType) {
  const filters = defaultResourceFilters(resourceType)
  const queryCache = useQueryCache()

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
          skip: filters.skip,
          take: filters.take,
        },
      }),
      filters,
    ),
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
    refresh: () => refetchWithBusinessContext(filters, listQuery),
    create: (body: TBody) =>
      (createMutation.mutateAsync as unknown as (vars: { body: TBody }) => Promise<unknown>)({
        body: withCreateIdempotency(resourceType, body),
      }),
    createError: createMutation.error,
    createPending: createMutation.isLoading,
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

export interface WorkerDirectoryFilters extends BusinessContextFilters {
  keyword?: string
  /** 精确匹配单个工人标识，用于回填已选人员。 */
  userId?: string
  departmentCode?: string
  /** 按班组过滤候选人。 */
  teamCode?: string
  /** 按车间过滤候选人——班组是车间级的。 */
  workshopCode?: string
  /** 按工作中心过滤候选人——经该工作中心所属车间解析到班组成员。 */
  workCenterCode?: string
  /** 按技能过滤候选人，只保留当前有效的技能记录。 */
  skillCode?: string
  /** 在岗状态：active / on-leave / resigned。 */
  employmentStatus?: string
  includeDisabled?: boolean
  pageIndex: number
  pageSize: number
}

function workerItems(envelope: BusinessConsoleWorkerDirectoryEnvelope | undefined) {
  if (!envelope?.success) {
    return []
  }

  return envelope.data?.items ?? []
}

function workerTotal(envelope: BusinessConsoleWorkerDirectoryEnvelope | undefined) {
  if (!envelope?.success) {
    return 0
  }

  return envelope.data?.totalCount ?? 0
}

/**
 * 员工目录（人员选择器与员工维护页的共同数据源）。读自 MasterData 员工主数据
 * `/master-data/workers`（注意分页用 pageIndex/pageSize，非 skip/take）。除关键词外还支持
 * 部门 / 班组 / 工作中心 / 技能 / 在岗状态过滤——派工候选就是靠 workCenterCode 收敛的。
 * UI 只呈现姓名 / 工号 / 部门 / 班组 / 技能，userId 仅作为绑定值。
 */
export function useBusinessWorkers(initial: Partial<WorkerDirectoryFilters> = {}) {
  const filters = bindBusinessContext(
    reactive<WorkerDirectoryFilters>({
      organizationId: '',
      environmentId: '',
      keyword: undefined,
      // 网关 BusinessConsoleWorkerDirectoryRequestValidator 校验 PageIndex > 0（1-based，
      // 默认 PageIndex=1，与 useBusinessScheduling 等一致）。发 0 会被后端拒为 400，人员选择器静默空。
      pageIndex: 1,
      ...initial,
      // 夹紧到网关上限：调用方传超了就是 400，而这里失败通常表现为「整列空」而不是报错，
      // 极难从界面看出来（见 WORKER_DIRECTORY_MAX_PAGE_SIZE 注释里的实测）。
      pageSize: Math.min(
        Math.max(1, initial.pageSize ?? DEFAULT_TAKE),
        WORKER_DIRECTORY_MAX_PAGE_SIZE,
      ),
    }),
  )

  const workersQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsoleWorkersQueryOptions({
        query: {
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
          ...optionalQuery('keyword', filters.keyword),
          ...optionalQuery('userId', filters.userId),
          ...optionalQuery('departmentCode', filters.departmentCode),
          ...optionalQuery('teamCode', filters.teamCode),
          ...optionalQuery('workshopCode', filters.workshopCode),
          ...optionalQuery('workCenterCode', filters.workCenterCode),
          ...optionalQuery('skillCode', filters.skillCode),
          ...optionalQuery('employmentStatus', filters.employmentStatus),
          ...optionalQuery('includeDisabled', filters.includeDisabled),
          pageIndex: filters.pageIndex,
          pageSize: filters.pageSize,
        },
      }),
      filters,
    ),
  )

  return {
    filters,
    refresh: () => refetchWithBusinessContext(filters, workersQuery),
    workers: computed<BusinessConsoleWorkerDirectoryItem[]>(() =>
      workerItems(workersQuery.data.value),
    ),
    workersError: workersQuery.error,
    workersPending: workersQuery.isLoading,
    workersTotal: computed(() => workerTotal(workersQuery.data.value)),
  }
}

/**
 * 员工维护页的读写面：目录列表 + 新建 + 编辑/停用/启用（后两者走通用资源端点
 * `master-data/resources/worker/{code}`，code 即工号）。
 */
export function useWorkerRegistry(initial: Partial<WorkerDirectoryFilters> = {}) {
  const directory = useBusinessWorkers({ includeDisabled: true, pageSize: 50, ...initial })
  const actions = useMasterDataResourceActions('worker')
  const queryCache = useQueryCache()

  function invalidate() {
    void queryCache
      .invalidateQueries({ predicate: isBusinessQuery('listBusinessConsoleWorkers') })
      .catch(ignoreBackgroundError)
  }

  const createMutation = useMutation({
    ...createBusinessConsoleWorkerMutationOptions(),
    onSuccess: invalidate,
  } as unknown as UseMutationOptions)

  async function runAndRefresh<T>(action: Promise<T>) {
    const result = await action
    invalidate()
    return result
  }

  return {
    ...directory,
    ...actions,
    create: (body: BusinessConsoleCreateWorkerRequest) =>
      runAndRefresh(
        (
          createMutation.mutateAsync as unknown as (vars: {
            body: BusinessConsoleCreateWorkerRequest
          }) => Promise<unknown>
        )({ body }),
      ),
    createPending: createMutation.isLoading,
    createError: createMutation.error,
    update: (code: string, patch: Partial<BusinessConsoleUpdateMasterDataResourceRequest>) =>
      runAndRefresh(actions.update(code, patch)),
    // 生命周期原因必须透传：后端对空原因稳定拒绝，且原因是审计事实的一部分（#878）。
    disable: (code: string, patch: MasterDataLifecyclePatch) =>
      runAndRefresh(actions.disable(code, patch)),
    enable: (code: string, patch: MasterDataLifecyclePatch) =>
      runAndRefresh(actions.enable(code, patch)),
  }
}

function teamMemberItems(envelope: BusinessConsoleTeamMemberListEnvelope | undefined) {
  if (!envelope?.success) {
    return []
  }

  return envelope.data?.members ?? []
}

export interface TeamMemberAddInput {
  userId: string
  isLeader?: boolean
  effectiveFrom?: string
}

/**
 * 某班组的成员维护：按 teamCode 列成员 + 添加成员 + 移除成员。teamCode 以 getter/ref 传入
 * 以便随选中行切换；增删成功后互相失效，列表即时刷新。移除走 DELETE，必须带用户填写的真实原因。
 */
export function useTeamMembers(teamCode: MaybeRefOrGetter<string | undefined>) {
  const ctx = defaultContext()
  const queryCache = useQueryCache()

  function invalidate() {
    void queryCache
      .invalidateQueries({ predicate: isBusinessQuery('listBusinessConsoleTeamMembers') })
      .catch(ignoreBackgroundError)
  }

  const membersQuery = useQuery(() => {
    const code = toValue(teamCode)
    return {
      ...listBusinessConsoleTeamMembersQueryOptions({
        path: { teamCode: code ?? '' },
        query: { organizationId: ctx.organizationId, environmentId: ctx.environmentId },
      }),
      enabled: Boolean(code) && hasBusinessContext(ctx),
    }
  })

  const addMutation = useMutation({
    ...addBusinessConsoleTeamMemberMutationOptions(),
    onSuccess: invalidate,
  } as unknown as UseMutationOptions)
  const removeMutationOptions = removeBusinessConsoleTeamMemberMutationOptions()
  type RemoveTeamMemberMutationVariables = Parameters<typeof removeMutationOptions.mutation>[0]
  const removeMutation = useMutation({
    ...removeMutationOptions,
    onSuccess: invalidate,
  })

  return {
    members: computed<BusinessConsoleTeamMemberItem[]>(() =>
      teamMemberItems(membersQuery.data.value),
    ),
    membersError: membersQuery.error,
    membersPending: membersQuery.isLoading,
    refresh: () =>
      Boolean(toValue(teamCode)) && hasBusinessContext(ctx)
        ? membersQuery.refetch()
        : Promise.resolve(),
    addMember: (input: TeamMemberAddInput) =>
      (addMutation.mutateAsync as unknown as (vars: unknown) => Promise<unknown>)({
        path: { teamCode: toValue(teamCode) ?? '' },
        body: {
          organizationId: ctx.organizationId,
          environmentId: ctx.environmentId,
          userId: input.userId,
          ...optionalQuery('isLeader', input.isLeader),
          ...optionalQuery('effectiveFrom', input.effectiveFrom),
        },
      }),
    addPending: addMutation.isLoading,
    removeMember: (userId: string, reason: string) => {
      const variables: RemoveTeamMemberMutationVariables = {
        path: { teamCode: toValue(teamCode) ?? '', userId },
        body: {
          organizationId: ctx.organizationId,
          environmentId: ctx.environmentId,
          reason,
        },
      }
      return removeMutation.mutateAsync(variables)
    },
    removePending: removeMutation.isLoading,
    memberError: computed(() => addMutation.error.value ?? removeMutation.error.value),
  }
}

export interface PersonnelSkillAssignInput {
  userId: string
  skillCode: string
  level: string
  effectiveFrom?: string
}

/**
 * 人员技能登记：把某工人的某技能登记为某等级（走 `/master-data/personnel-skills`）。
 * 成功后失效通用 resources 列表（人员技能列表读自 `useBusinessMasterDataResources('personnel-skill')`）。
 */
export function usePersonnelSkillAssignment() {
  const ctx = defaultContext()
  const queryCache = useQueryCache()

  const assignMutation = useMutation({
    ...assignBusinessConsolePersonnelSkillMutationOptions(),
    onSuccess() {
      void queryCache
        .invalidateQueries({ predicate: isBusinessQuery('listBusinessConsoleMasterDataResources') })
        .catch(ignoreBackgroundError)
    },
  } as unknown as UseMutationOptions)

  return {
    assign: (input: PersonnelSkillAssignInput) =>
      (assignMutation.mutateAsync as unknown as (vars: unknown) => Promise<unknown>)({
        body: {
          organizationId: ctx.organizationId,
          environmentId: ctx.environmentId,
          userId: input.userId,
          skillCode: input.skillCode,
          level: input.level,
          ...optionalQuery('effectiveFrom', input.effectiveFrom),
        },
      }),
    assignPending: assignMutation.isLoading,
    assignError: assignMutation.error,
  }
}

export interface PersonnelSkillMatrixFilters extends BusinessContextFilters {
  userId?: string
  skillCode?: string
  includeDisabled?: boolean
}

function skillMatrixData(envelope: BusinessConsolePersonnelSkillMatrixEnvelope | undefined) {
  if (!envelope?.success) {
    return { skillCodes: [] as string[], rows: [] as BusinessConsolePersonnelSkillMatrixRow[] }
  }

  return {
    skillCodes: envelope.data?.skillCodes ?? [],
    rows: envelope.data?.rows ?? [],
  }
}

/**
 * 人员技能矩阵（读，#375）。读自 `/master-data/personnel-skills/matrix`，返回列头 skillCodes
 * 与每人一行 rows（每行 userId + skills[skillCode/level/effectiveFrom/effectiveTo]）。
 * 支持服务端 userId / skillCode 过滤。登记技能仍走 `usePersonnelSkillAssignment()`。
 */
export function usePersonnelSkillMatrix() {
  const filters = bindBusinessContext(
    reactive<PersonnelSkillMatrixFilters>({
      organizationId: '',
      environmentId: '',
      userId: undefined,
      skillCode: undefined,
      includeDisabled: undefined,
    }),
  )

  const matrixQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsolePersonnelSkillMatrixQueryOptions({
        query: {
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
          ...optionalQuery('userId', filters.userId),
          ...optionalQuery('skillCode', filters.skillCode),
          ...optionalQuery('includeDisabled', filters.includeDisabled),
        },
      }),
      filters,
    ),
  )

  return {
    filters,
    refresh: () => refetchWithBusinessContext(filters, matrixQuery),
    skillCodes: computed<string[]>(() => skillMatrixData(matrixQuery.data.value).skillCodes),
    rows: computed<BusinessConsolePersonnelSkillMatrixRow[]>(
      () => skillMatrixData(matrixQuery.data.value).rows,
    ),
    matrixError: matrixQuery.error,
    matrixPending: matrixQuery.isLoading,
  }
}

/**
 * 计量单位换算（UoM conversion，#375）的「列表 + 新建」。换算无专属列端点，列表复用通用
 * resources 端点（resourceType=`uom-conversion`，详情 code 为 `from→to` 复合键，含
 * fromUomCode/toUomCode/factor/offset/effectiveFrom 等 typed 字段）；新建走 uom-conversions
 * 专属端点（fromUomCode/toUomCode/roundingMode 必填，factor/offset/precision/effectiveFrom 可选）。
 * 详情 / 停用经 `useMasterDataResourceActions('uom-conversion')`。
 */
export function useUomConversions() {
  const filters = defaultResourceFilters('uom-conversion')
  const queryCache = useQueryCache()

  const conversionsQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsoleMasterDataResourcesQueryOptions({
        query: {
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
          resourceType: filters.resourceType,
          ...optionalQuery('includeDisabled', filters.includeDisabled),
          ...optionalQuery('keyword', filters.keyword),
          skip: filters.skip,
          take: filters.take,
        },
      }),
      filters,
    ),
  )

  const createConversionMutation = useMutation({
    ...createBusinessConsoleUomConversionMutationOptions(),
    onSuccess() {
      void queryCache
        .invalidateQueries({ predicate: isBusinessQuery('listBusinessConsoleMasterDataResources') })
        .catch(ignoreBackgroundError)
    },
  })

  return {
    createUomConversion: (body: BusinessConsoleCreateUomConversionRequest) =>
      createConversionMutation.mutateAsync({ body }),
    createUomConversionError: createConversionMutation.error,
    createUomConversionPending: createConversionMutation.isLoading,
    filters,
    refreshConversions: () => refetchWithBusinessContext(filters, conversionsQuery),
    conversions: computed<BusinessConsoleResourceItem[]>(() =>
      resourceItems(conversionsQuery.data.value),
    ),
    conversionsError: conversionsQuery.error,
    conversionsPending: conversionsQuery.isLoading,
    conversionsTotal: computed(() => resourceTotal(conversionsQuery.data.value)),
  }
}
