import type { BusinessConsoleTelemetryHistoryItem } from '@nerv-iip/api-client'
import { describe, expect, it } from 'vitest'

import { projectTelemetryHistory } from '@/components/equipment/telemetryHistoryPresentation'

const items = [
  {
    itemType: 'sample',
    deviceAssetId: 'DEV-CNC-01',
    tagKey: 'temperature',
    value: '87.5',
    occurredAtUtc: '2026-07-02T07:30:00.000Z',
  },
  {
    itemType: 'state',
    deviceAssetId: 'DEV-CNC-01',
    tagKey: 'temperature',
    value: 'running',
    occurredAtUtc: '2026-07-02T07:15:00.000Z',
  },
  {
    itemType: 'alarm',
    deviceAssetId: 'DEV-CNC-01',
    tagKey: 'temperature',
    value: 'TEMP_HIGH',
    occurredAtUtc: '2026-07-02T07:20:00.000Z',
  },
  {
    itemType: 'hourly',
    deviceAssetId: 'DEV-CNC-01',
    tagKey: 'temperature',
    value: '82.25',
    occurredAtUtc: '2026-07-02T06:30:00.000Z',
  },
] satisfies BusinessConsoleTelemetryHistoryItem[]

describe('telemetry history presentation', () => {
  it('uses raw samples without mixing overlapping hourly rollups into the chart or statistics', () => {
    const result = projectTelemetryHistory(items)

    expect(result.chartData).toEqual([
      { occurredAt: '2026-07-02T07:30:00.000Z', time: '07/02 15:30', value: 87.5 },
    ])
    expect(result.statistics).toEqual({
      basis: 'sample',
      count: 1,
      latest: 87.5,
      maximum: 87.5,
      minimum: 87.5,
      lastSampleAtUtc: '2026-07-02T07:30:00.000Z',
    })
    expect(result.excludedAggregateCount).toBe(1)
  })

  it('falls back to one aggregate grain when retained raw samples are unavailable', () => {
    const result = projectTelemetryHistory([
      { itemType: 'daily', value: '70', occurredAtUtc: '2026-07-01T00:00:00Z' },
      { itemType: 'hourly', value: '80', occurredAtUtc: '2026-07-02T01:00:00Z' },
      { itemType: 'hourly', value: '82', occurredAtUtc: '2026-07-02T02:00:00Z' },
    ])

    expect(result.chartData.map((point) => point.value)).toEqual([80, 82])
    expect(result.statistics).toEqual(
      expect.objectContaining({ basis: 'hourly', count: 2, latest: 82, minimum: 80, maximum: 82 }),
    )
    expect(result.excludedAggregateCount).toBe(1)
  })

  it('keeps state and alarm records in an explicitly labelled event timeline', () => {
    const result = projectTelemetryHistory(items)

    expect(result.timelineItems).toEqual([
      expect.objectContaining({
        title: '报警记录',
        description: 'temperature：TEMP_HIGH',
        tone: 'danger',
      }),
      expect.objectContaining({
        title: '状态记录',
        description: 'temperature：running',
        tone: 'neutral',
      }),
    ])
  })

  it('does not coerce blank or non-numeric sample values to zero', () => {
    const result = projectTelemetryHistory([
      { itemType: 'sample', value: '', occurredAtUtc: '2026-07-02T01:00:00Z' },
      { itemType: 'sample', value: 'running', occurredAtUtc: '2026-07-02T02:00:00Z' },
      { itemType: 'sample', value: 'Infinity', occurredAtUtc: '2026-07-02T03:00:00Z' },
    ])

    expect(result.chartData).toEqual([])
    expect(result.statistics).toBeUndefined()
    expect(result.nonNumericMeasurementCount).toBe(3)
    expect(result.timelineItems).toEqual([
      expect.objectContaining({ title: '状态值', description: '设备状态：Infinity' }),
      expect.objectContaining({ title: '状态值', description: '设备状态：running' }),
    ])
  })

  it('excludes numeric rows without a valid timestamp from chart order and latest statistics', () => {
    const result = projectTelemetryHistory([
      { itemType: 'sample', value: '99', occurredAtUtc: 'not-a-time' },
      { itemType: 'sample', value: '42', occurredAtUtc: '2026-07-02T02:00:00Z' },
    ])

    expect(result.chartData.map((point) => point.value)).toEqual([42])
    expect(result.statistics).toEqual(expect.objectContaining({ latest: 42, count: 1 }))
    expect(result.invalidTimestampCount).toBe(1)
  })
})
