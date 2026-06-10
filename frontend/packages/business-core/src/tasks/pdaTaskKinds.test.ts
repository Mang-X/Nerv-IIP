import { describe, expect, it } from 'vitest'
import { PDA_TASK_KINDS, getPdaTaskKind } from './pdaTaskKinds'

describe('PDA task kinds dictionary', () => {
  it('covers the v1 frontline tasks with Chinese labels and route targets', () => {
    const ids = PDA_TASK_KINDS.map((k) => k.id)
    expect(ids).toEqual(
      expect.arrayContaining(['wms.inbound', 'wms.putaway', 'wms.pick', 'wms.count', 'mes.report']),
    )
    expect(getPdaTaskKind('wms.inbound')).toMatchObject({ label: '收货入库', route: '/wms/inbound' })
  })

  it('marks not-yet-implemented tasks so the app wall can disable them (no fake links)', () => {
    expect(getPdaTaskKind('wms.pick')?.routeReady).toBe(false)
    expect(getPdaTaskKind('mes.report')?.routeReady).toBe(false)
  })

  it('lights up the delivered WMS frontline pages (inbound receiving + outbound review)', () => {
    expect(getPdaTaskKind('wms.inbound')?.routeReady).toBe(true)
    expect(getPdaTaskKind('wms.review')?.routeReady).toBe(true)
  })

  it('keeps blocked WMS tasks dark until #374 lands (no half-baked entries)', () => {
    expect(getPdaTaskKind('wms.pick')?.routeReady).toBe(false)
    expect(getPdaTaskKind('wms.putaway')?.routeReady).toBe(false)
    expect(getPdaTaskKind('wms.count')?.routeReady).toBe(false)
  })

  it('returns undefined for unknown ids', () => {
    expect(getPdaTaskKind('nope')).toBeUndefined()
  })
})
