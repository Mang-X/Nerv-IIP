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
    expect(getPdaTaskKind('mes.report')?.routeReady).toBe(false)
  })

  it('lights up the delivered WMS frontline pages (inbound receiving + outbound review)', () => {
    expect(getPdaTaskKind('wms.inbound')?.routeReady).toBe(true)
    expect(getPdaTaskKind('wms.review')?.routeReady).toBe(true)
  })

  it('lights up the #374-unlocked WMS frontline pages (picking + putaway + count)', () => {
    expect(getPdaTaskKind('wms.pick')?.routeReady).toBe(true)
    expect(getPdaTaskKind('wms.putaway')?.routeReady).toBe(true)
    expect(getPdaTaskKind('wms.count')?.routeReady).toBe(true)
  })

  it('keeps MES/equipment tasks dark until their plans land (no fake links)', () => {
    expect(getPdaTaskKind('mes.report')?.routeReady).toBe(false)
    expect(getPdaTaskKind('mes.issue')?.routeReady).toBe(false)
    expect(getPdaTaskKind('mes.receipt')?.routeReady).toBe(false)
    expect(getPdaTaskKind('mes.operation')?.routeReady).toBe(false)
    expect(getPdaTaskKind('equipment.repair')?.routeReady).toBe(false)
    expect(getPdaTaskKind('equipment.inspect')?.routeReady).toBe(false)
  })

  it('returns undefined for unknown ids', () => {
    expect(getPdaTaskKind('nope')).toBeUndefined()
  })
})
