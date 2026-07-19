import type { BusinessConsoleTelemetryHistoryItem } from '@nerv-iip/api-client'
import { describe, expect, it } from 'vitest'

import { projectTelemetryHistory } from './telemetryHistoryPresentation'

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
  it('projects only real numeric measurements into the chart and its statistics', () => {
    const result = projectTelemetryHistory(items)

    expect(result.chartData).toEqual([
      { occurredAt: '2026-07-02T06:30:00.000Z', time: '07/02 14:30', value: 82.25 },
      { occurredAt: '2026-07-02T07:30:00.000Z', time: '07/02 15:30', value: 87.5 },
    ])
    expect(result.statistics).toEqual({
      count: 2,
      latest: 87.5,
      maximum: 87.5,
      minimum: 82.25,
      lastSampleAtUtc: '2026-07-02T07:30:00.000Z',
    })
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
  })
})
