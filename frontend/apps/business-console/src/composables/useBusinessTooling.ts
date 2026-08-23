import {
  changeBusinessConsoleToolingStatusMutationOptions,
  listBusinessConsoleToolingAssetsQueryOptions,
  recordBusinessConsoleToolingUsageMutationOptions,
  registerBusinessConsoleToolingAssetMutationOptions,
  type BusinessConsoleChangeToolingStatusRequest,
  type BusinessConsoleRecordToolingUsageRequest,
  type BusinessConsoleRegisterToolingAssetRequest,
  type BusinessConsoleToolingAssetItem,
  type BusinessConsoleToolingAssetStatus,
} from '@nerv-iip/api-client'
import { useMutation, useQuery, useQueryCache, type UseQueryEntry } from '@pinia/colada'
import { computed, reactive } from 'vue'
import {
  bindBusinessContext,
  refetchWithBusinessContext,
  withBusinessContextEnabled,
  type BusinessContextFields,
} from './businessContextBinding'

export interface ToolingFilters extends BusinessContextFields {
  keyword?: string
  status?: BusinessConsoleToolingAssetStatus
  skip: number
  take: number
}

type RegisterToolingInput = Omit<
  BusinessConsoleRegisterToolingAssetRequest,
  'organizationId' | 'environmentId'
>

function optionalQuery<TKey extends string, TValue>(key: TKey, value: TValue | undefined) {
  return value === undefined || (typeof value === 'string' && value.trim().length === 0)
    ? {}
    : { [key]: value }
}

function isToolingQuery(entry: UseQueryEntry) {
  const parts = Array.isArray(entry.key) ? entry.key : [entry.key]
  return parts.some(
    (part) =>
      typeof part === 'object' &&
      part !== null &&
      '_id' in part &&
      part._id === 'listBusinessConsoleToolingAssets',
  )
}

export function toolingStatusLabel(value: string | null | undefined) {
  return (
    { available: '可用', maintenance: '保养中', retired: '已退役' }[value ?? ''] ?? value ?? '未知'
  )
}

export function toolingTypeLabel(value: string | null | undefined) {
  return (
    {
      mould: '模具',
      fixture: '夹具',
      jig: '工装夹具',
      cutting: '刀具',
      gauge: '检具',
    }[value ?? ''] ??
    value ??
    '未分类'
  )
}

export function useBusinessTooling() {
  const filters = bindBusinessContext(
    reactive<ToolingFilters>({
      organizationId: '',
      environmentId: '',
      skip: 0,
      take: 10,
    }),
  )
  const queryCache = useQueryCache()
  const invalidate = () => queryCache.invalidateQueries({ predicate: isToolingQuery })

  const toolingQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsoleToolingAssetsQueryOptions({
        query: {
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
          ...optionalQuery('keyword', filters.keyword?.trim()),
          ...optionalQuery('status', filters.status),
          skip: filters.skip,
          take: filters.take,
        },
      }),
      filters,
    ),
  )
  const registerMutation = useMutation({
    ...registerBusinessConsoleToolingAssetMutationOptions(),
    onSuccess: invalidate,
  })
  const statusMutation = useMutation({
    ...changeBusinessConsoleToolingStatusMutationOptions(),
    onSuccess: invalidate,
  })
  const usageMutation = useMutation({
    ...recordBusinessConsoleToolingUsageMutationOptions(),
    onSuccess: invalidate,
  })

  const response = computed(() =>
    toolingQuery.data.value?.success ? toolingQuery.data.value.data : undefined,
  )

  return {
    filters,
    toolingAssets: computed<BusinessConsoleToolingAssetItem[]>(() => response.value?.items ?? []),
    toolingTotal: computed(() => response.value?.total ?? 0),
    toolingPending: toolingQuery.isLoading,
    toolingError: toolingQuery.error,
    refresh: () => refetchWithBusinessContext(filters, toolingQuery),
    register: (body: RegisterToolingInput) =>
      registerMutation.mutateAsync({
        body: {
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
          ...body,
        },
      }),
    registerPending: registerMutation.isLoading,
    changeStatus: (code: string, status: BusinessConsoleToolingAssetStatus, reason: string) =>
      statusMutation.mutateAsync({
        body: {
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
          code,
          status,
          reason,
        } satisfies BusinessConsoleChangeToolingStatusRequest,
      }),
    changeStatusPending: statusMutation.isLoading,
    recordUsage: (code: string, count: number) =>
      usageMutation.mutateAsync({
        body: {
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
          code,
          count,
        } satisfies BusinessConsoleRecordToolingUsageRequest,
      }),
    recordUsagePending: usageMutation.isLoading,
  }
}
