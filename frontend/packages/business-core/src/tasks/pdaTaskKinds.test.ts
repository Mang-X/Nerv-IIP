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
    // WMS PDA 入口在本分支仍待后端缺口列表端点，保持 disabled。
    expect(getPdaTaskKind('wms.pick')?.routeReady).toBe(false)
    expect(getPdaTaskKind('wms.inbound')?.routeReady).toBe(false)
    expect(getPdaTaskKind('equipment.repair')?.routeReady).toBe(false)
  })

  it('lights up the MES frontline tasks once their work pages land (Plan 3)', () => {
    expect(getPdaTaskKind('mes.report')?.routeReady).toBe(true)
    expect(getPdaTaskKind('mes.issue')?.routeReady).toBe(true)
    expect(getPdaTaskKind('mes.receipt')?.routeReady).toBe(true)
    expect(getPdaTaskKind('mes.operation')?.routeReady).toBe(true)
  })

  it('returns undefined for unknown ids', () => {
    expect(getPdaTaskKind('nope')).toBeUndefined()
  })
})
