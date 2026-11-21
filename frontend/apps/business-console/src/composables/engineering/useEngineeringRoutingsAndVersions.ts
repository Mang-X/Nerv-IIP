import {
  archiveBusinessConsoleEngineeringProductionVersionMutationOptions,
  createBusinessConsoleEngineeringProductionVersionMutationOptions,
  getBusinessConsoleEngineeringRoutingQueryOptions,
  listBusinessConsoleEngineeringProductionVersionsQueryOptions,
  listBusinessConsoleEngineeringRoutingsQueryOptions,
  listBusinessConsoleEngineeringStandardOperationsQueryOptions,
  createBusinessConsoleEngineeringStandardOperationMutationOptions,
  updateBusinessConsoleEngineeringStandardOperationMutationOptions,
  archiveBusinessConsoleEngineeringStandardOperationMutationOptions,
  releaseBusinessConsoleEngineeringRoutingMutationOptions,
  resolveBusinessConsoleEngineeringProductionVersionQueryOptions,
  updateBusinessConsoleEngineeringProductionVersionMutationOptions,
  type BusinessConsoleCreateProductionVersionRequest,
  type BusinessConsoleProductionVersionItem,
  type BusinessConsoleProductionVersionListEnvelope,
  type BusinessConsoleStandardOperationItem,
  type BusinessConsoleStandardOperationListEnvelope,
  type BusinessConsoleCreateStandardOperationRequest,
  type BusinessConsoleUpdateStandardOperationRequest,
  type BusinessConsoleReleaseRoutingRequest,
  type BusinessConsoleResolveProductionVersionResponse,
  type BusinessConsoleRoutingItem,
  type BusinessConsoleRoutingListEnvelope,
  type BusinessConsoleUpdateProductionVersionRequest,
} from '@nerv-iip/api-client'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useMutation, useQuery, useQueryCache, type UseMutationOptions } from '@pinia/colada'
import { computed, reactive, ref, shallowRef } from 'vue'
import {
  bindBusinessContext,
  hasBusinessContext,
  refetchWithBusinessContext,
  withBusinessContextEnabled,
} from '../businessContextBinding'
import {
  DEFAULT_TAKE,
  PUBLISHED,
  ignoreBackgroundError,
  isBusinessQuery,
  optionalQuery,
  runQueryOption,
  unwrapDetail,
  unwrapItems,
  unwrapTotal,
} from './engineeringShared'

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

export interface RoutingListFilters {
  organizationId: string
  environmentId: string
  skuCode?: string
  status?: string
  skip: number
  take: number
}

/**
 * 生产版本列表（list + filters）+ 三件套写操作（create/update/archive）。
 * 写成功后失效列表查询，即时刷新。
 */
export function useEngineeringProductionVersions(enabled = () => true) {
  const context = useBusinessContextStore()
  const queryCache = useQueryCache()
  const filters = bindBusinessContext(
    reactive<ProductionVersionListFilters>({
      organizationId: context.organizationId,
      environmentId: context.environmentId,
      skuCode: undefined,
      status: undefined,
      skip: 0,
      take: DEFAULT_TAKE,
    }),
  )

  const listQuery = useQuery(() => ({
    ...listBusinessConsoleEngineeringProductionVersionsQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        ...optionalQuery('skuCode', filters.skuCode),
        ...optionalQuery('status', filters.status),
        skip: filters.skip,
        take: filters.take,
      },
    }),
    enabled: hasBusinessContext(filters) && enabled(),
  }))

  const visibleList = computed(() =>
    enabled()
      ? (listQuery.data.value as BusinessConsoleProductionVersionListEnvelope | undefined)
      : undefined,
  )

  function invalidateList() {
    void queryCache
      .invalidateQueries({
        predicate: isBusinessQuery('listBusinessConsoleEngineeringProductionVersions'),
      })
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
      unwrapItems(visibleList.value),
    ),
    productionVersionsError: computed(() => (enabled() ? listQuery.error.value : null)),
    productionVersionsPending: computed(() => enabled() && listQuery.isLoading.value),
    productionVersionsTotal: computed(() => unwrapTotal(visibleList.value)),
    refresh: () =>
      enabled() ? refetchWithBusinessContext(filters, listQuery) : Promise.resolve(undefined),

    createProductionVersion: (body: BusinessConsoleCreateProductionVersionRequest) =>
      createMutation.mutateAsync({ body }),
    createPending: createMutation.isLoading,
    createError: createMutation.error,

    // 后端 update 走 `PUT .../{productionVersionId}`，org/env 在 query，绑定字段在 body。
    updateProductionVersion: (
      productionVersionId: string,
      body: BusinessConsoleUpdateProductionVersionRequest,
    ) =>
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
  const resolved = shallowRef<BusinessConsoleResolveProductionVersionResponse | undefined>(
    undefined,
  )
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
    } finally {
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
 * 已发布工艺路线选择器数据源（status=Published）。绑定值用 `routingCode`。
 */
export function usePublishedRoutings() {
  const context = useBusinessContextStore()
  const filters = bindBusinessContext(
    reactive({
      organizationId: context.organizationId,
      environmentId: context.environmentId,
      skuCode: undefined as string | undefined,
    }),
  )

  const query = useQuery(() =>
    withBusinessContextEnabled(
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
      filters,
    ),
  )

  return {
    filters,
    routings: computed<BusinessConsoleRoutingItem[]>(() =>
      unwrapItems(query.data.value as BusinessConsoleRoutingListEnvelope | undefined),
    ),
    routingsError: query.error,
    routingsPending: query.isLoading,
    refreshRoutings: () => refetchWithBusinessContext(filters, query),
  }
}

/**
 * 工艺路线列表（list + filters）+ 发布新版本（release）+ 按需取版本明细（get-by-id）。
 * Routing list 不含工序明细；查看时用 `fetchRoutingDetail(routingCode, revision)` 拉 get-by-id 取工序行（#389 已交付）。
 */
export function useEngineeringRoutings() {
  const context = useBusinessContextStore()
  const queryCache = useQueryCache()
  const filters = bindBusinessContext(
    reactive<RoutingListFilters>({
      organizationId: context.organizationId,
      environmentId: context.environmentId,
      skuCode: undefined,
      status: undefined,
      skip: 0,
      take: DEFAULT_TAKE,
    }),
  )

  const listQuery = useQuery(() =>
    withBusinessContextEnabled(
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
      filters,
    ),
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
    refresh: () => refetchWithBusinessContext(filters, listQuery),

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
  const filters = bindBusinessContext(
    reactive<StandardOperationListFilters>({
      organizationId: context.organizationId,
      environmentId: context.environmentId,
      enabled: undefined,
      search: undefined,
      skip: 0,
      take: DEFAULT_TAKE,
    }),
  )

  const listQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsoleEngineeringStandardOperationsQueryOptions({
        query: {
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
          ...optionalQuery('enabled', filters.enabled),
          ...optionalQuery('search', filters.search),
          skip: filters.skip,
          take: filters.take,
        },
      }),
      filters,
    ),
  )

  function invalidateList() {
    void queryCache
      .invalidateQueries({
        predicate: isBusinessQuery('listBusinessConsoleEngineeringStandardOperations'),
      })
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
    refresh: () => refetchWithBusinessContext(filters, listQuery),

    // 创建 body 含 org/env + 用户自定义 operationCode（页面提供）。
    createStandardOperation: (body: BusinessConsoleCreateStandardOperationRequest) =>
      createMutation.mutateAsync({ body }),
    createPending: createMutation.isLoading,

    // 更新走 `PUT .../{operationCode}`，org/env 在 body（无 query）。
    updateStandardOperation: (
      operationCode: string,
      body: BusinessConsoleUpdateStandardOperationRequest,
    ) =>
      (updateMutation.mutateAsync as unknown as (vars: unknown) => Promise<unknown>)({
        path: { operationCode },
        body,
      }),
    updatePending: updateMutation.isLoading,

    // 归档走 `POST .../{operationCode}/archive`，body 带 org/env + reason。
    archiveStandardOperation: (operationCode: string, reason: string) =>
      (archiveMutation.mutateAsync as unknown as (vars: unknown) => Promise<unknown>)({
        path: { operationCode },
        body: {
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
          reason,
        },
      }),
    archivePending: archiveMutation.isLoading,
  }
}
