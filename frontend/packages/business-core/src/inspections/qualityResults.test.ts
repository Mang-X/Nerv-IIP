import { describe, expect, it } from 'vitest'
import {
  characteristicRowOutOfTolerance,
  characteristicRowResult,
  createQualityCharacteristicDraft,
  isQualityCharacteristicRowValid,
  qualityCharacteristicRowsValid,
  qualityInspectionOverallVerdict,
  toQualityCharacteristicResultLines,
  type QualityCharacteristicDraftRow,
} from './qualityResults'

function measured(over: Partial<QualityCharacteristicDraftRow> = {}): QualityCharacteristicDraftRow {
  return { ...createQualityCharacteristicDraft('measured'), ...over }
}
function count(over: Partial<QualityCharacteristicDraftRow> = {}): QualityCharacteristicDraftRow {
  return { ...createQualityCharacteristicDraft('count'), ...over }
}

describe('createQualityCharacteristicDraft', () => {
  it('creates a blank row carrying the requested kind', () => {
    expect(createQualityCharacteristicDraft('measured').kind).toBe('measured')
    expect(createQualityCharacteristicDraft('count').kind).toBe('count')
    expect(createQualityCharacteristicDraft('measured').characteristicCode).toBe('')
  })
})

describe('characteristicRowOutOfTolerance (measured)', () => {
  it('flags a value below the lower limit', () => {
    expect(
      characteristicRowOutOfTolerance(
        measured({ characteristicCode: '外径', measuredValue: '9.8', uomCode: 'mm', lowerSpecLimit: '10' }),
      ),
    ).toBe(true)
  })

  it('flags a value above the upper limit', () => {
    expect(
      characteristicRowOutOfTolerance(
        measured({ characteristicCode: '外径', measuredValue: '10.5', uomCode: 'mm', upperSpecLimit: '10' }),
      ),
    ).toBe(true)
  })

  it('is within tolerance when between limits', () => {
    expect(
      characteristicRowOutOfTolerance(
        measured({ measuredValue: '10', lowerSpecLimit: '9', upperSpecLimit: '11' }),
      ),
    ).toBe(false)
  })

  it('never reports a half-filled row (no measured value) as out of tolerance', () => {
    expect(characteristicRowOutOfTolerance(measured({ lowerSpecLimit: '10' }))).toBe(false)
  })

  it('is always false for count rows', () => {
    expect(characteristicRowOutOfTolerance(count({ countResult: 'fail' }))).toBe(false)
  })
})

describe('characteristicRowResult', () => {
  it('derives pass/fail from tolerance for measured rows', () => {
    expect(characteristicRowResult(measured({ measuredValue: '10', upperSpecLimit: '9' }))).toBe('fail')
    expect(characteristicRowResult(measured({ measuredValue: '10', upperSpecLimit: '11' }))).toBe('pass')
  })

  it('returns null for a measured row without a parseable value (undetermined)', () => {
    expect(characteristicRowResult(measured({ measuredValue: '' }))).toBeNull()
  })

  it('passes a measured value with no limits configured', () => {
    expect(characteristicRowResult(measured({ measuredValue: '42' }))).toBe('pass')
  })

  it('takes the chosen pass/fail for count rows, null when unset', () => {
    expect(characteristicRowResult(count({ countResult: 'pass' }))).toBe('pass')
    expect(characteristicRowResult(count({ countResult: 'fail' }))).toBe('fail')
    expect(characteristicRowResult(count({ countResult: '' }))).toBeNull()
  })
})

describe('isQualityCharacteristicRowValid', () => {
  it('requires a characteristic code', () => {
    expect(isQualityCharacteristicRowValid(measured({ characteristicCode: '', measuredValue: '1', uomCode: 'mm' }))).toBe(false)
  })

  it('measured: needs value + uom; limits optional but lower<=upper when both set', () => {
    expect(isQualityCharacteristicRowValid(measured({ characteristicCode: '外径', measuredValue: '1', uomCode: 'mm' }))).toBe(true)
    expect(isQualityCharacteristicRowValid(measured({ characteristicCode: '外径', measuredValue: '', uomCode: 'mm' }))).toBe(false)
    expect(isQualityCharacteristicRowValid(measured({ characteristicCode: '外径', measuredValue: '1', uomCode: '' }))).toBe(false)
    expect(
      isQualityCharacteristicRowValid(
        measured({ characteristicCode: '外径', measuredValue: '1', uomCode: 'mm', lowerSpecLimit: '5', upperSpecLimit: '1' }),
      ),
    ).toBe(false)
  })

  it('count: requires pass/fail; fail requires a defect reason; defect qty non-negative when set', () => {
    expect(isQualityCharacteristicRowValid(count({ characteristicCode: '外观', countResult: 'pass' }))).toBe(true)
    expect(isQualityCharacteristicRowValid(count({ characteristicCode: '外观', countResult: '' }))).toBe(false)
    expect(isQualityCharacteristicRowValid(count({ characteristicCode: '外观', countResult: 'fail' }))).toBe(false)
    expect(
      isQualityCharacteristicRowValid(count({ characteristicCode: '外观', countResult: 'fail', defectReason: 'SCRATCH' })),
    ).toBe(true)
    expect(
      isQualityCharacteristicRowValid(
        count({ characteristicCode: '外观', countResult: 'fail', defectReason: 'SCRATCH', defectQuantity: '-1' }),
      ),
    ).toBe(false)
  })
})

describe('qualityCharacteristicRowsValid', () => {
  it('is false for an empty set (at least one explicit characteristic required)', () => {
    expect(qualityCharacteristicRowsValid([])).toBe(false)
  })

  it('requires every row to be valid', () => {
    const good = measured({ characteristicCode: '外径', measuredValue: '1', uomCode: 'mm' })
    const bad = count({ characteristicCode: '外观', countResult: 'fail' }) // missing reason
    expect(qualityCharacteristicRowsValid([good])).toBe(true)
    expect(qualityCharacteristicRowsValid([good, bad])).toBe(false)
  })
})

describe('qualityInspectionOverallVerdict', () => {
  it('is fail if any row is fail, else pass', () => {
    const pass = measured({ characteristicCode: '外径', measuredValue: '10', uomCode: 'mm', upperSpecLimit: '11' })
    const failMeasured = measured({ characteristicCode: '外径', measuredValue: '12', uomCode: 'mm', upperSpecLimit: '11' })
    const failCount = count({ characteristicCode: '外观', countResult: 'fail', defectReason: 'SCRATCH' })
    expect(qualityInspectionOverallVerdict([pass])).toBe('pass')
    expect(qualityInspectionOverallVerdict([pass, failMeasured])).toBe('fail')
    expect(qualityInspectionOverallVerdict([pass, failCount])).toBe('fail')
  })
})

describe('toQualityCharacteristicResultLines', () => {
  it('maps a measured row to observed/measured value + derived result, no defect fields', () => {
    const [line] = toQualityCharacteristicResultLines([
      measured({ characteristicCode: '外径', measuredValue: '10.5', uomCode: 'mm', upperSpecLimit: '10' }),
    ])
    expect(line).toEqual({
      characteristicCode: '外径',
      observedValue: '10.5',
      unitCode: 'mm',
      result: 'fail',
      defectReason: null,
      defectQuantity: null,
      measuredValue: 10.5,
    })
  })

  it('maps a passing count row with no defect metadata', () => {
    const [line] = toQualityCharacteristicResultLines([
      count({ characteristicCode: '外观', countResult: 'pass' }),
    ])
    expect(line).toMatchObject({
      characteristicCode: '外观',
      result: 'pass',
      observedValue: '合格',
      defectReason: null,
      defectQuantity: null,
      measuredValue: null,
    })
  })

  it('maps a failing count row carrying reason code + defect quantity', () => {
    const [line] = toQualityCharacteristicResultLines([
      count({ characteristicCode: '外观', countResult: 'fail', defectReason: 'SCRATCH', defectQuantity: '3' }),
    ])
    expect(line).toMatchObject({
      characteristicCode: '外观',
      result: 'fail',
      observedValue: '不合格',
      defectReason: 'SCRATCH',
      defectQuantity: 3,
      measuredValue: null,
    })
  })
})
