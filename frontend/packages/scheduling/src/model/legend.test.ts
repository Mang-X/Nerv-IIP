import { describe, expect, it } from 'vitest'
import { toModel } from './aps-mapper'
import { samplePlan, samplePlanWithCalendar } from './fixtures'
import { deriveLegendSemantics } from './legend'

// 图例不许列图上没有的东西——这组用例就是那条硬约束的门禁。
describe('deriveLegendSemantics', () => {
  it('只列方案里真实出现过的阻塞类型', () => {
    expect(deriveLegendSemantics(toModel(samplePlanWithCalendar)).blocks).toEqual([
      'maintenance',
      'changeover',
    ])
  })

  it('方案没有资源时间块时,阻塞一组为空', () => {
    expect(deriveLegendSemantics(toModel(samplePlan)).blocks).toEqual([])
  })

  it('后端带出工作日历才谈班次边界', () => {
    expect(deriveLegendSemantics(toModel(samplePlanWithCalendar)).calendar.shift).toBe(true)
    expect(deriveLegendSemantics(toModel(samplePlan)).calendar.shift).toBe(false)
    // 非工作底纹恒在:没有日历时引擎也会按通用作息画。
    expect(deriveLegendSemantics(toModel(samplePlan)).calendar.nonWorking).toBe(true)
  })

  it('「现在」线只在计划期覆盖当下时出现', () => {
    const model = toModel(samplePlan)
    expect(deriveLegendSemantics(model, Date.parse('2026-06-10T09:00:00.000Z')).calendar.now).toBe(
      true,
    )
    expect(deriveLegendSemantics(model, Date.parse('2026-07-01T09:00:00.000Z')).calendar.now).toBe(
      false,
    )
  })

  it('状态一组跟着模型走:有冲突有锁定才列', () => {
    const s = deriveLegendSemantics(toModel(samplePlan))
    expect(s.status).toEqual({ conflict: true, locked: true, materialRisk: false })

    const clean = toModel({ ...samplePlan, conflicts: [] })
    for (const task of clean.tasks) task.locked = false
    expect(deriveLegendSemantics(clean).status).toEqual({
      conflict: false,
      locked: false,
      materialRisk: false,
    })
  })

  // 图例一致性铁律(#1274):卡片/tooltip 上的「缺料待备」chip 出现在图上,图例就必须同步出现;
  // 方案全齐套时它绝不出现。
  it('物料风险:有缺料工序才列「缺料待备」', () => {
    expect(deriveLegendSemantics(toModel(samplePlan)).status.materialRisk).toBe(false)

    const risky = toModel({
      ...samplePlan,
      materialRisks: [
        {
          orderId: 'WO-001',
          operationId: 'op-10',
          reasonCodes: ['material-shortage'],
          shortages: [
            {
              materialId: 'RM-OIL-01',
              materialLotId: null,
              requiredQuantity: 145.86,
              availableQuantity: 0,
              shortageQuantity: 145.86,
            },
          ],
          message: '物料未齐套：RM-OIL-01 缺 145.86。已按计划排入,需在开工前完成备料。',
        },
      ],
    })
    expect(deriveLegendSemantics(risky).status.materialRisk).toBe(true)
  })

  it('卡片语义(优先级/插单/齐套/换型/瓶颈)缺省不列', () => {
    const s = deriveLegendSemantics(toModel(samplePlan))
    expect(s.card).toEqual({
      priority: false,
      rush: false,
      kitting: false,
      changeover: false,
      bottleneck: false,
    })
  })

  it('依赖箭头只在真有依赖链时列', () => {
    expect(deriveLegendSemantics(toModel(samplePlan)).gantt.link).toBe(true)
    expect(deriveLegendSemantics({ ...toModel(samplePlan), links: [] }).gantt.link).toBe(false)
  })
})
