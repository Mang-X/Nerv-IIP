import { describe, expect, it } from 'vitest'
import {
  DEFAULT_FACTORY_ID,
  DEVICES,
  devicesByLine,
  devicesByWorkshop,
  FACTORIES,
  linesByWorkshop,
  LINES,
  WORK_CENTERS,
  workshopsByFactory,
  WORKSHOPS,
} from './masterdata'

// 《工厂世界观设定集》L0 黄金向量：这些数字与后端 MasterData WorldBibleSpec.cs
// 逐条对应。改动这里 = 改动演示世界观，必须先改设定集。
describe('masterdata 对齐工厂世界观设定集（L0 黄金向量）', () => {
  it('单基地 SITE-001 一号工厂（宁沪减振科技）', () => {
    expect(FACTORIES.map((f) => f.id)).toEqual(['SITE-001'])
    expect(DEFAULT_FACTORY_ID).toBe('SITE-001')
    expect(FACTORIES[0].name).toContain('宁沪减振')
  })

  it('3 车间 / 14 产线 / 17 工作中心 / 46 台设备', () => {
    expect(WORKSHOPS.map((w) => w.code)).toEqual(['WS-01', 'WS-02', 'WS-03'])
    expect(LINES).toHaveLength(14)
    expect(WORK_CENTERS).toHaveLength(17)
    expect(DEVICES).toHaveLength(46)
    // 设定集 §2：机加 5 + 装配 6 + 表面与包装 3
    expect(linesByWorkshop('WS-01')).toHaveLength(5)
    expect(linesByWorkshop('WS-02')).toHaveLength(6)
    expect(linesByWorkshop('WS-03')).toHaveLength(3)
  })

  it('设备编码段与台数对齐设定集 §3，型号取自 L0', () => {
    const countBy = (prefix: string) => DEVICES.filter((d) => d.code.startsWith(prefix)).length
    expect(countBy('DEV-CNC-')).toBe(10)
    expect(countBy('DEV-GRD-')).toBe(4)
    expect(countBy('DEV-ASM-')).toBe(12)
    expect(countBy('DEV-WLD-')).toBe(3)
    expect(countBy('DEV-CTG-')).toBe(3)
    expect(countBy('DEV-TST-')).toBe(4)
    expect(countBy('DEV-PKG-')).toBe(2)
    expect(countBy('DEV-AUX-')).toBe(8)
    // 大屏与 PC 控制台必须是同一台设备（同编码同型号）
    expect(DEVICES.find((d) => d.code === 'DEV-CNC-03')?.name).toBe('数控车床 CK6150')
    expect(DEVICES.find((d) => d.code === 'DEV-CNC-07')?.name).toBe('立式加工中心 VMC-850')
    expect(DEVICES.find((d) => d.code === 'DEV-GRD-01')?.name).toBe('数控外圆磨床 MK1332')
  })

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
    expect(linesByWorkshop('WS-02')).toHaveLength(6)
  })
})
