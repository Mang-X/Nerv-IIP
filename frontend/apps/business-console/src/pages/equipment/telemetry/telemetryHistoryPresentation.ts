import type { BusinessConsoleTelemetryHistoryItem } from '@nerv-iip/api-client'
import type { TimelineItem } from '@nerv-iip/ui'

const MEASUREMENT_TYPES = new Set(['sample', 'hourly', 'daily'])
const EVENT_TYPES = new Set(['alarm', 'state'])

export interface TelemetryChartPoint {
  [key: string]: number | string
  occurredAt: string
  time: string
  value: number
}

export interface TelemetryStatistics {
  count: number
  latest: number
  maximum: number
  minimum: number
  lastSampleAtUtc: string
}

export interface TelemetryHistoryProjection {
  chartData: TelemetryChartPoint[]
  nonNumericMeasurementCount: number
  statistics?: TelemetryStatistics
  timelineItems: TimelineItem[]
}

export function projectTelemetryHistory(
  items: BusinessConsoleTelemetryHistoryItem[],
): TelemetryHistoryProjection {
  const numericMeasurements = items
    .filter((item) => MEASUREMENT_TYPES.has(item.itemType?.toLowerCase() ?? ''))
    .map((item) => ({ item, value: finiteNumber(item.value) }))

  const chartData = numericMeasurements
    .filter(
      (entry): entry is { item: BusinessConsoleTelemetryHistoryItem; value: number } =>
        entry.value !== undefined,
    )
    .sort((left, right) => timestamp(left.item.occurredAtUtc) - timestamp(right.item.occurredAtUtc))
    .map(({ item, value }) => ({
      occurredAt: item.occurredAtUtc ?? '',
      time: shortDateTime(item.occurredAtUtc),
      value,
    }))

  const statistics = chartData.length
    ? {
        count: chartData.length,
        latest: chartData.at(-1)!.value,
        maximum: Math.max(...chartData.map((point) => point.value)),
        minimum: Math.min(...chartData.map((point) => point.value)),
        lastSampleAtUtc: chartData.at(-1)!.occurredAt,
      }
    : undefined

  const timelineItems = items
    .filter((item) => EVENT_TYPES.has(item.itemType?.toLowerCase() ?? ''))
    .sort((left, right) => timestamp(right.occurredAtUtc) - timestamp(left.occurredAtUtc))
    .map<TimelineItem>((item, index) => {
      const isAlarm = item.itemType?.toLowerCase() === 'alarm'
      return {
        key: `${item.itemType}-${item.occurredAtUtc ?? index}-${index}`,
        title: isAlarm ? '报警记录' : '状态记录',
        label: formatDateTime(item.occurredAtUtc),
        description: `${item.tagKey?.trim() || '设备状态'}：${item.value?.trim() || '无值'}`,
        tone: isAlarm ? 'danger' : 'neutral',
        dotType: isAlarm ? 'solid' : 'hollow',
      }
    })

  return {
    chartData,
    nonNumericMeasurementCount: numericMeasurements.filter((entry) => entry.value === undefined)
      .length,
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

function timestamp(value?: string | null) {
  const parsed = value ? new Date(value).getTime() : Number.NaN
  return Number.isNaN(parsed) ? Number.POSITIVE_INFINITY : parsed
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

function formatDateTime(value?: string | null) {
  if (!value) return '无时间'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString('zh-CN')
}
