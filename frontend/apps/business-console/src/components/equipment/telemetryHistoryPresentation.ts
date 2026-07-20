import type { BusinessConsoleTelemetryHistoryItem } from '@nerv-iip/api-client'
import type { TimelineItem } from '@nerv-iip/ui'

const MEASUREMENT_TYPES = new Set(['sample', 'hourly', 'daily'])
const EVENT_TYPES = new Set(['alarm', 'state'])
const SERIES_PRECEDENCE = ['sample', 'hourly', 'daily'] as const

export type TelemetrySeriesBasis = (typeof SERIES_PRECEDENCE)[number]

export interface TelemetryChartPoint {
  [key: string]: number | string
  occurredAt: string
  time: string
  value: number
}

export interface TelemetryStatistics {
  basis: TelemetrySeriesBasis
  count: number
  latest: number
  maximum: number
  minimum: number
  lastSampleAtUtc: string
}

export interface TelemetryHistoryProjection {
  chartData: TelemetryChartPoint[]
  excludedAggregateCount: number
  invalidTimestampCount: number
  nonNumericMeasurementCount: number
  statistics?: TelemetryStatistics
  timelineItems: TimelineItem[]
}

interface NumericMeasurement {
  item: BusinessConsoleTelemetryHistoryItem
  itemType: TelemetrySeriesBasis
  occurredAt: number
  value: number
}

export function projectTelemetryHistory(
  items: BusinessConsoleTelemetryHistoryItem[],
): TelemetryHistoryProjection {
  const measurements = items.filter((item) =>
    MEASUREMENT_TYPES.has(item.itemType?.toLowerCase() ?? ''),
  )
  const numericMeasurements = measurements
    .map((item) => ({
      item,
      itemType: item.itemType?.toLowerCase() as TelemetrySeriesBasis,
      occurredAt: parseTimestamp(item.occurredAtUtc),
      value: finiteNumber(item.value),
    }))
    .filter(
      (entry): entry is NumericMeasurement =>
        entry.occurredAt !== undefined && entry.value !== undefined,
    )
  const basis = SERIES_PRECEDENCE.find((candidate) =>
    numericMeasurements.some((entry) => entry.itemType === candidate),
  )
  const selectedMeasurements = basis
    ? numericMeasurements
        .filter((entry) => entry.itemType === basis)
        .sort((left, right) => left.occurredAt - right.occurredAt)
    : []
  const chartData = selectedMeasurements.map(({ item, value }) => ({
    occurredAt: item.occurredAtUtc ?? '',
    time: shortDateTime(item.occurredAtUtc),
    value,
  }))

  const statistics = basis
    ? {
        basis,
        count: chartData.length,
        latest: chartData.at(-1)!.value,
        maximum: Math.max(...chartData.map((point) => point.value)),
        minimum: Math.min(...chartData.map((point) => point.value)),
        lastSampleAtUtc: chartData.at(-1)!.occurredAt,
      }
    : undefined

  const timelineItems = items
    .filter((item) => {
      const itemType = item.itemType?.toLowerCase() ?? ''
      return (
        EVENT_TYPES.has(itemType) ||
        (itemType === 'sample' && finiteNumber(item.value) === undefined && item.value?.trim())
      )
    })
    .sort(
      (left, right) =>
        (parseTimestamp(right.occurredAtUtc) ?? Number.NEGATIVE_INFINITY) -
        (parseTimestamp(left.occurredAtUtc) ?? Number.NEGATIVE_INFINITY),
    )
    .map<TimelineItem>((item, index) => {
      const itemType = item.itemType?.toLowerCase()
      const isAlarm = itemType === 'alarm'
      const isStateValue = itemType === 'sample'
      return {
        key: `${item.itemType}-${item.occurredAtUtc ?? index}-${index}`,
        title: isAlarm ? '报警记录' : isStateValue ? '状态值' : '状态记录',
        label: formatTelemetryDateTime(item.occurredAtUtc),
        description: `${item.tagKey?.trim() || '设备状态'}：${item.value?.trim() || '无值'}`,
        tone: isAlarm ? 'danger' : 'neutral',
        dotType: isAlarm ? 'solid' : 'hollow',
      }
    })

  return {
    chartData,
    excludedAggregateCount: basis
      ? numericMeasurements.filter(
          (entry) => entry.itemType !== basis && entry.itemType !== 'sample',
        ).length
      : 0,
    invalidTimestampCount: measurements.filter(
      (item) =>
        finiteNumber(item.value) !== undefined && parseTimestamp(item.occurredAtUtc) === undefined,
    ).length,
    nonNumericMeasurementCount: measurements.filter(
      (item) => finiteNumber(item.value) === undefined,
    ).length,
    statistics,
    timelineItems,
  }
}

function finiteNumber(value?: string | null) {
  const normalized = value?.trim() ?? ''
  if (!normalized) return undefined
  const parsed = Number(normalized)
  return Number.isFinite(parsed) ? parsed : undefined
}

function parseTimestamp(value?: string | null) {
  const parsed = value ? new Date(value).getTime() : Number.NaN
  return Number.isNaN(parsed) ? undefined : parsed
}

function shortDateTime(value?: string | null) {
  if (!value) return '无时间'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  const parts = new Intl.DateTimeFormat('zh-CN', {
    day: '2-digit',
    hour: '2-digit',
    hour12: false,
    minute: '2-digit',
    month: '2-digit',
  }).formatToParts(date)
  const part = (type: Intl.DateTimeFormatPartTypes) =>
    parts.find((candidate) => candidate.type === type)?.value ?? ''
  return `${part('month')}/${part('day')} ${part('hour')}:${part('minute')}`
}

export function formatTelemetryDateTime(value?: string | null) {
  if (!value) return '无时间'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString('zh-CN')
}
