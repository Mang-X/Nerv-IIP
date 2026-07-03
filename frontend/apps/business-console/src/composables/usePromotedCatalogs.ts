/**
 * 「字典升主数据」三目录 composable（#400/#401/#402 已交付，真实接线）。
 *
 * 三者均由 codex 升为独立主数据：产品分类（分类树）、质量原因（分组目录）、技能（分组目录）。
 *
 * 统一契约（以生成层为准）：
 * - 身份是各自的 code（categoryCode / skillCode / reasonCode）；列表 `{ items, total }`。
 * - list 查询支持 enabled/search(+reason 的 groupName)/skip/take。
 * - create body 含 org/env；categoryCode/skillCode 可空（自动编码），reasonCode 必填（用户定义）。
 * - update/archive 走 `…/{code}` + `…/{code}/archive`，org/env 在 query，body 不含 org/env。
 */
import {
  archiveBusinessConsoleProductCategoryMutationOptions,
  archiveBusinessConsoleQualityReasonCodeMutationOptions,
  archiveBusinessConsoleSkillMutationOptions,
  createBusinessConsoleProductCategoryMutationOptions,
  createBusinessConsoleQualityReasonCodeMutationOptions,
  createBusinessConsoleSkillMutationOptions,
  listBusinessConsoleProductCategoriesQueryOptions,
  listBusinessConsoleQualityReasonCodesQueryOptions,
  listBusinessConsoleSkillsQueryOptions,
  updateBusinessConsoleProductCategoryMutationOptions,
  updateBusinessConsoleQualityReasonCodeMutationOptions,
  updateBusinessConsoleSkillMutationOptions,
  type BusinessConsoleCreateProductCategoryRequest,
  type BusinessConsoleCreateQualityReasonRequest,
  type BusinessConsoleCreateSkillRequest,
  type BusinessConsoleProductCategoryItem,
  type BusinessConsoleQualityReasonItem,
  type BusinessConsoleSkillItem,
  type BusinessConsoleUpdateProductCategoryRequest,
  type BusinessConsoleUpdateQualityReasonRequest,
  type BusinessConsoleUpdateSkillRequest,
} from '@nerv-iip/api-client'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useMutation, useQuery, type UseMutationOptions } from '@pinia/colada'
import { computed, reactive, ref } from 'vue'
import { bindBusinessContext, refetchWithBusinessContext, withBusinessContextEnabled } from './businessContextBinding'

const DEFAULT_TAKE = 100

// 页面沿用这些名字 import 类型；指向生成层真实契约。
export type ProductCategoryItem = BusinessConsoleProductCategoryItem
export type CreateProductCategoryRequest = BusinessConsoleCreateProductCategoryRequest
export type UpdateProductCategoryRequest = BusinessConsoleUpdateProductCategoryRequest
export type QualityReasonItem = BusinessConsoleQualityReasonItem
export type CreateQualityReasonRequest = BusinessConsoleCreateQualityReasonRequest
export type UpdateQualityReasonRequest = BusinessConsoleUpdateQualityReasonRequest
export type SkillCatalogItem = BusinessConsoleSkillItem
export type CreateSkillCatalogRequest = BusinessConsoleCreateSkillRequest
export type UpdateSkillCatalogRequest = BusinessConsoleUpdateSkillRequest

export interface PromotedListFilters {
  organizationId: string
  environmentId: string
  enabled?: boolean
  search?: string
  groupName?: string
  skip: number
  take: number
}

function optionalQuery<T>(key: string, value: T | undefined): Record<string, T> {
  return value === undefined || value === '' ? {} : { [key]: value }
}
function unwrapItems<T>(envelope: { success?: boolean, data?: { items?: T[] } | null } | undefined): T[] {
  if (!envelope?.success) return []
  return envelope.data?.items ?? []
}
function unwrapTotal(envelope: { success?: boolean, data?: { total?: number } | null } | undefined): number {
  if (!envelope?.success) return 0
  return envelope.data?.total ?? 0
}
const asVars = (fn: unknown) => fn as unknown as (vars: unknown) => Promise<unknown>

// ── 产品分类（分类树）────────────────────────────────────────────
export function useProductCategories() {
  const context = useBusinessContextStore()
  const filters = bindBusinessContext(reactive<PromotedListFilters>({
    organizationId: context.organizationId,
    environmentId: context.environmentId,
    enabled: undefined,
    search: undefined,
    skip: 0,
    take: DEFAULT_TAKE,
  }))
  const listQuery = useQuery(() =>
    withBusinessContextEnabled(listBusinessConsoleProductCategoriesQueryOptions({
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
  const refresh = () => refetchWithBusinessContext(filters, listQuery)
  const createMutation = useMutation({ ...createBusinessConsoleProductCategoryMutationOptions(), onSuccess: refresh })
  const updateMutation = useMutation({ ...updateBusinessConsoleProductCategoryMutationOptions(), onSuccess: refresh } as unknown as UseMutationOptions)
  const archiveMutation = useMutation({ ...archiveBusinessConsoleProductCategoryMutationOptions(), onSuccess: refresh } as unknown as UseMutationOptions)
  return {
    backendReady: true as boolean,
    filters,
    categories: computed<BusinessConsoleProductCategoryItem[]>(() => unwrapItems(listQuery.data.value)),
    categoriesError: listQuery.error,
    categoriesPending: listQuery.isLoading,
    categoriesTotal: computed(() => unwrapTotal(listQuery.data.value)),
    refresh,
    createCategory: (body: BusinessConsoleCreateProductCategoryRequest) => createMutation.mutateAsync({ body }),
    createPending: createMutation.isLoading,
    updateCategory: (categoryCode: string, body: BusinessConsoleUpdateProductCategoryRequest) =>
      asVars(updateMutation.mutateAsync)({
        path: { categoryCode },
        query: { organizationId: filters.organizationId, environmentId: filters.environmentId },
        body,
      }),
    updatePending: updateMutation.isLoading,
    archiveCategory: (categoryCode: string, reason: string) =>
      asVars(archiveMutation.mutateAsync)({
        path: { categoryCode },
        query: { organizationId: filters.organizationId, environmentId: filters.environmentId },
        body: { reason },
      }),
    archivePending: archiveMutation.isLoading,
  }
}

// ── 质量原因（分组目录）──────────────────────────────────────────
export function useQualityReasonCodes() {
  const context = useBusinessContextStore()
  const filters = bindBusinessContext(reactive<PromotedListFilters>({
    organizationId: context.organizationId,
    environmentId: context.environmentId,
    enabled: undefined,
    search: undefined,
    groupName: undefined,
    skip: 0,
    take: DEFAULT_TAKE,
  }))
  const listQuery = useQuery(() =>
    withBusinessContextEnabled(listBusinessConsoleQualityReasonCodesQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        ...optionalQuery('enabled', filters.enabled),
        ...optionalQuery('search', filters.search),
        ...optionalQuery('groupName', filters.groupName),
        skip: filters.skip,
        take: filters.take,
      },
    }), filters),
  )
  const refresh = () => refetchWithBusinessContext(filters, listQuery)
  const createMutation = useMutation({ ...createBusinessConsoleQualityReasonCodeMutationOptions(), onSuccess: refresh })
  const updateMutation = useMutation({ ...updateBusinessConsoleQualityReasonCodeMutationOptions(), onSuccess: refresh } as unknown as UseMutationOptions)
  const archiveMutation = useMutation({ ...archiveBusinessConsoleQualityReasonCodeMutationOptions(), onSuccess: refresh } as unknown as UseMutationOptions)
  return {
    backendReady: true as boolean,
    filters,
    reasons: computed<BusinessConsoleQualityReasonItem[]>(() => unwrapItems(listQuery.data.value)),
    reasonsError: listQuery.error,
    reasonsPending: listQuery.isLoading,
    reasonsTotal: computed(() => unwrapTotal(listQuery.data.value)),
    refresh,
    createReason: (body: BusinessConsoleCreateQualityReasonRequest) => createMutation.mutateAsync({ body }),
    createPending: createMutation.isLoading,
    updateReason: (reasonCode: string, body: BusinessConsoleUpdateQualityReasonRequest) =>
      asVars(updateMutation.mutateAsync)({
        path: { reasonCode },
        query: { organizationId: filters.organizationId, environmentId: filters.environmentId },
        body,
      }),
    updatePending: updateMutation.isLoading,
    archiveReason: (reasonCode: string) =>
      asVars(archiveMutation.mutateAsync)({
        path: { reasonCode },
        query: { organizationId: filters.organizationId, environmentId: filters.environmentId },
      }),
    archivePending: archiveMutation.isLoading,
  }
}

// ── 技能（分组目录）──────────────────────────────────────────────
export function useSkillCatalog() {
  const context = useBusinessContextStore()
  const filters = bindBusinessContext(reactive<PromotedListFilters>({
    organizationId: context.organizationId,
    environmentId: context.environmentId,
    enabled: undefined,
    search: undefined,
    groupName: undefined,
    skip: 0,
    take: DEFAULT_TAKE,
  }))
  const listQuery = useQuery(() =>
    withBusinessContextEnabled(listBusinessConsoleSkillsQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        ...optionalQuery('enabled', filters.enabled),
        ...optionalQuery('search', filters.search),
        ...optionalQuery('groupName', filters.groupName),
        skip: filters.skip,
        take: filters.take,
      },
    }), filters),
  )
  const refresh = () => refetchWithBusinessContext(filters, listQuery)
  const createMutation = useMutation({ ...createBusinessConsoleSkillMutationOptions(), onSuccess: refresh })
  const updateMutation = useMutation({ ...updateBusinessConsoleSkillMutationOptions(), onSuccess: refresh } as unknown as UseMutationOptions)
  const archiveMutation = useMutation({ ...archiveBusinessConsoleSkillMutationOptions(), onSuccess: refresh } as unknown as UseMutationOptions)
  return {
    backendReady: true as boolean,
    filters,
    skills: computed<BusinessConsoleSkillItem[]>(() => unwrapItems(listQuery.data.value)),
    skillsError: listQuery.error,
    skillsPending: listQuery.isLoading,
    skillsTotal: computed(() => unwrapTotal(listQuery.data.value)),
    refresh,
    createSkill: (body: BusinessConsoleCreateSkillRequest) => createMutation.mutateAsync({ body }),
    createPending: createMutation.isLoading,
    updateSkill: (skillCode: string, body: BusinessConsoleUpdateSkillRequest) =>
      asVars(updateMutation.mutateAsync)({
        path: { skillCode },
        query: { organizationId: filters.organizationId, environmentId: filters.environmentId },
        body,
      }),
    updatePending: updateMutation.isLoading,
    archiveSkill: (skillCode: string, reason: string) =>
      asVars(archiveMutation.mutateAsync)({
        path: { skillCode },
        query: { organizationId: filters.organizationId, environmentId: filters.environmentId },
        body: { reason },
      }),
    archivePending: archiveMutation.isLoading,
  }
}
