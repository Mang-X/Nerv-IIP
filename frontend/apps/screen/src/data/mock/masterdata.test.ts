import { describe, expect, it } from 'vitest'
import {
  DEVICES,
  devicesByLine,
  devicesByWorkshop,
  FACTORIES,
  linesByWorkshop,
  LINES,
  workshopsByFactory,
} from './masterdata'

describe('masterdata 映射字典', () => {
  it('两个工厂，且每个车间归属某工厂', () => {
    expect(FACTORIES.map((f) => f.id)).toEqual(['F01', 'F02'])
    expect(workshopsByFactory('F01').length).toBeGreaterThan(0)
  })

  it('产线归属车间、设备归属产线，映射自洽', () => {
    for (const l of LINES) {
      expect(devicesByLine(l.id).every((d) => d.lineId === l.id)).toBe(true)
    }
    const wsId = workshopsByFactory('F01')[0].id
    const lineIds = new Set(linesByWorkshop(wsId).map((l) => l.id))
    expect(devicesByWorkshop(wsId).every((d) => lineIds.has(d.lineId))).toBe(true)
  })

  it('电池车间存在且有产线（供 workshop-lead persona 用）', () => {
    expect(workshopsByFactory('F01').some((w) => w.id === 'WS-BATTERY')).toBe(true)
    expect(linesByWorkshop('WS-BATTERY').length).toBeGreaterThan(0)
    expect(DEVICES.length).toBeGreaterThan(10)
  })
})
