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
  createBusinessConsoleTeamMutationOptions,
  createBusinessConsoleWorkCalendarMutationOptions,
  createBusinessConsoleWorkCenterMutationOptions,
  createBusinessConsoleWorkshopMutationOptions,
  disableBusinessConsoleMasterDataResourceMutationOptions,
  enableBusinessConsoleMasterDataResourceMutationOptions,
  getBusinessConsoleMasterDataResourceDetail,
  listBusinessConsoleMasterDataResourcesQueryOptions,
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
  type BusinessConsoleCreateWorkshopRequest,
  type BusinessConsoleMasterDataResourceDetail,
  type BusinessConsoleResourceItem,
  type BusinessConsoleResourceListEnvelope,
  type BusinessConsoleTeamMemberItem,
  type BusinessConsoleTeamMemberListEnvelope,
  type BusinessConsoleUpdateMasterDataResourceRequest,
  type BusinessConsoleWorkerDirectoryEnvelope,
  type BusinessConsoleWorkerDirectoryItem,
} from '@nerv-iip/api-client'
import { useMutation, useQuery, useQueryCache, type UseMutationOptions, type UseQueryEntry } from '@pinia/colada'
import { computed, reactive, toValue, type MaybeRefOrGetter } from 'vue'

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

/**
 * 业务伙伴的「列表 + 新建」。列表走通用 resources 端点（含 typed partnerType/partnerRoles/taxId），
 * 新建走 business-partner 专属端点（需显式 partnerType 主角色 + 可选 partnerRoles 附加角色）。
 * 角色一律取真实 typed 字段，绝不靠 code 子串推断。
 */
export function useBusinessPartners() {
  const filters = defaultResourceFilters('business-partner')
  const queryCache = useQueryCache()

  const partnersQuery = useQuery(() =>
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
      createPartnerMutation.mutateAsync({ body }),
    createPartnerError: createPartnerMutation.error,
    createPartnerPending: createPartnerMutation.isLoading,
    filters,
    refreshPartners: partnersQuery.refetch,
    partners: computed<BusinessConsoleResourceItem[]>(() => resourceItems(partnersQuery.data.value)),
    partnersError: partnersQuery.error,
    partnersPending: partnersQuery.isLoading,
    partnersTotal: computed(() => resourceTotal(partnersQuery.data.value)),
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
    listBusinessConsoleWorkshopsQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        ...optionalQuery('includeDisabled', filters.includeDisabled),
        skip: filters.skip,
        take: filters.take,
      },
    }),
  )

  const createWorkshopMutation = useMutation({
    ...createBusinessConsoleWorkshopMutationOptions(),
    onSuccess() {
      for (const id of ['listBusinessConsoleWorkshops', 'listBusinessConsoleMasterDataResources']) {
        void queryCache.invalidateQueries({ predicate: isBusinessQuery(id) }).catch(ignoreBackgroundError)
      }
    },
  })

  return {
    createWorkshop: (body: BusinessConsoleCreateWorkshopRequest) =>
      createWorkshopMutation.mutateAsync({ body }),
    createWorkshopError: createWorkshopMutation.error,
    createWorkshopPending: createWorkshopMutation.isLoading,
    filters,
    refreshWorkshops: workshopsQuery.refetch,
    workshops: computed<BusinessConsoleResourceItem[]>(() => resourceItems(workshopsQuery.data.value)),
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
  const filters = reactive<MasterDataResourceFilters & { codeSet?: string }>({
    ...defaultResourceFilters('reference-data'),
  })
  const queryCache = useQueryCache()

  const codesQuery = useQuery(() =>
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
    createCode: (body: BusinessConsoleCreateReferenceDataCodeRequest) => createCodeMutation.mutateAsync({ body }),
    createCodeError: createCodeMutation.error,
    createCodePending: createCodeMutation.isLoading,
    filters,
    refreshCodes: codesQuery.refetch,
    codes: computed<BusinessConsoleResourceItem[]>(() => resourceItems(codesQuery.data.value)),
    codesError: codesQuery.error,
    codesPending: codesQuery.isLoading,
    codesTotal: computed(() => resourceTotal(codesQuery.data.value)),
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

  // 编辑前拉全字段详情用于回填(列表项只含部分 typed 字段)。
  async function fetchDetail(code: string): Promise<BusinessConsoleMasterDataResourceDetail | undefined> {
    const res = await getBusinessConsoleMasterDataResourceDetail({
      path: { resourceType, code },
      query: { organizationId: ctx.organizationId, environmentId: ctx.environmentId },
    })
    const envelope = (res as { data?: { success?: boolean; data?: BusinessConsoleMasterDataResourceDetail | null } }).data
    return envelope?.success ? envelope.data ?? undefined : undefined
  }

  return {
    update: (code: string, patch: Partial<BusinessConsoleUpdateMasterDataResourceRequest>) => callPathBody(updateMutation, code, patch),
    disable: (code: string) => callPathBody(disableMutation, code, {}),
    enable: (code: string) => callPathBody(enableMutation, code, {}),
    fetchDetail,
    updatePending: updateMutation.isLoading,
    disablePending: disableMutation.isLoading,
    enablePending: enableMutation.isLoading,
    actionError: computed(() => updateMutation.error.value ?? disableMutation.error.value ?? enableMutation.error.value),
  }
}

export interface WorkerDirectoryFilters extends BusinessContextFilters {
  keyword?: string
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
 * 工人目录（人员选择器数据源）。读自 `/master-data/workers`（注意分页用 pageIndex/pageSize，
 * 非 skip/take），支持服务端 keyword 检索。仅暴露姓名 / 工号 / 部门给 UI，userId 内部使用。
 */
export function useBusinessWorkers() {
  const filters = reactive<WorkerDirectoryFilters>({
    ...defaultContext(),
    keyword: undefined,
    pageIndex: 0,
    pageSize: DEFAULT_TAKE,
  })

  const workersQuery = useQuery(() =>
    listBusinessConsoleWorkersQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        ...optionalQuery('keyword', filters.keyword),
        pageIndex: filters.pageIndex,
        pageSize: filters.pageSize,
      },
    }),
  )

  return {
    filters,
    refresh: workersQuery.refetch,
    workers: computed<BusinessConsoleWorkerDirectoryItem[]>(() => workerItems(workersQuery.data.value)),
    workersError: workersQuery.error,
    workersPending: workersQuery.isLoading,
    workersTotal: computed(() => workerTotal(workersQuery.data.value)),
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
 * 以便随选中行切换；增删成功后互相失效，列表即时刷新。移除走 DELETE（body 带 org/env）。
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
      enabled: Boolean(code),
    }
  })

  const addMutation = useMutation({ ...addBusinessConsoleTeamMemberMutationOptions(), onSuccess: invalidate } as unknown as UseMutationOptions)
  const removeMutation = useMutation({ ...removeBusinessConsoleTeamMemberMutationOptions(), onSuccess: invalidate } as unknown as UseMutationOptions)

  return {
    members: computed<BusinessConsoleTeamMemberItem[]>(() => teamMemberItems(membersQuery.data.value)),
    membersError: membersQuery.error,
    membersPending: membersQuery.isLoading,
    refresh: membersQuery.refetch,
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
    removeMember: (userId: string) =>
      (removeMutation.mutateAsync as unknown as (vars: unknown) => Promise<unknown>)({
        path: { teamCode: toValue(teamCode) ?? '', userId },
        body: { organizationId: ctx.organizationId, environmentId: ctx.environmentId },
      }),
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
