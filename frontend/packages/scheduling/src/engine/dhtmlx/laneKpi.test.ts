import { describe, expect, it } from 'vitest'
import { hasAnyLaneKpi, resolveLaneKpiVisibility } from './DhtmlxEngine'

/**
 * #1399 M8 泳道产能指标的上屏判定。
 *
 * 背景:读面不透传 oee / changeoverCount / materialRisk(根因 C),映射层也不造,
 * 于是资源排产板每条泳道都写着「OEE 0% · 切换 0 · 待料 0」。**「OEE 0%」比没有这一列更伤**
 * ——领导看见 0% 会问"设备是不是停了",而真相是我们根本没这个数。
 *
 * 判定必须是**跨全部泳道**:某条产线今天切换 0 次是有意义的读数,只有整列都没有非零值
 * 才说明这个字段没有数据供给。
 */
describe('resolveLaneKpiVisibility', () => {
  it('字段在所有泳道都缺失时不上屏', () => {
    const v = resolveLaneKpiVisibility([
      { utilization: 0.74 },
      { utilization: 0.77 },
      { utilization: 0.69 },
    ])
    expect(v.utilization).toBe(true)
    expect(v.oee).toBe(false)
    expect(v.changeoverCount).toBe(false)
    expect(v.materialRisk).toBe(false)
  })

  it('字段在所有泳道都是 0 时同样不上屏(恒 0 不是"表现差",是没数据)', () => {
    const v = resolveLaneKpiVisibility([
      { utilization: 0.74, oee: 0, changeoverCount: 0, materialRisk: 0 },
      { utilization: 0.55, oee: 0, changeoverCount: 0, materialRisk: 0 },
    ])
    expect(v.oee).toBe(false)
    expect(v.changeoverCount).toBe(false)
    expect(v.materialRisk).toBe(false)
  })

  it('只要有一条泳道有非零值就整列上屏——其余泳道的真 0 是有意义的读数,不能连坐隐藏', () => {
    const v = resolveLaneKpiVisibility([
      { changeoverCount: 0, materialRisk: 0 },
      { changeoverCount: 6, materialRisk: 0 },
    ])
    expect(v.changeoverCount).toBe(true)
    expect(v.materialRisk).toBe(false)
  })

  it('NaN / Infinity 不算有效值', () => {
    const v = resolveLaneKpiVisibility([{ oee: Number.NaN }, { oee: Number.POSITIVE_INFINITY }])
    expect(v.oee).toBe(false)
  })

  it('没有任何泳道时全部不上屏', () => {
    const v = resolveLaneKpiVisibility([])
    expect(hasAnyLaneKpi(v)).toBe(false)
  })

  it('四项全无数据时整列「产能指标」都不该出现', () => {
    expect(hasAnyLaneKpi(resolveLaneKpiVisibility([{}, undefined]))).toBe(false)
    expect(hasAnyLaneKpi(resolveLaneKpiVisibility([{ utilization: 0.9 }]))).toBe(true)
  })
})
