import { describe, expect, it } from 'vitest'
import { factoryTimeInput, factoryTimeInstant } from './schedulingFactoryTime'

describe('factory time editing (#4448)', () => {
  it('shows Shanghai wall time and preserves the exact no-op instant', () => {
    const original = '2026-10-13T19:25:29.123+08:00'
    expect(factoryTimeInput(original, 'Asia/Shanghai')).toBe('2026-10-13T19:25:29')
    expect(factoryTimeInstant('2026-10-13T19:25:29', 'Asia/Shanghai', original)).toBe(original)
    expect(factoryTimeInstant('2026-10-13T20:25:29', 'Asia/Shanghai', original)).toBe(
      '2026-10-13T12:25:29.123Z',
    )
  })
  it('uses the configured factory rather than the browser timezone', () => {
    expect(factoryTimeInput('2026-10-13T11:25:29Z', 'UTC')).toBe('2026-10-13T11:25:29')
    expect(factoryTimeInstant('2026-10-13T12:25:29', 'UTC', '2026-10-13T11:25:29Z')).toBe(
      '2026-10-13T12:25:29.000Z',
    )
  })
  it('refuses missing/invalid zones and nonexistent or ambiguous wall times', () => {
    expect(factoryTimeInput('2026-10-13T11:25:29Z', '')).toBe('')
    expect(factoryTimeInstant('2026-10-13T12:25:29', 'invalid', '')).toBeUndefined()
    expect(factoryTimeInstant('2026-03-08T02:30:00', 'America/New_York', '')).toBeUndefined()
    expect(factoryTimeInstant('2026-11-01T01:30:00', 'America/New_York', '')).toBeUndefined()
  })
})
