import { type BusinessConsoleRecordProductionReportRequest } from '@nerv-iip/api-client'
import { useQueryCache, type UseQueryEntry } from '@pinia/colada'
import { reactive, watchEffect } from 'vue'
import { useAuthStore } from '@/stores/auth'

export const DEFAULT_TAKE = 100

export const MES_OPERATIONS_READ_PERMISSION = 'business.mes.operations.read'

export const MES_OPERATIONS_MANAGE_PERMISSION = 'business.mes.operations.manage'

export const MES_REPORTING_READ_PERMISSION = 'business.mes.reporting.read'

export const MES_REPORTING_WRITE_PERMISSION = 'business.mes.reporting.write'

export const MES_WORK_ORDERS_READ_PERMISSION = 'business.mes.work-orders.read'

export const MES_WORK_SCOPE_REQUIRED_MESSAGE =
  '尚未选择已授权作业范围，当前操作已禁用。请在页面顶部的「作业范围」中选择后继续。'

export const MES_WORK_SCOPE_UNAVAILABLE_MESSAGE =
  '当前账号在本组织没有已授权的作业范围，无法读取现场数据。' +
  '请联系管理员为该账号配置数据范围（组织/车间/工作中心/班组/本人）后重新登录。'

export interface MesListFilters {
  organizationId: string
  environmentId: string
  status?: string
  keyword?: string
  operationTaskId?: string
  workOrderId?: string
  workCenterId?: string
  deviceAssetId?: string
  skip: number
  take: number
}

export type MesScope = Pick<MesListFilters, 'organizationId' | 'environmentId'>

/**
 * PDA has no business-context store — org/env come from the logged-in principal.
 * Keeps the reactive `filters` scope synced so an empty principal yields an empty
 * scope and disables list queries.
 */
export function bindAuthScope<TFilters extends MesScope>(filters: TFilters): TFilters {
  const auth = useAuthStore()
  watchEffect(() => {
    filters.organizationId = auth.principal?.organizationId ?? ''
    filters.environmentId = auth.principal?.environmentId ?? ''
  })
  return filters
}

export function defaultFilters(): MesListFilters {
  return bindAuthScope(
    reactive({
      organizationId: '',
      environmentId: '',
      skip: 0,
      take: DEFAULT_TAKE,
    }),
  )
}

export function optionalQuery<TKey extends string, TValue>(key: TKey, value: TValue | undefined) {
  return value === undefined || value === '' ? {} : { [key]: value }
}

export function toListQuery(filters: MesListFilters) {
  return {
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
    ...optionalQuery('status', filters.status),
    ...optionalQuery('keyword', filters.keyword),
    ...optionalQuery('operationTaskId', filters.operationTaskId),
    ...optionalQuery('workOrderId', filters.workOrderId),
    ...optionalQuery('workCenterId', filters.workCenterId),
    ...optionalQuery('deviceAssetId', filters.deviceAssetId),
    skip: filters.skip,
    take: filters.take,
  }
}

export function hasScope(filters: MesScope) {
  return Boolean(filters.organizationId && filters.environmentId)
}

export function scopeQuery(filters: MesScope) {
  return {
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
  }
}

export interface MesSelectedWorkScope {
  kind: string
  id: string
  displayName?: string
}

export interface MesReportExecutionContext {
  principalId: string
  organizationId: string
  environmentId: string
  scopeKind: string
  scopeId: string
  generation: number
}

export function hasSameReportExecutionContext(
  current: MesReportExecutionContext,
  frozen: MesReportExecutionContext,
) {
  return (
    current.principalId === frozen.principalId &&
    current.organizationId === frozen.organizationId &&
    current.environmentId === frozen.environmentId &&
    current.scopeKind === frozen.scopeKind &&
    current.scopeId === frozen.scopeId &&
    current.generation === frozen.generation
  )
}

export function scopeKey(filters: MesScope) {
  return `${filters.organizationId.trim()}:${filters.environmentId.trim()}`
}

export function envelopeItems<
  TItem,
  TEnvelope extends { success?: boolean; data?: { items?: TItem[] } | null },
>(envelope: TEnvelope | undefined) {
  if (!envelope?.success) {
    return []
  }
  return envelope.data?.items ?? []
}

export function envelopeTotal<
  TEnvelope extends { success?: boolean; data?: { total?: number } | null },
>(envelope: TEnvelope | undefined) {
  if (!envelope?.success) {
    return 0
  }
  return envelope.data?.total ?? 0
}

export function envelopeData<TData, TEnvelope extends { success?: boolean; data?: TData | null }>(
  envelope: TEnvelope | undefined,
) {
  if (!envelope?.success) {
    return undefined
  }
  return envelope.data ?? undefined
}

export function exactItem<TItem>(
  envelope: { success?: boolean; data?: { items?: TItem[] } | null } | undefined,
  matches: (item: TItem) => boolean,
) {
  const matchesById = (envelope?.success ? (envelope.data?.items ?? []) : []).filter(matches)
  return matchesById.length === 1 ? matchesById[0] : undefined
}

export function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

export function isNonBlankString(value: unknown): value is string {
  return typeof value === 'string' && value.trim() === value && value.length > 0
}

export function isBusinessQuery(id: string) {
  return (entry: UseQueryEntry) => {
    const keyParts = Array.isArray(entry.key) ? entry.key : [entry.key]
    return keyParts.some((part) => {
      return typeof part === 'object' && part !== null && '_id' in part && part._id === id
    })
  }
}

export function ignoreBackgroundError(_error: unknown) {}

export function invalidateMesQueries(queryCache: ReturnType<typeof useQueryCache>, ids: string[]) {
  return Promise.all(
    ids.map((id) =>
      queryCache.invalidateQueries({
        predicate: isBusinessQuery(id),
      }),
    ),
  )
}

export type RecordReportInput = Omit<
  BusinessConsoleRecordProductionReportRequest,
  'organizationId' | 'environmentId' | 'reportedAtUtc' | 'scopeKind' | 'scopeId'
>
