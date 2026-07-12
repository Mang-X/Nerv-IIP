import { describe, expect, it } from 'vitest'
import {
  createMeasurementDraft,
  hasMeasurementInput,
  isMeasurementRowValid,
  measurementOutOfTolerance,
  measurementRowsValid,
  parseOptionalNumber,
  parseRequiredNumber,
  toMeasurementPayload,
  type MeasurementDraftRow,
} from './measurements'

function row(overrides: Partial<MeasurementDraftRow> = {}): MeasurementDraftRow {
  return { ...createMeasurementDraft(), ...overrides }
}

describe('measurement number parsing', () => {
  it('treats blank optional as valid null and blank required as invalid', () => {
    expect(parseOptionalNumber('')).toEqual({ valid: true, value: null })
    expect(parseOptionalNumber('  ')).toEqual({ valid: true, value: null })
    expect(parseRequiredNumber('')).toEqual({ valid: false, value: null })
  })

  it('rejects non-numeric and accepts finite numbers', () => {
    expect(parseOptionalNumber('abc').valid).toBe(false)
    expect(parseRequiredNumber('12.5')).toEqual({ valid: true, value: 12.5 })
    expect(parseRequiredNumber('-3')).toEqual({ valid: true, value: -3 })
  })
})

describe('hasMeasurementInput', () => {
  it('is false for a pristine row and true once any field is filled', () => {
    expect(hasMeasurementInput(row())).toBe(false)
    expect(hasMeasurementInput(row({ uomCode: 'C' }))).toBe(true)
    expect(hasMeasurementInput(row({ measuredValue: 0 }))).toBe(true)
  })
})

describe('isMeasurementRowValid', () => {
  it('requires characteristic, measured value and uom', () => {
    expect(isMeasurementRowValid(row({ characteristicCode: 'temp', measuredValue: '65', uomCode: 'C' }))).toBe(true)
    expect(isMeasurementRowValid(row({ measuredValue: '65', uomCode: 'C' }))).toBe(false)
    expect(isMeasurementRowValid(row({ characteristicCode: 'temp', uomCode: 'C' }))).toBe(false)
    expect(isMeasurementRowValid(row({ characteristicCode: 'temp', measuredValue: '65' }))).toBe(false)
  })

  it('rejects lower limit greater than upper limit', () => {
    expect(
      isMeasurementRowValid(
        row({ characteristicCode: 'temp', measuredValue: '65', uomCode: 'C', lowerSpecLimit: '70', upperSpecLimit: '10' }),
      ),
    ).toBe(false)
    expect(
      isMeasurementRowValid(
        row({ characteristicCode: 'temp', measuredValue: '65', uomCode: 'C', lowerSpecLimit: '0', upperSpecLimit: '70' }),
      ),
    ).toBe(true)
  })
})

describe('measurementRowsValid', () => {
  it('ignores pristine rows but fails a partially-filled invalid row', () => {
    expect(measurementRowsValid([row(), row()])).toBe(true)
    expect(measurementRowsValid([row({ characteristicCode: 'temp' })])).toBe(false)
  })
})

describe('measurementOutOfTolerance', () => {
  it('flags a value below lower or above upper spec limit', () => {
    expect(measurementOutOfTolerance(row({ measuredValue: '75', lowerSpecLimit: '0', upperSpecLimit: '70' }))).toBe(true)
    expect(measurementOutOfTolerance(row({ measuredValue: '-1', lowerSpecLimit: '0', upperSpecLimit: '70' }))).toBe(true)
  })

  it('is false within limits, with no limits, or when the value is not yet a number', () => {
    expect(measurementOutOfTolerance(row({ measuredValue: '65', lowerSpecLimit: '0', upperSpecLimit: '70' }))).toBe(false)
    expect(measurementOutOfTolerance(row({ measuredValue: '65' }))).toBe(false)
    expect(measurementOutOfTolerance(row({ measuredValue: '', lowerSpecLimit: '0', upperSpecLimit: '70' }))).toBe(false)
    expect(measurementOutOfTolerance(row({ measuredValue: 'abc', lowerSpecLimit: '0', upperSpecLimit: '70' }))).toBe(false)
  })

  it('respects a single-sided limit', () => {
    expect(measurementOutOfTolerance(row({ measuredValue: '75', upperSpecLimit: '70' }))).toBe(true)
    expect(measurementOutOfTolerance(row({ measuredValue: '75', lowerSpecLimit: '0' }))).toBe(false)
  })
})

describe('toMeasurementPayload', () => {
  it('drops pristine rows and normalizes numbers with null limits', () => {
    const payload = toMeasurementPayload([
      row({ characteristicCode: 'temp', measuredValue: '65', uomCode: 'C', lowerSpecLimit: '0', upperSpecLimit: '70' }),
      row(),
      row({ characteristicCode: 'vibration', measuredValue: '2.1', uomCode: 'mm/s' }),
    ])
    expect(payload).toEqual([
      { characteristicCode: 'temp', measuredValue: 65, uomCode: 'C', lowerSpecLimit: 0, upperSpecLimit: 70 },
      { characteristicCode: 'vibration', measuredValue: 2.1, uomCode: 'mm/s', lowerSpecLimit: null, upperSpecLimit: null },
    ])
  })
})
