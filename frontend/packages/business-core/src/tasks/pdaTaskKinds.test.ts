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

  it('keeps not-yet-implemented WMS tasks disabled so the app wall can disable them (no fake links)', () => {
    // WMS PDA 入口仍待后端缺口列表端点（#374 之外的个人过滤/缺口），保持 disabled。
    expect(getPdaTaskKind('wms.pick')?.routeReady).toBe(false)
    expect(getPdaTaskKind('wms.inbound')?.routeReady).toBe(false)
    // 合并 #378(MES) + #379(equipment) 后，所有 wms.* 仍未落地，应保持 false。
    for (const kind of PDA_TASK_KINDS) {
      if (kind.group === 'wms') {
        expect(kind.routeReady, `${kind.id} should stay false`).toBe(false)
      }
    }
  })

  it('lights up the MES frontline tasks once their work pages land (Plan 3)', () => {
    expect(getPdaTaskKind('mes.report')?.routeReady).toBe(true)
    expect(getPdaTaskKind('mes.issue')?.routeReady).toBe(true)
    expect(getPdaTaskKind('mes.receipt')?.routeReady).toBe(true)
    expect(getPdaTaskKind('mes.operation')?.routeReady).toBe(true)
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
