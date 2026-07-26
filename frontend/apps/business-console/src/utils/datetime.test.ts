import { afterAll, beforeAll, describe, expect, it } from 'vitest'

import { toIsoFromLocalInput, toLocalDateTimeInput } from './datetime'

const originalTimezone = process.env.TZ

beforeAll(() => {
  process.env.TZ = 'Asia/Shanghai'
})

afterAll(() => {
  if (originalTimezone === undefined) {
    delete process.env.TZ
  } else {
    process.env.TZ = originalTimezone
  }
})

describe('toIsoFromLocalInput', () => {
  it('converts a datetime-local value from the local timezone to UTC', () => {
    expect(toIsoFromLocalInput('2026-07-01T09:30')).toBe('2026-07-01T01:30:00.000Z')
  })

  it('preserves empty and invalid values for the existing form fallback contract', () => {
    expect(toIsoFromLocalInput('')).toBe('')
    expect(toIsoFromLocalInput('not-a-date')).toBe('not-a-date')
  })

  it('preserves supplied seconds and milliseconds in the ISO result', () => {
    expect(toIsoFromLocalInput('2026-07-01T09:30:45')).toBe('2026-07-01T01:30:45.000Z')
    expect(toIsoFromLocalInput('2026-07-01T09:30:45.678')).toBe('2026-07-01T01:30:45.678Z')
  })
})

describe('toLocalDateTimeInput', () => {
  it('converts UTC strings and Date values to datetime-local minute precision', () => {
    expect(toLocalDateTimeInput('2026-07-01T01:30:45.678Z')).toBe('2026-07-01T09:30')
    expect(toLocalDateTimeInput(new Date('2026-12-31T15:59:59.999Z'))).toBe('2026-12-31T23:59')
  })

  it('returns an empty value for absent or invalid input', () => {
    expect(toLocalDateTimeInput('')).toBe('')
    expect(toLocalDateTimeInput(null)).toBe('')
    expect(toLocalDateTimeInput(undefined)).toBe('')
    expect(toLocalDateTimeInput('not-a-date')).toBe('')
  })

  it('truncates seconds and milliseconds at the datetime-local minute boundary', () => {
    expect(toLocalDateTimeInput('2026-07-01T01:30:00.000Z')).toBe('2026-07-01T09:30')
    expect(toLocalDateTimeInput('2026-07-01T01:30:59.999Z')).toBe('2026-07-01T09:30')
  })
})
