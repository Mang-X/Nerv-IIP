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
  getBusinessConsoleEngineeringBomDiffQueryOptions,
  getBusinessConsoleEngineeringBomExplosionQueryOptions,
  getBusinessConsoleEngineeringBomQueryOptions,
  getBusinessConsoleEngineeringBomWhereUsedQueryOptions,
  getBusinessConsoleEngineeringChangeQueryOptions,
  getBusinessConsoleEngineeringDocumentQueryOptions,
  getBusinessConsoleEngineeringItemQueryOptions,
  getBusinessConsoleEngineeringManufacturingBomExplosionQueryOptions,
  getBusinessConsoleEngineeringManufacturingBomQueryOptions,
  getBusinessConsoleEngineeringManufacturingBomWhereUsedQueryOptions,
  getBusinessConsoleEngineeringRoutingQueryOptions,
  listBusinessConsoleEngineeringBomsQueryOptions,
  listBusinessConsoleEngineeringChangesQueryOptions,
  listBusinessConsoleEngineeringDocumentsQueryOptions,
  listBusinessConsoleEngineeringItemsQueryOptions,
  listBusinessConsoleEngineeringManufacturingBomsQueryOptions,
  listBusinessConsoleEngineeringProductionVersionsQueryOptions,
  listBusinessConsoleEngineeringRoutingsQueryOptions,
  listBusinessConsoleEngineeringStandardOperationsQueryOptions,
  createBusinessConsoleEngineeringStandardOperationMutationOptions,
  updateBusinessConsoleEngineeringStandardOperationMutationOptions,
  archiveBusinessConsoleEngineeringStandardOperationMutationOptions,
  registerBusinessConsoleEngineeringDocumentMutationOptions,
  previewBusinessConsoleEngineeringChangeImpactMutationOptions,
  releaseBusinessConsoleEngineeringBomMutationOptions,
  releaseBusinessConsoleEngineeringChangeMutationOptions,
  releaseBusinessConsoleEngineeringManufacturingBomMutationOptions,
  releaseBusinessConsoleEngineeringRoutingMutationOptions,
  resolveBusinessConsoleEngineeringProductionVersionQueryOptions,
  updateBusinessConsoleEngineeringProductionVersionMutationOptions,
  type BusinessConsoleBomExplosionResponse,
  type BusinessConsoleBomDiffResponse,
  type BusinessConsoleBomWhereUsedResponse,
  type BusinessConsoleCreateEngineeringItemRevisionRequest,
  type BusinessConsoleCreateProductionVersionRequest,
  type BusinessConsoleEngineeringBomItem,
  type BusinessConsoleEngineeringBomListEnvelope,
  type BusinessConsoleEngineeringChangeItem,
  type BusinessConsoleEngineeringChangeListEnvelope,
  type BusinessConsoleEngineeringDocumentItem,
  type BusinessConsoleEngineeringDocumentListEnvelope,
  type BusinessConsoleEngineeringItemRevisionItem,
  type BusinessConsoleEngineeringItemListEnvelope,
  type BusinessConsoleManufacturingBomItem,
  type BusinessConsoleManufacturingBomListEnvelope,
  type BusinessConsoleProductionVersionItem,
  type BusinessConsoleProductionVersionListEnvelope,
  type BusinessConsoleStandardOperationItem,
  type BusinessConsoleStandardOperationListEnvelope,
  type BusinessConsoleCreateStandardOperationRequest,
  type BusinessConsoleUpdateStandardOperationRequest,
  type BusinessConsoleRegisterEngineeringDocumentRequest,
  type BusinessConsoleReleaseEngineeringBomRequest,
  type BusinessConsoleReleaseEngineeringChangeRequest,
  type BusinessConsoleEngineeringChangeImpactPreviewRequest,
  type BusinessConsoleEngineeringChangeImpactPreviewResponse,
  type BusinessConsoleReleaseManufacturingBomRequest,
  type BusinessConsoleReleaseRoutingRequest,
  type BusinessConsoleResolveProductionVersionResponse,
  type BusinessConsoleRoutingItem,
  type BusinessConsoleRoutingListEnvelope,
  type BusinessConsoleUpdateProductionVersionRequest,
} from '@nerv-iip/api-client'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useMutation, useQuery, useQueryCache, type UseMutationOptions, type UseQueryEntry } from '@pinia/colada'
import { computed, reactive, ref, shallowRef } from 'vue'
import { bindBusinessContext, withBusinessContextEnabled } from './businessContextBinding'

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

export interface BomExplosionInput {
  code: string
  effectiveDate: string
  lotSize?: number
  bomCode?: string
  revision?: string
}

export interface BomWhereUsedInput {
  componentCode: string
  effectiveDate: string
}

export interface BomDiffInput {
  bomKind: 'engineering' | 'manufacturing'
  fromBomCode: string
  fromRevision: string
  toBomCode: string
  toRevision: string
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

/** 解包 get-by-id 单体响应（query option 返回 `{ success, data }` envelope）。 */
function unwrapDetail<T>(envelope: { success?: boolean, data?: T | null } | undefined): T | undefined {
  if (!envelope?.success) return undefined
  return envelope.data ?? undefined
}

async function runQueryOption<T>(options: { query: (context: never) => Promise<T> }): Promise<T> {
  return options.query({} as never)
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

export function useBomAnalysis() {
  const context = useBusinessContextStore()
  const explosion = shallowRef<BusinessConsoleBomExplosionResponse>()
  const diff = shallowRef<BusinessConsoleBomDiffResponse>()
  const whereUsed = shallowRef<BusinessConsoleBomWhereUsedResponse>()
  const pending = ref(false)
  const error = ref<unknown>()

  async function run<T>(work: () => Promise<T>): Promise<T | undefined> {
    pending.value = true
    error.value = undefined
    try {
      return await work()
    }
    catch (err) {
      error.value = err
      throw err
    }
    finally {
      pending.value = false
    }
  }

  function commonQuery() {
    return {
      organizationId: context.organizationId,
      environmentId: context.environmentId,
    }
  }

  return {
    explosion,
    diff,
    whereUsed,
    pending,
    error,
    loadBomDiff: (input: BomDiffInput) =>
      run(async () => {
        const envelope = await runQueryOption(
          getBusinessConsoleEngineeringBomDiffQueryOptions({
            query: {
              ...commonQuery(),
              bomKind: input.bomKind,
              fromBomCode: input.fromBomCode,
              fromRevision: input.fromRevision,
              toBomCode: input.toBomCode,
              toRevision: input.toRevision,
            },
          }),
        )
        diff.value = unwrapDetail<BusinessConsoleBomDiffResponse>(envelope)
        explosion.value = undefined
        whereUsed.value = undefined
        return diff.value
      }),
    loadEngineeringExplosion: (input: BomExplosionInput) =>
      run(async () => {
        const envelope = await runQueryOption(
          getBusinessConsoleEngineeringBomExplosionQueryOptions({
            query: {
              ...commonQuery(),
              itemCode: input.code,
              effectiveDate: input.effectiveDate,
              ...optionalQuery('lotSize', input.lotSize),
              ...optionalQuery('bomCode', input.bomCode),
              ...optionalQuery('revision', input.revision),
            },
          }),
        )
        explosion.value = unwrapDetail<BusinessConsoleBomExplosionResponse>(envelope)
        diff.value = undefined
        whereUsed.value = undefined
        return explosion.value
      }),
    loadManufacturingExplosion: (input: BomExplosionInput) =>
      run(async () => {
        const envelope = await runQueryOption(
          getBusinessConsoleEngineeringManufacturingBomExplosionQueryOptions({
            query: {
              ...commonQuery(),
              skuCode: input.code,
              effectiveDate: input.effectiveDate,
              ...optionalQuery('lotSize', input.lotSize),
              ...optionalQuery('bomCode', input.bomCode),
              ...optionalQuery('revision', input.revision),
            },
          }),
        )
        explosion.value = unwrapDetail<BusinessConsoleBomExplosionResponse>(envelope)
        diff.value = undefined
        whereUsed.value = undefined
        return explosion.value
      }),
    loadEngineeringWhereUsed: (input: BomWhereUsedInput) =>
      run(async () => {
        const envelope = await runQueryOption(
          getBusinessConsoleEngineeringBomWhereUsedQueryOptions({
            query: {
              ...commonQuery(),
              componentCode: input.componentCode,
              effectiveDate: input.effectiveDate,
            },
          }),
        )
        whereUsed.value = unwrapDetail<BusinessConsoleBomWhereUsedResponse>(envelope)
        explosion.value = undefined
        diff.value = undefined
        return whereUsed.value
      }),
    loadManufacturingWhereUsed: (input: BomWhereUsedInput) =>
      run(async () => {
        const envelope = await runQueryOption(
          getBusinessConsoleEngineeringManufacturingBomWhereUsedQueryOptions({
            query: {
              ...commonQuery(),
              componentCode: input.componentCode,
              effectiveDate: input.effectiveDate,
            },
          }),
        )
        whereUsed.value = unwrapDetail<BusinessConsoleBomWhereUsedResponse>(envelope)
        explosion.value = undefined
        diff.value = undefined
        return whereUsed.value
      }),
  }
}

/**
 * 生产版本列表（list + filters）+ 三件套写操作（create/update/archive）。
 * 写成功后失效列表查询，即时刷新。
 */
export function useEngineeringProductionVersions() {
  const context = useBusinessContextStore()
  const queryCache = useQueryCache()
  const filters = bindBusinessContext(reactive<ProductionVersionListFilters>({
    organizationId: context.organizationId,
    environmentId: context.environmentId,
    skuCode: undefined,
    status: undefined,
    skip: 0,
    take: DEFAULT_TAKE,
  }))

  const listQuery = useQuery(() =>
    withBusinessContextEnabled(listBusinessConsoleEngineeringProductionVersionsQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        ...optionalQuery('skuCode', filters.skuCode),
        ...optionalQuery('status', filters.status),
        skip: filters.skip,
        take: filters.take,
      },
    }), filters),
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
 * 为「按需触发」语义（点解析才发请求），执行 stable query option，不直接绕 generated SDK。
 */
export function useProductionVersionResolve() {
  const context = useBusinessContextStore()
  const resolved = shallowRef<BusinessConsoleResolveProductionVersionResponse | undefined>(undefined)
  const pending = ref(false)
  const resolvedOnce = ref(false)

  async function resolve(input: ProductionVersionResolveInput) {
    pending.value = true
    try {
      const envelope = await runQueryOption(
        resolveBusinessConsoleEngineeringProductionVersionQueryOptions({
          query: {
            organizationId: context.organizationId,
            environmentId: context.environmentId,
            skuCode: input.skuCode,
            effectiveDate: input.effectiveDate,
            lotSize: input.lotSize,
          },
        }),
      )
      resolved.value = unwrapDetail<BusinessConsoleResolveProductionVersionResponse>(envelope)
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
  const filters = bindBusinessContext(reactive({
    organizationId: context.organizationId,
    environmentId: context.environmentId,
    skuCode: undefined as string | undefined,
  }))

  const query = useQuery(() =>
    withBusinessContextEnabled(listBusinessConsoleEngineeringManufacturingBomsQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        ...optionalQuery('skuCode', filters.skuCode),
        status: PUBLISHED,
        skip: 0,
        take: DEFAULT_TAKE,
      },
    }), filters),
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
  const filters = bindBusinessContext(reactive({
    organizationId: context.organizationId,
    environmentId: context.environmentId,
    skuCode: undefined as string | undefined,
  }))

  const query = useQuery(() =>
    withBusinessContextEnabled(listBusinessConsoleEngineeringRoutingsQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        ...optionalQuery('skuCode', filters.skuCode),
        status: PUBLISHED,
        skip: 0,
        take: DEFAULT_TAKE,
      },
    }), filters),
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
  const filters = bindBusinessContext(reactive<EngineeringBomListFilters>({
    organizationId: context.organizationId,
    environmentId: context.environmentId,
    parentItemCode: undefined,
    status: undefined,
    skip: 0,
    take: DEFAULT_TAKE,
  }))

  const listQuery = useQuery(() =>
    withBusinessContextEnabled(listBusinessConsoleEngineeringBomsQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        ...optionalQuery('parentItemCode', filters.parentItemCode),
        ...optionalQuery('status', filters.status),
        skip: filters.skip,
        take: filters.take,
      },
    }), filters),
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
      const envelope = await runQueryOption(
        getBusinessConsoleEngineeringBomQueryOptions({
          path: { bomCode, revision },
          query: { organizationId: filters.organizationId, environmentId: filters.environmentId },
        }),
      )
      return unwrapDetail<BusinessConsoleEngineeringBomItem>(envelope)
    },
  }
}

/**
 * 已发布 EBOM 选择器数据源（status=Published），供 MBOM 发布向导选「引用的设计 BOM」。
 * 绑定用 `bomCode` + `revision`（MBOM release 需 engineeringBomCode + engineeringBomRevision）。
 */
export function usePublishedEboms() {
  const context = useBusinessContextStore()
  const filters = bindBusinessContext(reactive({
    organizationId: context.organizationId,
    environmentId: context.environmentId,
    parentItemCode: undefined as string | undefined,
  }))

  const query = useQuery(() =>
    withBusinessContextEnabled(listBusinessConsoleEngineeringBomsQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        ...optionalQuery('parentItemCode', filters.parentItemCode),
        status: PUBLISHED,
        skip: 0,
        take: DEFAULT_TAKE,
      },
    }), filters),
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
  const filters = bindBusinessContext(reactive<ManufacturingBomListFilters>({
    organizationId: context.organizationId,
    environmentId: context.environmentId,
    skuCode: undefined,
    status: undefined,
    skip: 0,
    take: DEFAULT_TAKE,
  }))

  const listQuery = useQuery(() =>
    withBusinessContextEnabled(listBusinessConsoleEngineeringManufacturingBomsQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        ...optionalQuery('skuCode', filters.skuCode),
        ...optionalQuery('status', filters.status),
        skip: filters.skip,
        take: filters.take,
      },
    }), filters),
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
      const envelope = await runQueryOption(
        getBusinessConsoleEngineeringManufacturingBomQueryOptions({
          path: { bomCode, revision },
          query: { organizationId: filters.organizationId, environmentId: filters.environmentId },
        }),
      )
      return unwrapDetail<BusinessConsoleManufacturingBomItem>(envelope)
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
  const filters = bindBusinessContext(reactive<RoutingListFilters>({
    organizationId: context.organizationId,
    environmentId: context.environmentId,
    skuCode: undefined,
    status: undefined,
    skip: 0,
    take: DEFAULT_TAKE,
  }))

  const listQuery = useQuery(() =>
    withBusinessContextEnabled(listBusinessConsoleEngineeringRoutingsQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        ...optionalQuery('skuCode', filters.skuCode),
        ...optionalQuery('status', filters.status),
        skip: filters.skip,
        take: filters.take,
      },
    }), filters),
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
      const envelope = await runQueryOption(
        getBusinessConsoleEngineeringRoutingQueryOptions({
          path: { routingCode, revision },
          query: { organizationId: filters.organizationId, environmentId: filters.environmentId },
        }),
      )
      return unwrapDetail<BusinessConsoleRoutingItem>(envelope)
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
  const filters = bindBusinessContext(reactive<EngineeringItemListFilters>({
    organizationId: context.organizationId,
    environmentId: context.environmentId,
    itemCode: undefined,
    status: undefined,
    skip: 0,
    take: DEFAULT_TAKE,
  }))

  const listQuery = useQuery(() =>
    withBusinessContextEnabled(listBusinessConsoleEngineeringItemsQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        ...optionalQuery('itemCode', filters.itemCode),
        ...optionalQuery('status', filters.status),
        skip: filters.skip,
        take: filters.take,
      },
    }), filters),
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
      const envelope = await runQueryOption(
        getBusinessConsoleEngineeringItemQueryOptions({
          path: { itemCode, revision },
          query: { organizationId: filters.organizationId, environmentId: filters.environmentId },
        }),
      )
      return unwrapDetail<BusinessConsoleEngineeringItemRevisionItem>(envelope)
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
  const filters = bindBusinessContext(reactive<EngineeringDocumentListFilters>({
    organizationId: context.organizationId,
    environmentId: context.environmentId,
    itemCode: undefined,
    documentType: undefined,
    skip: 0,
    take: DEFAULT_TAKE,
  }))

  const listQuery = useQuery(() =>
    withBusinessContextEnabled(listBusinessConsoleEngineeringDocumentsQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        ...optionalQuery('itemCode', filters.itemCode),
        ...optionalQuery('documentType', filters.documentType),
        skip: filters.skip,
        take: filters.take,
      },
    }), filters),
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
      const envelope = await runQueryOption(
        getBusinessConsoleEngineeringDocumentQueryOptions({
          path: { documentNumber, revision },
          query: { organizationId: filters.organizationId, environmentId: filters.environmentId },
        }),
      )
      return unwrapDetail<BusinessConsoleEngineeringDocumentItem>(envelope)
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
  const filters = bindBusinessContext(reactive<EngineeringChangeListFilters>({
    organizationId: context.organizationId,
    environmentId: context.environmentId,
    status: undefined,
    skip: 0,
    take: DEFAULT_TAKE,
  }))

  const listQuery = useQuery(() =>
    withBusinessContextEnabled(listBusinessConsoleEngineeringChangesQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        ...optionalQuery('status', filters.status),
        skip: filters.skip,
        take: filters.take,
      },
    }), filters),
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
  const previewMutation = useMutation({
    ...previewBusinessConsoleEngineeringChangeImpactMutationOptions(),
  } as unknown as UseMutationOptions)
  const impactPreview = shallowRef<BusinessConsoleEngineeringChangeImpactPreviewResponse>()

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

    previewImpact: async (body: BusinessConsoleEngineeringChangeImpactPreviewRequest) => {
      const envelope = await (previewMutation.mutateAsync as unknown as (vars: unknown) => Promise<unknown>)({ body })
      impactPreview.value = unwrapDetail<BusinessConsoleEngineeringChangeImpactPreviewResponse>(
        envelope as { success?: boolean, data?: BusinessConsoleEngineeringChangeImpactPreviewResponse | null } | undefined,
      )
      return impactPreview.value
    },
    previewPending: previewMutation.isLoading,
    previewError: previewMutation.error,
    impactPreview,
    clearImpactPreview: () => {
      impactPreview.value = undefined
    },

    // 按需取某变更明细（含受影响版本），用于「查看」。失败抛错由调用方处理。
    fetchChangeDetail: async (changeNumber: string) => {
      const envelope = await runQueryOption(
        getBusinessConsoleEngineeringChangeQueryOptions({
          path: { changeNumber },
          query: { organizationId: filters.organizationId, environmentId: filters.environmentId },
        }),
      )
      return unwrapDetail<BusinessConsoleEngineeringChangeItem>(envelope)
    },
  }
}

/**
 * 标准工序 StandardOperation（#397 已交付，真实接线）。
 *
 * 关键契约（以生成层为准）：
 * - 身份是 `operationCode`（用户自定义、创建必填，**不走自动编码**）；无独立 id。
 * - 列表 `{ items, total }`，支持 enabled/search/skip/take 过滤。
 * - 创建/更新 body 含 org/env；更新走 `PUT .../{operationCode}`（org/env 在 body，不在 query）。
 * - 控制模型：`controlKey`(必填字符串) + 三个布尔 requiresReporting/requiresQualityInspection/isOutsourced。
 * - 工时 `standardSetupMinutes`/`standardRunMinutes` 为整数分钟（可空）。
 */
export interface StandardOperationListFilters {
  organizationId: string
  environmentId: string
  enabled?: boolean
  search?: string
  skip: number
  take: number
}

export function useStandardOperations() {
  const context = useBusinessContextStore()
  const queryCache = useQueryCache()
  const filters = bindBusinessContext(reactive<StandardOperationListFilters>({
    organizationId: context.organizationId,
    environmentId: context.environmentId,
    enabled: undefined,
    search: undefined,
    skip: 0,
    take: DEFAULT_TAKE,
  }))

  const listQuery = useQuery(() =>
    withBusinessContextEnabled(listBusinessConsoleEngineeringStandardOperationsQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        ...optionalQuery('enabled', filters.enabled),
        ...optionalQuery('search', filters.search),
        skip: filters.skip,
        take: filters.take,
      },
    }), filters),
  )

  function invalidateList() {
    void queryCache
      .invalidateQueries({ predicate: isBusinessQuery('listBusinessConsoleEngineeringStandardOperations') })
      .catch(ignoreBackgroundError)
  }

  const createMutation = useMutation({
    ...createBusinessConsoleEngineeringStandardOperationMutationOptions(),
    onSuccess: invalidateList,
  })
  const updateMutation = useMutation({
    ...updateBusinessConsoleEngineeringStandardOperationMutationOptions(),
    onSuccess: invalidateList,
  } as unknown as UseMutationOptions)
  const archiveMutation = useMutation({
    ...archiveBusinessConsoleEngineeringStandardOperationMutationOptions(),
    onSuccess: invalidateList,
  } as unknown as UseMutationOptions)

  return {
    filters,
    standardOperations: computed<BusinessConsoleStandardOperationItem[]>(() =>
      unwrapItems(listQuery.data.value as BusinessConsoleStandardOperationListEnvelope | undefined),
    ),
    standardOperationsError: listQuery.error,
    standardOperationsPending: listQuery.isLoading,
    standardOperationsTotal: computed(() =>
      unwrapTotal(listQuery.data.value as BusinessConsoleStandardOperationListEnvelope | undefined),
    ),
    refresh: listQuery.refetch,

    // 创建 body 含 org/env + 用户自定义 operationCode（页面提供）。
    createStandardOperation: (body: BusinessConsoleCreateStandardOperationRequest) =>
      createMutation.mutateAsync({ body }),
    createPending: createMutation.isLoading,

    // 更新走 `PUT .../{operationCode}`，org/env 在 body（无 query）。
    updateStandardOperation: (operationCode: string, body: BusinessConsoleUpdateStandardOperationRequest) =>
      (updateMutation.mutateAsync as unknown as (vars: unknown) => Promise<unknown>)({
        path: { operationCode },
        body,
      }),
    updatePending: updateMutation.isLoading,

    // 归档走 `POST .../{operationCode}/archive`，body 带 org/env + reason。
    archiveStandardOperation: (operationCode: string, reason: string) =>
      (archiveMutation.mutateAsync as unknown as (vars: unknown) => Promise<unknown>)({
        path: { operationCode },
        body: { organizationId: filters.organizationId, environmentId: filters.environmentId, reason },
      }),
    archivePending: archiveMutation.isLoading,
  }
}
