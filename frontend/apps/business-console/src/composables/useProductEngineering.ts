/**
 * 产品工程域 composable（每域一 composable，手工接线风格对齐 useBusinessMasterData）。
 *
 * 仅覆盖工程域唯一完整 CRUD 实体「生产版本（ProductionVersion）」，外加它在表单里要用的
 * 两个选择器数据源：已发布 MBOM、已发布工艺路线。
 *
 * 关键事实（以后端代码为准）：
 * - 生产版本状态枚举是 `active`/`archived`（见 ProductionVersion.cs ProductionVersionStatus）。
 * - MBOM / 工艺路线的版本状态枚举是 `Draft`/`Published`/`Archived`（EngineeringVersionStatus），
 *   选择器只取 `Published`（**不是** `Released`，旧 index.vue 的 `Released` 是对不上的 bug）。
 * - 生产版本通过 `mbomVersionId`/`routingVersionId` 字符串引用已发布 MBOM / 路线；后端列表项只暴露
 *   `bomCode`/`routingCode`（+ revision），故选择器以 `bomCode`/`routingCode` 作为绑定值。
 */
import {
  archiveBusinessConsoleEngineeringProductionVersionMutationOptions,
  createBusinessConsoleEngineeringItemRevisionMutationOptions,
  createBusinessConsoleEngineeringProductionVersionMutationOptions,
  getBusinessConsoleEngineeringBom,
  getBusinessConsoleEngineeringChange,
  getBusinessConsoleEngineeringDocument,
  getBusinessConsoleEngineeringItem,
  getBusinessConsoleEngineeringManufacturingBom,
  getBusinessConsoleEngineeringRouting,
  listBusinessConsoleEngineeringBomsQueryOptions,
  listBusinessConsoleEngineeringChangesQueryOptions,
  listBusinessConsoleEngineeringDocumentsQueryOptions,
  listBusinessConsoleEngineeringItemsQueryOptions,
  listBusinessConsoleEngineeringManufacturingBomsQueryOptions,
  listBusinessConsoleEngineeringProductionVersionsQueryOptions,
  listBusinessConsoleEngineeringRoutingsQueryOptions,
  registerBusinessConsoleEngineeringDocumentMutationOptions,
  releaseBusinessConsoleEngineeringBomMutationOptions,
  releaseBusinessConsoleEngineeringChangeMutationOptions,
  releaseBusinessConsoleEngineeringManufacturingBomMutationOptions,
  releaseBusinessConsoleEngineeringRoutingMutationOptions,
  resolveBusinessConsoleEngineeringProductionVersion,
  updateBusinessConsoleEngineeringProductionVersionMutationOptions,
  type BusinessConsoleCreateEngineeringItemRevisionRequest,
  type BusinessConsoleCreateProductionVersionRequest,
  type BusinessConsoleEngineeringBomDetailEnvelope,
  type BusinessConsoleEngineeringBomItem,
  type BusinessConsoleEngineeringBomListEnvelope,
  type BusinessConsoleEngineeringChangeDetailEnvelope,
  type BusinessConsoleEngineeringChangeItem,
  type BusinessConsoleEngineeringChangeListEnvelope,
  type BusinessConsoleEngineeringDocumentDetailEnvelope,
  type BusinessConsoleEngineeringDocumentItem,
  type BusinessConsoleEngineeringDocumentListEnvelope,
  type BusinessConsoleEngineeringItemDetailEnvelope,
  type BusinessConsoleEngineeringItemRevisionItem,
  type BusinessConsoleEngineeringItemListEnvelope,
  type BusinessConsoleManufacturingBomDetailEnvelope,
  type BusinessConsoleManufacturingBomItem,
  type BusinessConsoleManufacturingBomListEnvelope,
  type BusinessConsoleProductionVersionItem,
  type BusinessConsoleProductionVersionListEnvelope,
  type BusinessConsoleRegisterEngineeringDocumentRequest,
  type BusinessConsoleReleaseEngineeringBomRequest,
  type BusinessConsoleReleaseEngineeringChangeRequest,
  type BusinessConsoleReleaseManufacturingBomRequest,
  type BusinessConsoleReleaseRoutingRequest,
  type BusinessConsoleResolveProductionVersionEnvelope,
  type BusinessConsoleResolveProductionVersionResponse,
  type BusinessConsoleRoutingDetailEnvelope,
  type BusinessConsoleRoutingItem,
  type BusinessConsoleRoutingListEnvelope,
  type BusinessConsoleUpdateProductionVersionRequest,
} from '@nerv-iip/api-client'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useMutation, useQuery, useQueryCache, type UseMutationOptions, type UseQueryEntry } from '@pinia/colada'
import { computed, reactive, ref, shallowRef } from 'vue'

const DEFAULT_TAKE = 100
/** MBOM / 工艺路线选择器只取后端真枚举 `Published`（不是 `Released`）。 */
const PUBLISHED = 'Published'

export interface ProductionVersionListFilters {
  organizationId: string
  environmentId: string
  skuCode?: string
  status?: string
  skip: number
  take: number
}

export interface ProductionVersionResolveInput {
  skuCode: string
  effectiveDate: string
  lotSize: number
}

export interface EngineeringBomListFilters {
  organizationId: string
  environmentId: string
  parentItemCode?: string
  status?: string
  skip: number
  take: number
}

export interface ManufacturingBomListFilters {
  organizationId: string
  environmentId: string
  skuCode?: string
  status?: string
  skip: number
  take: number
}

export interface RoutingListFilters {
  organizationId: string
  environmentId: string
  skuCode?: string
  status?: string
  skip: number
  take: number
}

export interface EngineeringItemListFilters {
  organizationId: string
  environmentId: string
  itemCode?: string
  status?: string
  skip: number
  take: number
}

export interface EngineeringDocumentListFilters {
  organizationId: string
  environmentId: string
  itemCode?: string
  documentType?: string
  skip: number
  take: number
}

export interface EngineeringChangeListFilters {
  organizationId: string
  environmentId: string
  status?: string
  skip: number
  take: number
}

function optionalQuery<TKey extends string, TValue>(key: TKey, value: TValue | undefined) {
  return value === undefined || value === '' ? {} : { [key]: value }
}

function unwrapItems<T>(envelope: { success?: boolean, data?: { items?: T[] } | null } | undefined): T[] {
  if (!envelope?.success) return []
  return envelope.data?.items ?? []
}

function unwrapTotal(envelope: { success?: boolean, data?: { total?: number } | null } | undefined) {
  if (!envelope?.success) return 0
  return envelope.data?.total ?? 0
}

/** 解包 get-by-id 单体响应（SDK fn 返回 `{ data: envelope }`，envelope 是 `{ success, data }`）。 */
function unwrapDetail<T>(res: { data?: { success?: boolean, data?: T | null } } | undefined): T | undefined {
  const envelope = res?.data
  if (!envelope?.success) return undefined
  return envelope.data ?? undefined
}

function isBusinessQuery(id: string) {
  return (entry: UseQueryEntry) => {
    const keyParts = Array.isArray(entry.key) ? entry.key : [entry.key]
    return keyParts.some((part) =>
      typeof part === 'object' && part !== null && '_id' in part && part._id === id,
    )
  }
}

function ignoreBackgroundError(_error: unknown) {}

/**
 * 生产版本列表（list + filters）+ 三件套写操作（create/update/archive）。
 * 写成功后失效列表查询，即时刷新。
 */
export function useEngineeringProductionVersions() {
  const context = useBusinessContextStore()
  const queryCache = useQueryCache()
  const filters = reactive<ProductionVersionListFilters>({
    organizationId: context.organizationId,
    environmentId: context.environmentId,
    skuCode: undefined,
    status: undefined,
    skip: 0,
    take: DEFAULT_TAKE,
  })

  const listQuery = useQuery(() =>
    listBusinessConsoleEngineeringProductionVersionsQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        ...optionalQuery('skuCode', filters.skuCode),
        ...optionalQuery('status', filters.status),
        skip: filters.skip,
        take: filters.take,
      },
    }),
  )

  function invalidateList() {
    void queryCache
      .invalidateQueries({ predicate: isBusinessQuery('listBusinessConsoleEngineeringProductionVersions') })
      .catch(ignoreBackgroundError)
  }

  const createMutation = useMutation({
    ...createBusinessConsoleEngineeringProductionVersionMutationOptions(),
    onSuccess: invalidateList,
  })
  const updateMutation = useMutation({
    ...updateBusinessConsoleEngineeringProductionVersionMutationOptions(),
    onSuccess: invalidateList,
  } as unknown as UseMutationOptions)
  const archiveMutation = useMutation({
    ...archiveBusinessConsoleEngineeringProductionVersionMutationOptions(),
    onSuccess: invalidateList,
  } as unknown as UseMutationOptions)

  return {
    filters,
    productionVersions: computed<BusinessConsoleProductionVersionItem[]>(() =>
      unwrapItems(listQuery.data.value as BusinessConsoleProductionVersionListEnvelope | undefined),
    ),
    productionVersionsError: listQuery.error,
    productionVersionsPending: listQuery.isLoading,
    productionVersionsTotal: computed(() =>
      unwrapTotal(listQuery.data.value as BusinessConsoleProductionVersionListEnvelope | undefined),
    ),
    refresh: listQuery.refetch,

    createProductionVersion: (body: BusinessConsoleCreateProductionVersionRequest) =>
      createMutation.mutateAsync({ body }),
    createPending: createMutation.isLoading,
    createError: createMutation.error,

    // 后端 update 走 `PUT .../{productionVersionId}`，org/env 在 query，绑定字段在 body。
    updateProductionVersion: (productionVersionId: string, body: BusinessConsoleUpdateProductionVersionRequest) =>
      (updateMutation.mutateAsync as unknown as (vars: unknown) => Promise<unknown>)({
        path: { productionVersionId },
        query: { organizationId: filters.organizationId, environmentId: filters.environmentId },
        body,
      }),
    updatePending: updateMutation.isLoading,
    updateError: updateMutation.error,

    // 归档走 `POST .../{productionVersionId}/archive`，body 带必填 reason。
    archiveProductionVersion: (productionVersionId: string, reason: string) =>
      (archiveMutation.mutateAsync as unknown as (vars: unknown) => Promise<unknown>)({
        path: { productionVersionId },
        query: { organizationId: filters.organizationId, environmentId: filters.environmentId },
        body: { reason },
      }),
    archivePending: archiveMutation.isLoading,
    archiveError: archiveMutation.error,
  }
}

/**
 * 生产版本 resolve（给定 SKU + 生效日 + 批量 → 命中哪个版本）。
 * 为「按需触发」语义（点解析才发请求），直接用 sdk fn 命令式调用，不挂常驻 query。
 */
export function useProductionVersionResolve() {
  const context = useBusinessContextStore()
  const resolved = shallowRef<BusinessConsoleResolveProductionVersionResponse | undefined>(undefined)
  const pending = ref(false)
  const resolvedOnce = ref(false)

  async function resolve(input: ProductionVersionResolveInput) {
    pending.value = true
    try {
      const res = await resolveBusinessConsoleEngineeringProductionVersion({
        query: {
          organizationId: context.organizationId,
          environmentId: context.environmentId,
          skuCode: input.skuCode,
          effectiveDate: input.effectiveDate,
          lotSize: input.lotSize,
        },
      })
      const envelope = (res as { data?: BusinessConsoleResolveProductionVersionEnvelope }).data
      resolved.value = envelope?.success ? envelope.data ?? undefined : undefined
      resolvedOnce.value = true
      return resolved.value
    }
    finally {
      pending.value = false
    }
  }

  function clear() {
    resolved.value = undefined
    resolvedOnce.value = false
  }

  return {
    resolve,
    clear,
    resolved,
    resolvePending: pending,
    resolvedOnce,
  }
}

/**
 * 已发布 MBOM 选择器数据源（status=Published）。绑定值用 `bomCode`，label 显示 `code · rev · SKU`。
 */
export function usePublishedMboms() {
  const context = useBusinessContextStore()
  const filters = reactive({
    organizationId: context.organizationId,
    environmentId: context.environmentId,
    skuCode: undefined as string | undefined,
  })

  const query = useQuery(() =>
    listBusinessConsoleEngineeringManufacturingBomsQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        ...optionalQuery('skuCode', filters.skuCode),
        status: PUBLISHED,
        skip: 0,
        take: DEFAULT_TAKE,
      },
    }),
  )

  return {
    filters,
    mboms: computed<BusinessConsoleManufacturingBomItem[]>(() =>
      unwrapItems(query.data.value as BusinessConsoleManufacturingBomListEnvelope | undefined),
    ),
    mbomsError: query.error,
    mbomsPending: query.isLoading,
    refreshMboms: query.refetch,
  }
}

/**
 * 已发布工艺路线选择器数据源（status=Published）。绑定值用 `routingCode`。
 */
export function usePublishedRoutings() {
  const context = useBusinessContextStore()
  const filters = reactive({
    organizationId: context.organizationId,
    environmentId: context.environmentId,
    skuCode: undefined as string | undefined,
  })

  const query = useQuery(() =>
    listBusinessConsoleEngineeringRoutingsQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        ...optionalQuery('skuCode', filters.skuCode),
        status: PUBLISHED,
        skip: 0,
        take: DEFAULT_TAKE,
      },
    }),
  )

  return {
    filters,
    routings: computed<BusinessConsoleRoutingItem[]>(() =>
      unwrapItems(query.data.value as BusinessConsoleRoutingListEnvelope | undefined),
    ),
    routingsError: query.error,
    routingsPending: query.isLoading,
    refreshRoutings: query.refetch,
  }
}

/**
 * EBOM 列表（list + filters）+ 发布新版本（release）+ 按需取版本明细（get-by-id）。
 * EBOM list 不含行明细；查看时用 `fetchEbomDetail(bomCode, revision)` 拉 get-by-id 取组件行（#389 已交付）。
 * release 后失效 EBOM 列表查询，即时刷新。
 */
export function useEngineeringEboms() {
  const context = useBusinessContextStore()
  const queryCache = useQueryCache()
  const filters = reactive<EngineeringBomListFilters>({
    organizationId: context.organizationId,
    environmentId: context.environmentId,
    parentItemCode: undefined,
    status: undefined,
    skip: 0,
    take: DEFAULT_TAKE,
  })

  const listQuery = useQuery(() =>
    listBusinessConsoleEngineeringBomsQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        ...optionalQuery('parentItemCode', filters.parentItemCode),
        ...optionalQuery('status', filters.status),
        skip: filters.skip,
        take: filters.take,
      },
    }),
  )

  function invalidateList() {
    void queryCache
      .invalidateQueries({ predicate: isBusinessQuery('listBusinessConsoleEngineeringBoms') })
      .catch(ignoreBackgroundError)
  }

  const releaseMutation = useMutation({
    ...releaseBusinessConsoleEngineeringBomMutationOptions(),
    onSuccess: invalidateList,
  } as unknown as UseMutationOptions)

  return {
    filters,
    eboms: computed<BusinessConsoleEngineeringBomItem[]>(() =>
      unwrapItems(listQuery.data.value as BusinessConsoleEngineeringBomListEnvelope | undefined),
    ),
    ebomsError: listQuery.error,
    ebomsPending: listQuery.isLoading,
    ebomsTotal: computed(() =>
      unwrapTotal(listQuery.data.value as BusinessConsoleEngineeringBomListEnvelope | undefined),
    ),
    refresh: listQuery.refetch,

    releaseEbom: (body: BusinessConsoleReleaseEngineeringBomRequest) =>
      (releaseMutation.mutateAsync as unknown as (vars: unknown) => Promise<unknown>)({ body }),
    releasePending: releaseMutation.isLoading,
    releaseError: releaseMutation.error,

    // 按需取某版本明细（含组件行），用于「查看」。失败抛错由调用方处理。
    fetchEbomDetail: async (bomCode: string, revision: string) => {
      const res = await getBusinessConsoleEngineeringBom({
        path: { bomCode, revision },
        query: { organizationId: filters.organizationId, environmentId: filters.environmentId },
      })
      return unwrapDetail<BusinessConsoleEngineeringBomItem>(
        res as { data?: BusinessConsoleEngineeringBomDetailEnvelope },
      )
    },
  }
}

/**
 * 已发布 EBOM 选择器数据源（status=Published），供 MBOM 发布向导选「引用的设计 BOM」。
 * 绑定用 `bomCode` + `revision`（MBOM release 需 engineeringBomCode + engineeringBomRevision）。
 */
export function usePublishedEboms() {
  const context = useBusinessContextStore()
  const filters = reactive({
    organizationId: context.organizationId,
    environmentId: context.environmentId,
    parentItemCode: undefined as string | undefined,
  })

  const query = useQuery(() =>
    listBusinessConsoleEngineeringBomsQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        ...optionalQuery('parentItemCode', filters.parentItemCode),
        status: PUBLISHED,
        skip: 0,
        take: DEFAULT_TAKE,
      },
    }),
  )

  return {
    filters,
    eboms: computed<BusinessConsoleEngineeringBomItem[]>(() =>
      unwrapItems(query.data.value as BusinessConsoleEngineeringBomListEnvelope | undefined),
    ),
    ebomsError: query.error,
    ebomsPending: query.isLoading,
    refreshEboms: query.refetch,
  }
}

/**
 * MBOM 列表（list + filters）+ 发布新版本（release）+ 按需取版本明细（get-by-id）。
 * MBOM list 含 MaterialLines，但不含 RecipeLines；查看时用 `fetchMbomDetail(bomCode, revision)`
 * 拉 get-by-id，补齐物料行 + 配方行（#389 已交付）。
 */
export function useEngineeringMboms() {
  const context = useBusinessContextStore()
  const queryCache = useQueryCache()
  const filters = reactive<ManufacturingBomListFilters>({
    organizationId: context.organizationId,
    environmentId: context.environmentId,
    skuCode: undefined,
    status: undefined,
    skip: 0,
    take: DEFAULT_TAKE,
  })

  const listQuery = useQuery(() =>
    listBusinessConsoleEngineeringManufacturingBomsQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        ...optionalQuery('skuCode', filters.skuCode),
        ...optionalQuery('status', filters.status),
        skip: filters.skip,
        take: filters.take,
      },
    }),
  )

  function invalidateList() {
    void queryCache
      .invalidateQueries({ predicate: isBusinessQuery('listBusinessConsoleEngineeringManufacturingBoms') })
      .catch(ignoreBackgroundError)
  }

  const releaseMutation = useMutation({
    ...releaseBusinessConsoleEngineeringManufacturingBomMutationOptions(),
    onSuccess: invalidateList,
  } as unknown as UseMutationOptions)

  return {
    filters,
    mboms: computed<BusinessConsoleManufacturingBomItem[]>(() =>
      unwrapItems(listQuery.data.value as BusinessConsoleManufacturingBomListEnvelope | undefined),
    ),
    mbomsError: listQuery.error,
    mbomsPending: listQuery.isLoading,
    mbomsTotal: computed(() =>
      unwrapTotal(listQuery.data.value as BusinessConsoleManufacturingBomListEnvelope | undefined),
    ),
    refresh: listQuery.refetch,

    releaseMbom: (body: BusinessConsoleReleaseManufacturingBomRequest) =>
      (releaseMutation.mutateAsync as unknown as (vars: unknown) => Promise<unknown>)({ body }),
    releasePending: releaseMutation.isLoading,
    releaseError: releaseMutation.error,

    // 按需取某版本明细（含物料行 + 配方行），用于「查看」。失败抛错由调用方处理。
    fetchMbomDetail: async (bomCode: string, revision: string) => {
      const res = await getBusinessConsoleEngineeringManufacturingBom({
        path: { bomCode, revision },
        query: { organizationId: filters.organizationId, environmentId: filters.environmentId },
      })
      return unwrapDetail<BusinessConsoleManufacturingBomItem>(
        res as { data?: BusinessConsoleManufacturingBomDetailEnvelope },
      )
    },
  }
}

/**
 * 工艺路线列表（list + filters）+ 发布新版本（release）+ 按需取版本明细（get-by-id）。
 * Routing list 不含工序明细；查看时用 `fetchRoutingDetail(routingCode, revision)` 拉 get-by-id 取工序行（#389 已交付）。
 */
export function useEngineeringRoutings() {
  const context = useBusinessContextStore()
  const queryCache = useQueryCache()
  const filters = reactive<RoutingListFilters>({
    organizationId: context.organizationId,
    environmentId: context.environmentId,
    skuCode: undefined,
    status: undefined,
    skip: 0,
    take: DEFAULT_TAKE,
  })

  const listQuery = useQuery(() =>
    listBusinessConsoleEngineeringRoutingsQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        ...optionalQuery('skuCode', filters.skuCode),
        ...optionalQuery('status', filters.status),
        skip: filters.skip,
        take: filters.take,
      },
    }),
  )

  function invalidateList() {
    void queryCache
      .invalidateQueries({ predicate: isBusinessQuery('listBusinessConsoleEngineeringRoutings') })
      .catch(ignoreBackgroundError)
  }

  const releaseMutation = useMutation({
    ...releaseBusinessConsoleEngineeringRoutingMutationOptions(),
    onSuccess: invalidateList,
  } as unknown as UseMutationOptions)

  return {
    filters,
    routings: computed<BusinessConsoleRoutingItem[]>(() =>
      unwrapItems(listQuery.data.value as BusinessConsoleRoutingListEnvelope | undefined),
    ),
    routingsError: listQuery.error,
    routingsPending: listQuery.isLoading,
    routingsTotal: computed(() =>
      unwrapTotal(listQuery.data.value as BusinessConsoleRoutingListEnvelope | undefined),
    ),
    refresh: listQuery.refetch,

    releaseRouting: (body: BusinessConsoleReleaseRoutingRequest) =>
      (releaseMutation.mutateAsync as unknown as (vars: unknown) => Promise<unknown>)({ body }),
    releasePending: releaseMutation.isLoading,
    releaseError: releaseMutation.error,

    // 按需取某版本明细（含工序行），用于「查看」。失败抛错由调用方处理。
    fetchRoutingDetail: async (routingCode: string, revision: string) => {
      const res = await getBusinessConsoleEngineeringRouting({
        path: { routingCode, revision },
        query: { organizationId: filters.organizationId, environmentId: filters.environmentId },
      })
      return unwrapDetail<BusinessConsoleRoutingItem>(
        res as { data?: BusinessConsoleRoutingDetailEnvelope },
      )
    },
  }
}

/**
 * 工程物料（EngineeringItem，修订链）列表（list + filters）+ 新建修订（create-revision）+ 按需取修订明细（get-by-id）。
 *
 * 工程数据语义：物料不是直接编辑，而是从已发布修订「派生新修订」。`createRevision` 带 `release` 标志
 * 决定新修订是否立即发布；不发布则为草稿（Draft）。物料编码 `itemCode` 留空由后端自动编码，
 * 仅在补登历史已知编码时才填（默认不收，遵守自动编码约束）。
 * 状态枚举用后端真值 Draft/Published/Archived（EngineeringVersionStatus）。
 */
export function useEngineeringItems() {
  const context = useBusinessContextStore()
  const queryCache = useQueryCache()
  const filters = reactive<EngineeringItemListFilters>({
    organizationId: context.organizationId,
    environmentId: context.environmentId,
    itemCode: undefined,
    status: undefined,
    skip: 0,
    take: DEFAULT_TAKE,
  })

  const listQuery = useQuery(() =>
    listBusinessConsoleEngineeringItemsQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        ...optionalQuery('itemCode', filters.itemCode),
        ...optionalQuery('status', filters.status),
        skip: filters.skip,
        take: filters.take,
      },
    }),
  )

  function invalidateList() {
    void queryCache
      .invalidateQueries({ predicate: isBusinessQuery('listBusinessConsoleEngineeringItems') })
      .catch(ignoreBackgroundError)
  }

  const createRevisionMutation = useMutation({
    ...createBusinessConsoleEngineeringItemRevisionMutationOptions(),
    onSuccess: invalidateList,
  } as unknown as UseMutationOptions)

  return {
    filters,
    items: computed<BusinessConsoleEngineeringItemRevisionItem[]>(() =>
      unwrapItems(listQuery.data.value as BusinessConsoleEngineeringItemListEnvelope | undefined),
    ),
    itemsError: listQuery.error,
    itemsPending: listQuery.isLoading,
    itemsTotal: computed(() =>
      unwrapTotal(listQuery.data.value as BusinessConsoleEngineeringItemListEnvelope | undefined),
    ),
    refresh: listQuery.refetch,

    createItemRevision: (body: BusinessConsoleCreateEngineeringItemRevisionRequest) =>
      (createRevisionMutation.mutateAsync as unknown as (vars: unknown) => Promise<unknown>)({ body }),
    createPending: createRevisionMutation.isLoading,
    createError: createRevisionMutation.error,

    // 按需取某物料修订明细，用于「查看」。失败抛错由调用方处理。
    fetchItemDetail: async (itemCode: string, revision: string) => {
      const res = await getBusinessConsoleEngineeringItem({
        path: { itemCode, revision },
        query: { organizationId: filters.organizationId, environmentId: filters.environmentId },
      })
      return unwrapDetail<BusinessConsoleEngineeringItemRevisionItem>(
        res as { data?: BusinessConsoleEngineeringItemDetailEnvelope },
      )
    },
  }
}

/**
 * 工程文档（EngineeringDocument，按修订登记）列表（list + filters）+ 登记文档（register）+ 按需取明细（get-by-id）。
 *
 * register 须带 documentNumber/revision/fileId/fileName/contentType/documentType；后端无文件上传通道，
 * fileId 先作文件引用 ID 文本输入（页面标注「文件上传待接入」）。文档号 documentNumber 由用户定义。
 */
export function useEngineeringDocuments() {
  const context = useBusinessContextStore()
  const queryCache = useQueryCache()
  const filters = reactive<EngineeringDocumentListFilters>({
    organizationId: context.organizationId,
    environmentId: context.environmentId,
    itemCode: undefined,
    documentType: undefined,
    skip: 0,
    take: DEFAULT_TAKE,
  })

  const listQuery = useQuery(() =>
    listBusinessConsoleEngineeringDocumentsQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        ...optionalQuery('itemCode', filters.itemCode),
        ...optionalQuery('documentType', filters.documentType),
        skip: filters.skip,
        take: filters.take,
      },
    }),
  )

  function invalidateList() {
    void queryCache
      .invalidateQueries({ predicate: isBusinessQuery('listBusinessConsoleEngineeringDocuments') })
      .catch(ignoreBackgroundError)
  }

  const registerMutation = useMutation({
    ...registerBusinessConsoleEngineeringDocumentMutationOptions(),
    onSuccess: invalidateList,
  } as unknown as UseMutationOptions)

  return {
    filters,
    documents: computed<BusinessConsoleEngineeringDocumentItem[]>(() =>
      unwrapItems(listQuery.data.value as BusinessConsoleEngineeringDocumentListEnvelope | undefined),
    ),
    documentsError: listQuery.error,
    documentsPending: listQuery.isLoading,
    documentsTotal: computed(() =>
      unwrapTotal(listQuery.data.value as BusinessConsoleEngineeringDocumentListEnvelope | undefined),
    ),
    refresh: listQuery.refetch,

    registerDocument: (body: BusinessConsoleRegisterEngineeringDocumentRequest) =>
      (registerMutation.mutateAsync as unknown as (vars: unknown) => Promise<unknown>)({ body }),
    registerPending: registerMutation.isLoading,
    registerError: registerMutation.error,

    // 按需取某文档修订明细，用于「查看」。失败抛错由调用方处理。
    fetchDocumentDetail: async (documentNumber: string, revision: string) => {
      const res = await getBusinessConsoleEngineeringDocument({
        path: { documentNumber, revision },
        query: { organizationId: filters.organizationId, environmentId: filters.environmentId },
      })
      return unwrapDetail<BusinessConsoleEngineeringDocumentItem>(
        res as { data?: BusinessConsoleEngineeringDocumentDetailEnvelope },
      )
    },
  }
}

/**
 * 工程变更（EngineeringChange/ECO）列表（list + filters）+ 发布变更（release）+ 按需取明细（get-by-id 看受影响版本）。
 *
 * **后端是一步发布**（Open→Approve→Release 一气呵成），没有多步审批工作流、没有草稿/待审状态——
 * 页面只做「发布变更」向导，不假造审批态。release 须带 reason/approvalReferenceId/effectiveDate/affectedVersions[]；
 * 变更号 changeNumber 留空由后端自动编码（默认不收）。
 */
export function useEngineeringChanges() {
  const context = useBusinessContextStore()
  const queryCache = useQueryCache()
  const filters = reactive<EngineeringChangeListFilters>({
    organizationId: context.organizationId,
    environmentId: context.environmentId,
    status: undefined,
    skip: 0,
    take: DEFAULT_TAKE,
  })

  const listQuery = useQuery(() =>
    listBusinessConsoleEngineeringChangesQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        ...optionalQuery('status', filters.status),
        skip: filters.skip,
        take: filters.take,
      },
    }),
  )

  function invalidateList() {
    void queryCache
      .invalidateQueries({ predicate: isBusinessQuery('listBusinessConsoleEngineeringChanges') })
      .catch(ignoreBackgroundError)
  }

  const releaseMutation = useMutation({
    ...releaseBusinessConsoleEngineeringChangeMutationOptions(),
    onSuccess: invalidateList,
  } as unknown as UseMutationOptions)

  return {
    filters,
    changes: computed<BusinessConsoleEngineeringChangeItem[]>(() =>
      unwrapItems(listQuery.data.value as BusinessConsoleEngineeringChangeListEnvelope | undefined),
    ),
    changesError: listQuery.error,
    changesPending: listQuery.isLoading,
    changesTotal: computed(() =>
      unwrapTotal(listQuery.data.value as BusinessConsoleEngineeringChangeListEnvelope | undefined),
    ),
    refresh: listQuery.refetch,

    releaseChange: (body: BusinessConsoleReleaseEngineeringChangeRequest) =>
      (releaseMutation.mutateAsync as unknown as (vars: unknown) => Promise<unknown>)({ body }),
    releasePending: releaseMutation.isLoading,
    releaseError: releaseMutation.error,

    // 按需取某变更明细（含受影响版本），用于「查看」。失败抛错由调用方处理。
    fetchChangeDetail: async (changeNumber: string) => {
      const res = await getBusinessConsoleEngineeringChange({
        path: { changeNumber },
        query: { organizationId: filters.organizationId, environmentId: filters.environmentId },
      })
      return unwrapDetail<BusinessConsoleEngineeringChangeItem>(
        res as { data?: BusinessConsoleEngineeringChangeDetailEnvelope },
      )
    },
  }
}
