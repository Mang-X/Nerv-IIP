import { describe, expect, it } from 'vitest'
import { defineStepFlow } from './defineStepFlow'

interface ReceiveCtx {
  order?: string
  sku?: string
  qty?: number
}

describe('defineStepFlow', () => {
  const flow = defineStepFlow<ReceiveCtx>({
    id: 'receive',
    steps: [
      { id: 'scanOrder', done: (c) => Boolean(c.order) },
      { id: 'scanSku', done: (c) => Boolean(c.sku) },
      { id: 'confirmQty', done: (c) => typeof c.qty === 'number' && c.qty > 0 },
    ],
  })

  it('starts at the first incomplete step', () => {
    expect(flow.currentStep({}).id).toBe('scanOrder')
    expect(flow.currentStep({ order: 'RO-1' }).id).toBe('scanSku')
  })

  it('reports completion only when every step is done', () => {
    expect(flow.isComplete({ order: 'RO-1', sku: 'S1' })).toBe(false)
    expect(flow.isComplete({ order: 'RO-1', sku: 'S1', qty: 5 })).toBe(true)
  })

  it('exposes ordered progress for the UI step indicator', () => {
    expect(flow.progress({ order: 'RO-1' })).toEqual({ completed: 1, total: 3 })
  })
})
