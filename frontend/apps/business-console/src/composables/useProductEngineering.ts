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
  createBusinessConsoleEngineeringProductionVersionMutationOptions,
  listBusinessConsoleEngineeringManufacturingBomsQueryOptions,
  listBusinessConsoleEngineeringProductionVersionsQueryOptions,
  listBusinessConsoleEngineeringRoutingsQueryOptions,
  resolveBusinessConsoleEngineeringProductionVersion,
  updateBusinessConsoleEngineeringProductionVersionMutationOptions,
  type BusinessConsoleCreateProductionVersionRequest,
  type BusinessConsoleManufacturingBomItem,
  type BusinessConsoleManufacturingBomListEnvelope,
  type BusinessConsoleProductionVersionItem,
  type BusinessConsoleProductionVersionListEnvelope,
  type BusinessConsoleResolveProductionVersionEnvelope,
  type BusinessConsoleResolveProductionVersionResponse,
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
