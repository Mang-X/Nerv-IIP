import { useAuthStore } from '@/stores/auth'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import {
  createBusinessConsoleBusinessPartnerMutationOptions,
  createBusinessConsoleReferenceDataCodeMutationOptions,
  createBusinessConsoleSkuMutationOptions,
  createBusinessConsoleUnitOfMeasureMutationOptions,
  createBusinessConsoleUomConversionMutationOptions,
  listBusinessConsoleMasterDataResourcesQueryOptions,
  listBusinessConsoleSkusQueryOptions,
  type BusinessConsoleCreateBusinessPartnerRequest,
  type BusinessConsoleCreateReferenceDataCodeRequest,
  type BusinessConsoleCreateSkuRequest,
  type BusinessConsoleCreateUnitOfMeasureRequest,
  type BusinessConsoleCreateUomConversionRequest,
  type BusinessConsoleResourceItem,
} from '@nerv-iip/api-client'
import { useMutation, useQuery, useQueryCache } from '@pinia/colada'
import { computed } from 'vue'
import {
  hasBusinessContext,
  refetchWithBusinessContext,
  withBusinessContextEnabled,
} from '../businessContextBinding'
import {
  defaultListFilters,
  ignoreBackgroundError,
  invalidateAfterCreate,
  isBusinessQuery,
  optionalQuery,
  resourceItems,
  resourceTotal,
  withCreateIdempotency,
} from './masterDataShared'
import { defaultResourceFilters } from './useMasterDataResources'

export function useBusinessSkus(enabled = () => true) {
  const auth = useAuthStore()
  const canRead = () =>
    enabled() && auth.principal?.permissionCodes?.includes(P.masterDataProductsRead) === true
  const filters = defaultListFilters()

  const skusQuery = useQuery(() => ({
    ...listBusinessConsoleSkusQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        ...optionalQuery('includeDisabled', filters.includeDisabled),
        skip: filters.skip,
        take: filters.take,
      },
    }),
    enabled: hasBusinessContext(filters) && canRead(),
  }))
  const visibleData = computed(() => (canRead() ? skusQuery.data.value : undefined))

  return {
    filters,
    refreshSkus: () =>
      canRead() ? refetchWithBusinessContext(filters, skusQuery) : Promise.resolve(undefined),
    skus: computed<BusinessConsoleResourceItem[]>(() => resourceItems(visibleData.value)),
    skusError: computed(() => (canRead() ? skusQuery.error.value : null)),
    skusPending: computed(() => canRead() && skusQuery.isLoading.value),
    skusTotal: computed(() => resourceTotal(visibleData.value)),
  }
}

/** 新建物料。与列表分开：新增弹窗只要新建，不必连带拉一份物料列表。 */
export function useCreateSku() {
  const queryCache = useQueryCache()
  const mutation = useMutation({
    ...createBusinessConsoleSkuMutationOptions(),
    onSuccess: () => invalidateAfterCreate(queryCache, ['listBusinessConsoleSkus']),
  })
  return {
    create: (body: BusinessConsoleCreateSkuRequest) => mutation.mutateAsync({ body }),
    pending: mutation.isLoading,
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
