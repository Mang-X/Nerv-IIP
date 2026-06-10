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

  it('marks not-yet-implemented WMS/MES tasks so the app wall can disable them (no fake links)', () => {
    expect(getPdaTaskKind('wms.pick')?.routeReady).toBe(false)
    expect(getPdaTaskKind('mes.report')?.routeReady).toBe(false)
    // 本分支所有 mes.*/wms.* 仍未落地，应保持 false。
    for (const kind of PDA_TASK_KINDS) {
      if (kind.group === 'wms' || kind.group === 'mes') {
        expect(kind.routeReady, `${kind.id} should stay false`).toBe(false)
      }
    }
  })

  it('lights up the equipment maintenance trio (Plan 4: repair / inspect / alarms)', () => {
    expect(getPdaTaskKind('equipment.repair')?.routeReady).toBe(true)
    expect(getPdaTaskKind('equipment.inspect')?.routeReady).toBe(true)
    expect(getPdaTaskKind('equipment.alarms')?.routeReady).toBe(true)
  })

  it('adds the new equipment.alarms entry pointing at the read-only alarms route', () => {
    expect(getPdaTaskKind('equipment.alarms')).toMatchObject({
      id: 'equipment.alarms',
      label: '查看报警',
      group: 'equipment',
      route: '/equipment/alarms',
      routeReady: true,
    })
  })

  it('returns undefined for unknown ids', () => {
    expect(getPdaTaskKind('nope')).toBeUndefined()
  })
})
