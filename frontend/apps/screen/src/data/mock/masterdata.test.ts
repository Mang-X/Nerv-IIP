import { describe, expect, it } from 'vitest'
import {
  DEFAULT_FACTORY_ID,
  DEVICES,
  devicesByLine,
  devicesByWorkshop,
  linesByWorkshop,
  LINES,
  WORK_CENTERS,
  workshopsByFactory,
  WORKSHOPS,
} from './masterdata'

describe('大屏 mock 目录', () => {
  it('产线归属车间、设备经工作中心归属产线，映射自洽', () => {
    for (const l of LINES) {
      expect(devicesByLine(l.id).every((d) => d.lineId === l.id)).toBe(true)
    }
    for (const w of WORKSHOPS) {
      const lineIds = new Set(linesByWorkshop(w.id).map((l) => l.id))
      expect(devicesByWorkshop(w.id).every((d) => lineIds.has(d.lineId))).toBe(true)
    }
    const wcById = new Map(WORK_CENTERS.map((wc) => [wc.id, wc]))
    for (const d of DEVICES) {
      const wc = wcById.get(d.workCenterId)
      expect(wc).toBeDefined()
      expect(d.workshopId).toBe(wc!.workshopId)
      expect(d.lineId).toBe(wc!.lineId)
    }
  })

  it('装配车间存在且有产线（供 workshop-lead persona 用）', () => {
    expect(workshopsByFactory(DEFAULT_FACTORY_ID).some((w) => w.id === 'WS-02')).toBe(true)
    expect(linesByWorkshop('WS-02').length).toBeGreaterThan(0)
  })
})
