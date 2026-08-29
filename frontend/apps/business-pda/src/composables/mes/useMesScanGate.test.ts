import { describe, expect, it } from 'vitest'

import { useMesScanGate } from './useMesScanGate'

describe('useMesScanGate', () => {
  it('keeps writes pending until every active scanner has settled', () => {
    const gate = useMesScanGate()

    gate.set('list', 'pending')
    gate.set('context', 'pending')
    gate.set('context', 'resolved')

    expect(gate.pending.value).toBe(true)
    expect(gate.guarded.value).toBe(true)

    gate.set('list', 'resolved')
    expect(gate.pending.value).toBe(false)
    expect(gate.guarded.value).toBe(false)
  })

  it('keeps writes blocked after a failed scan until that scanner is cleared', () => {
    const gate = useMesScanGate()

    gate.set('context', 'rejected')
    expect(gate.guarded.value).toBe(true)

    gate.set('context', 'resolved')
    expect(gate.guarded.value).toBe(false)
  })

  it('drops an abandoned scanner intent from the page gate', () => {
    const gate = useMesScanGate()

    gate.set('list', 'unknown')
    expect(gate.guarded.value).toBe(true)

    gate.clear('list')
    expect(gate.guarded.value).toBe(false)
  })
})
