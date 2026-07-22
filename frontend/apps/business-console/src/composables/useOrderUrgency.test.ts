import type { BusinessConsoleOrderUrgency } from '@nerv-iip/api-client'
import { describe, expect, it } from 'vitest'
import { indexOrderUrgenciesByReference } from './useOrderUrgency'

function urgency(orderId: string, businessReference: string, level: string) {
  return { orderId, businessReference, level } as BusinessConsoleOrderUrgency
}

describe('indexOrderUrgenciesByReference', () => {
  it('keeps the most urgent order for a shared upstream business reference', () => {
    const normal = urgency('WO-002', 'SO-001', 'normal')
    const critical = urgency('WO-001', 'SO-001', 'critical')

    const indexed = indexOrderUrgenciesByReference([normal, critical])

    expect(indexed.get('SO-001')).toBe(critical)
    expect(indexed.get('WO-001')).toBe(critical)
    expect(indexed.get('WO-002')).toBe(normal)
  })
})
