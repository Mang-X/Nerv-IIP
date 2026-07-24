import type { BusinessConsoleOrderUrgency } from '@nerv-iip/api-client'
import { describe, expect, it } from 'vitest'
import {
  URGENCY_DISPLAY_MODES,
  compareOrderUrgency,
  formatUrgencyDisplay,
  orderRowsByUrgency,
  type UrgencyDisplayMode,
} from './useUrgencyDisplayMode'

const urgency: BusinessConsoleOrderUrgency = {
  orderId: 'WO-1',
  businessReference: 'SO-1',
  level: 'urgent',
  businessPriority: { level: 'p1' },
  timeCriticality: {
    level: 'highrisk',
    criticalRatio: 0.8,
    slackHours: -2,
    expectedDelayHours: 5,
  },
  executionRisk: { level: 'attention' },
}

describe('URGENCY_DISPLAY_MODES', () => {
  it('exposes exactly the seven required display modes', () => {
    expect(URGENCY_DISPLAY_MODES.map((mode) => mode.value)).toEqual([
      'level',
      'businessPriority',
      'dynamicUrgency',
      'executionRisk',
      'criticalRatio',
      'slack',
      'expectedDelay',
    ])
  })
})

describe('formatUrgencyDisplay', () => {
  const cases: Array<[UrgencyDisplayMode, string]> = [
    ['level', '紧急'],
    ['businessPriority', 'P1'],
    ['dynamicUrgency', '高风险'],
    ['executionRisk', '关注'],
    ['criticalRatio', 'CR 0.8'],
    ['slack', 'Slack -2h'],
    ['expectedDelay', '延误 5h'],
  ]

  it.each(cases)('renders the %s mode label without recomputing the result', (mode, label) => {
    const display = formatUrgencyDisplay(urgency, mode)
    expect(display.label).toBe(label)
    // Tone is always derived from the unified level, so switching mode never
    // changes the color semantics — only the label.
    expect(display.tone).toBe('danger')
  })

  it('falls back to 未计算 when there is no urgency result', () => {
    for (const mode of URGENCY_DISPLAY_MODES) {
      const display = formatUrgencyDisplay(undefined, mode.value)
      expect(display).toEqual({ label: '未计算', tone: 'neutral' })
    }
  })

  it('defaults business priority to P2 when unset', () => {
    expect(formatUrgencyDisplay({ level: 'normal' }, 'businessPriority').label).toBe('P2')
  })
})

function make(level: string, extra: Partial<BusinessConsoleOrderUrgency> = {}) {
  return { level, ...extra } as BusinessConsoleOrderUrgency
}

describe('compareOrderUrgency', () => {
  it('orders by unified level first (higher rank comes first)', () => {
    expect(compareOrderUrgency(make('critical'), make('normal'))).toBeLessThan(0)
    expect(compareOrderUrgency(make('normal'), make('critical'))).toBeGreaterThan(0)
  })

  it('breaks level ties by ascending critical ratio', () => {
    const low = make('urgent', { timeCriticality: { criticalRatio: 0.5 } })
    const high = make('urgent', { timeCriticality: { criticalRatio: 1.5 } })
    expect(compareOrderUrgency(low, high)).toBeLessThan(0)
  })

  it('then breaks by descending expected delay', () => {
    const more = make('urgent', { timeCriticality: { criticalRatio: 1, expectedDelayHours: 8 } })
    const less = make('urgent', { timeCriticality: { criticalRatio: 1, expectedDelayHours: 2 } })
    expect(compareOrderUrgency(more, less)).toBeLessThan(0)
  })

  it('then breaks by earliest due time', () => {
    const early = make('urgent', {
      timeCriticality: { criticalRatio: 1, expectedDelayHours: 1, dueUtc: '2026-07-01T00:00:00Z' },
    })
    const late = make('urgent', {
      timeCriticality: { criticalRatio: 1, expectedDelayHours: 1, dueUtc: '2026-07-05T00:00:00Z' },
    })
    expect(compareOrderUrgency(early, late)).toBeLessThan(0)
  })

  it('sorts rows without an urgency result last', () => {
    expect(compareOrderUrgency(make('normal'), undefined)).toBeLessThan(0)
    expect(compareOrderUrgency(undefined, make('normal'))).toBeGreaterThan(0)
    expect(compareOrderUrgency(undefined, undefined)).toBe(0)
  })
})

describe('orderRowsByUrgency', () => {
  it('reorders host rows by urgency and keeps unmatched rows last, stably', () => {
    const rows = [
      { ref: 'A' },
      { ref: 'B' },
      { ref: 'C' },
      { ref: 'D' }, // no urgency mapping
    ]
    const byReference = new Map<string, BusinessConsoleOrderUrgency>([
      ['A', make('normal')],
      ['B', make('critical')],
      ['C', make('urgent')],
    ])

    const ordered = orderRowsByUrgency(rows, (row) => row.ref, byReference)
    expect(ordered.map((row) => row.ref)).toEqual(['B', 'C', 'A', 'D'])
  })
})
