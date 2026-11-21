import {
  getBusinessConsoleEngineeringBomQueryOptions,
  getBusinessConsoleEngineeringManufacturingBomQueryOptions,
  listBusinessConsoleEngineeringBomsQueryOptions,
  listBusinessConsoleEngineeringManufacturingBomsQueryOptions,
  releaseBusinessConsoleEngineeringBomMutationOptions,
  releaseBusinessConsoleEngineeringManufacturingBomMutationOptions,
  type BusinessConsoleEngineeringBomItem,
  type BusinessConsoleEngineeringBomListEnvelope,
  type BusinessConsoleManufacturingBomItem,
  type BusinessConsoleManufacturingBomListEnvelope,
  type BusinessConsoleReleaseEngineeringBomRequest,
  type BusinessConsoleReleaseManufacturingBomRequest,
} from '@nerv-iip/api-client'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useMutation, useQuery, useQueryCache, type UseMutationOptions } from '@pinia/colada'
import { computed, reactive } from 'vue'
import {
  bindBusinessContext,
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

/**
 * 已发布 MBOM 选择器数据源（status=Published）。绑定值用 `bomCode`，label 显示 `code · rev · SKU`。
 */
export function usePublishedMboms() {
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
      filters,
    ),
  )

  return {
    filters,
    mboms: computed<BusinessConsoleManufacturingBomItem[]>(() =>
      unwrapItems(query.data.value as BusinessConsoleManufacturingBomListEnvelope | undefined),
    ),
    mbomsError: query.error,
    mbomsPending: query.isLoading,
    refreshMboms: () => refetchWithBusinessContext(filters, query),
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
  const filters = bindBusinessContext(
    reactive<EngineeringBomListFilters>({
      organizationId: context.organizationId,
      environmentId: context.environmentId,
      parentItemCode: undefined,
      status: undefined,
      skip: 0,
      take: DEFAULT_TAKE,
    }),
  )

  const listQuery = useQuery(() =>
    withBusinessContextEnabled(
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
      filters,
    ),
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
    refresh: () => refetchWithBusinessContext(filters, listQuery),

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
  const filters = bindBusinessContext(
    reactive({
      organizationId: context.organizationId,
      environmentId: context.environmentId,
      parentItemCode: undefined as string | undefined,
    }),
  )

  const query = useQuery(() =>
    withBusinessContextEnabled(
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
      filters,
    ),
  )

  return {
    filters,
    eboms: computed<BusinessConsoleEngineeringBomItem[]>(() =>
      unwrapItems(query.data.value as BusinessConsoleEngineeringBomListEnvelope | undefined),
    ),
    ebomsError: query.error,
    ebomsPending: query.isLoading,
    refreshEboms: () => refetchWithBusinessContext(filters, query),
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
  const filters = bindBusinessContext(
    reactive<ManufacturingBomListFilters>({
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
      filters,
    ),
  )

  function invalidateList() {
    void queryCache
      .invalidateQueries({
        predicate: isBusinessQuery('listBusinessConsoleEngineeringManufacturingBoms'),
      })
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
    refresh: () => refetchWithBusinessContext(filters, listQuery),

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
