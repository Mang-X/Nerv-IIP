import { describe, expect, it } from 'vitest'
import { PDA_TASK_KINDS, getPdaTaskKind } from './pdaTaskKinds'

describe('PDA task kinds dictionary', () => {
  it('covers the v1 frontline tasks with Chinese labels and route targets', () => {
    const ids = PDA_TASK_KINDS.map((k) => k.id)
    expect(ids).toEqual(
      expect.arrayContaining(['wms.inbound', 'wms.putaway', 'wms.pick', 'wms.count', 'mes.report']),
    )
    expect(getPdaTaskKind('wms.inbound')).toMatchObject({
      label: '收货入库',
      route: '/wms/inbound',
    })
  })

  it('lights up every PDA entry now that all WMS/MES/equipment pages are delivered', () => {
    // 合并 #378(MES) + #379(equipment) + #380(WMS) 后所有作业页均已落地，应用墙无 disabled 入口。
    for (const kind of PDA_TASK_KINDS) {
      expect(kind.routeReady, `${kind.id} should be routeReady`).toBe(true)
    }
  })

  it('lights up the delivered WMS frontline pages (inbound / review / pick / putaway / count)', () => {
    expect(getPdaTaskKind('wms.inbound')?.routeReady).toBe(true)
    expect(getPdaTaskKind('wms.review')?.routeReady).toBe(true)
    expect(getPdaTaskKind('wms.pick')?.routeReady).toBe(true)
    expect(getPdaTaskKind('wms.putaway')?.routeReady).toBe(true)
    expect(getPdaTaskKind('wms.count')?.routeReady).toBe(true)
  })

  it('lights up the MES frontline tasks (Plan 3: report / issue / receipt / operation)', () => {
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

  it('adds the quality.tasks entry pointing at the inspection task execution route', () => {
    expect(getPdaTaskKind('quality.tasks')).toMatchObject({
      id: 'quality.tasks',
      label: '检验任务',
      group: 'quality',
      route: '/quality/tasks',
      routeReady: true,
    })
  })

  it('adds the equipment.alarms entry pointing at the read-only alarms route', () => {
    expect(getPdaTaskKind('equipment.alarms')).toMatchObject({
      id: 'equipment.alarms',
      label: '查看报警',
      group: 'equipment',
      route: '/equipment/alarms',
      routeReady: true,
    })
  })

  it('adds the two shift-handover entries pointing at the PDA entry/accept routes (#2784)', () => {
    // 两个入口不是一个页面的两种叫法：交班录入写 handovers.manage，接班确认先读 handovers.read，
    // 首页按权限裁剪时必须能分别隐藏，所以字典里就是两条。
    expect(getPdaTaskKind('mes.handover')).toMatchObject({
      id: 'mes.handover',
      label: '交班',
      group: 'mes',
      route: '/mes/handover',
      routeReady: true,
    })
    expect(getPdaTaskKind('mes.handover-accept')).toMatchObject({
      id: 'mes.handover-accept',
      label: '接班',
      group: 'mes',
      route: '/mes/handovers',
      routeReady: true,
    })
    // 两条路由必须不同：同一路由会让「交班」点进接班列表。
    expect(getPdaTaskKind('mes.handover')?.route).not.toBe(
      getPdaTaskKind('mes.handover-accept')?.route,
    )
  })

  it('returns undefined for unknown ids', () => {
    expect(getPdaTaskKind('nope')).toBeUndefined()
  })
})
