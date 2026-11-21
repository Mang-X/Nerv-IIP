import {
  type BusinessConsoleCreateOrUpdateTelemetryAlarmRuleRequest,
  type BusinessConsoleTelemetryOeeAggregateDimension,
} from '@nerv-iip/api-client'
import { reactive } from 'vue'
import { useBusinessContextStore } from '@/stores/businessContext'

const DEFAULT_TAKE = 100

export const EQUIPMENT_HEALTH_POLL_INTERVAL_MS = 5_000

export type TelemetryEnabledFilter = 'all' | 'enabled' | 'disabled'

export interface TelemetryListFilters {
  deviceAssetId: string
  isEnabled: TelemetryEnabledFilter
  skip: number
  take: number
}

export interface TelemetryWindowFilters {
  deviceAssetId: string
  tagKey: string
  windowStartUtc: string
  windowEndUtc: string
}

export interface TelemetryOeeAggregateFilters {
  dimension: BusinessConsoleTelemetryOeeAggregateDimension
  windowStartUtc: string
  windowEndUtc: string
  deviceAssetId: string
  workCenterId: string
  shiftCode: string
  lineCode: string
  workshopCode: string
  businessDate: string
  skip: number
  take: number
}

export type SaveTelemetryAlarmRuleInput = Omit<
  BusinessConsoleCreateOrUpdateTelemetryAlarmRuleRequest,
  'organizationId' | 'environmentId'
>

export function defaultListFilters(
  initial: Partial<TelemetryListFilters> = {},
): TelemetryListFilters {
  return reactive({
    deviceAssetId: '',
    isEnabled: 'all',
    skip: 0,
    take: DEFAULT_TAKE,
    ...initial,
  })
}

export function defaultWindowFilters(
  initial: Partial<TelemetryWindowFilters> = {},
): TelemetryWindowFilters {
  const end = new Date()
  const start = new Date(end)
  start.setHours(start.getHours() - 8)

  return reactive({
    deviceAssetId: '',
    tagKey: '',
    windowStartUtc: start.toISOString(),
    windowEndUtc: end.toISOString(),
    ...initial,
  })
}

export function trimOptional(value: string | null | undefined) {
  const normalized = value?.trim() ?? ''
  return normalized.length > 0 ? normalized : undefined
}

export function enabledQuery(value: TelemetryEnabledFilter) {
  if (value === 'enabled') return true
  if (value === 'disabled') return false
  return undefined
}

export function toContextQuery(businessContext: ReturnType<typeof useBusinessContextStore>) {
  return {
    organizationId: businessContext.organizationId,
    environmentId: businessContext.environmentId,
  }
}

export function listItems<TItem>(
  envelope: { success?: boolean; data?: { items?: TItem[] } | null } | undefined,
) {
  return envelope?.success ? (envelope.data?.items ?? []) : []
}

export function listTotal(
  envelope: { success?: boolean; data?: { total?: number } | null } | undefined,
) {
  return envelope?.success ? (envelope.data?.total ?? 0) : 0
}

export function unwrapData<TData>(
  envelope: { success?: boolean; data?: TData | null } | undefined,
) {
  return envelope?.success ? (envelope.data ?? undefined) : undefined
}
