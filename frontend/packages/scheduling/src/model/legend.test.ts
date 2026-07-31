import { describe, expect, it } from 'vitest'
import { toModel } from './aps-mapper'
import { samplePlan, samplePlanWithCalendar } from './fixtures'
import { deriveLegendSemantics } from './legend'
import { resolveTimeScale, shiftBoundaryRendersAt } from './scale'

/** 固定「现在」,免得 calendar.now 跟着真实时间飘。样例计划期覆盖 2026-06-10。 */
const NOW = Date.parse('2026-06-10T09:00:00.000Z')

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
    expect(deriveLegendSemantics(toModel(samplePlanWithCalendar), NOW, 'hour').calendar.shift).toBe(
      true,
    )
    expect(deriveLegendSemantics(toModel(samplePlan), NOW, 'hour').calendar.shift).toBe(false)
    // 非工作底纹恒在:没有日历时引擎也会按通用作息画。
    expect(deriveLegendSemantics(toModel(samplePlan)).calendar.nonWorking).toBe(true)
  })

  // 走查台账 #41:图例=图面事实。班次竖线由引擎逐格判定(单元格起点 === 班次窗口起点),
  // 日级刻度一格是一整天,08:00/16:00 起的班次一条线都画不出来 → 图例也不许列。
  describe('班次边界按当前刻度推导(台账 #41)', () => {
    // 用本地时间构造,避免测试跟着运行机器时区飘。
    const localIso = (hour: number) => new Date(2026, 5, 10, hour, 0, 0, 0).toISOString()
    const planWithShiftsAt = (...hours: number[]) => ({
      ...samplePlan,
      calendars: [
        {
          calendarId: 'CAL-MAIN',
          resourceIds: ['WC-001'],
          workCenterIds: ['WC-001'],
          shiftWindows: hours.map((hour) => ({
            startUtc: localIso(hour),
            endUtc: localIso(hour + 8),
            shiftCode: `shift-${hour}`,
          })),
        },
      ],
    })

    it('班次级刻度下,落在单元格起点(偶数整点)的班次才列', () => {
      const model = toModel(planWithShiftsAt(8, 16))
      expect(deriveLegendSemantics(model, NOW, 'hour').calendar.shift).toBe(true)
    })

    it('班次级刻度下,奇数整点起班落不到 2 小时一格的起点上,不列', () => {
      const model = toModel(planWithShiftsAt(7))
      expect(deriveLegendSemantics(model, NOW, 'hour').calendar.shift).toBe(false)
    })

    // 已知近似(记录假设,不是在断言 DHTMLX 的行为):
    // hour 档假定 2 小时格从**偶数整点**起步。引擎从不设 config.start_date,时间轴范围由任务
    // 时间推导后按刻度对齐,对齐到哪一档单位由 DHTMLX 内部决定——本仓库没有证据,本机也没有
    // DHTMLX 试用包可实测(loader 别名到 stub、引擎契约测试 skip)。
    // 因此:时间轴若起于奇数整点,下面两条的期望值会与图面相反。治本前请勿把它读成"与引擎等价"。
    // 治本方向:引擎回传实际格线,或显式钉死 config.start_date 的相位。
    it('【已知近似】hour 档以「偶数整点 = 格线」为假设,奇数相位时间轴下会与图面相反', () => {
      const evenShift = toModel(planWithShiftsAt(8))
      const oddShift = toModel(planWithShiftsAt(7))
      // 当前假设下的判定:偶数整点起班列、奇数整点起班不列。
      expect(deriveLegendSemantics(evenShift, NOW, 'hour').calendar.shift).toBe(true)
      expect(deriveLegendSemantics(oddShift, NOW, 'hour').calendar.shift).toBe(false)
      // 判定只看班次起点的小时奇偶,不看时间轴从哪里开始——这正是"近似"之所在。
      expect(shiftBoundaryRendersAt(localIso(8), 'hour')).toBe(true)
      expect(shiftBoundaryRendersAt(localIso(7), 'hour')).toBe(false)
    })

    // 与上一条相对:日 / 周 / 月档是**精确**判定,和时间轴相位无关——
    // 格子 ≥1 天,任何非零点起班都不可能等于格子起点,零点起班也只与日边界重合。
    it('日级及以上是精确判定:任何相位下都画不出班次边界', () => {
      for (const scale of ['day', 'week', 'month'] as const) {
        for (const hour of [0, 7, 8, 15, 16, 23]) {
          expect(shiftBoundaryRendersAt(localIso(hour), scale), `${scale}@${hour}`).toBe(false)
        }
      }
    })

    it('日级刻度下班次竖线画不出来,图例不列班次边界', () => {
      const model = toModel(planWithShiftsAt(8, 16))
      expect(deriveLegendSemantics(model, NOW, 'day').calendar.shift).toBe(false)
      // 非工作底纹与「现在」线不受刻度影响,仍照常推导。
      expect(deriveLegendSemantics(model, NOW, 'day').calendar.nonWorking).toBe(true)
    })

    it('周 / 月刻度同理不列', () => {
      const model = toModel(planWithShiftsAt(0, 8, 16))
      expect(deriveLegendSemantics(model, NOW, 'week').calendar.shift).toBe(false)
      expect(deriveLegendSemantics(model, NOW, 'month').calendar.shift).toBe(false)
    })

    it('不传刻度时按 auto 解析:计划期跨度决定实际刻度', () => {
      // toModel 的 horizon 由任务时间推导,这里直接覆写成想验的跨度。
      const short = {
        ...toModel(planWithShiftsAt(8)),
        horizon: { startUtc: localIso(0), endUtc: localIso(24) },
      }
      // 跨度 ≤2 天 → 班次级 → 画得出来
      expect(resolveTimeScale('auto', short.horizon)).toBe('hour')
      expect(deriveLegendSemantics(short, NOW).calendar.shift).toBe(true)

      const long = {
        ...toModel(planWithShiftsAt(8)),
        horizon: { startUtc: localIso(0), endUtc: new Date(2026, 5, 30).toISOString() },
      }
      // 跨度 20 天 → 周级 → 画不出来
      expect(resolveTimeScale('auto', long.horizon)).toBe('week')
      expect(deriveLegendSemantics(long, NOW).calendar.shift).toBe(false)
    })
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
    expect(s.status).toEqual({
      conflict: true,
      locked: true,
      materialRisk: false,
      equipmentRisk: false,
    })

    const clean = toModel({ ...samplePlan, conflicts: [] })
    for (const task of clean.tasks) task.locked = false
    expect(deriveLegendSemantics(clean).status).toEqual({
      conflict: false,
      locked: false,
      materialRisk: false,
      equipmentRisk: false,
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

  // 同一条铁律(#1274/#1320):「设备状态未知」chip 上图,图例必须同步出现;设备状态全都清楚时绝不出现。
  it('设备数据风险:有工序排在状态未知设备上才列「设备状态未知」', () => {
    expect(deriveLegendSemantics(toModel(samplePlan)).status.equipmentRisk).toBe(false)

    const risky = toModel({
      ...samplePlan,
      equipmentRisks: [
        {
          orderId: 'WO-001',
          operationId: 'op-10',
          resourceId: 'DEV-CNC-01',
          reasonCodes: ['equipment.sourceStale'],
          message:
            '设备 DEV-CNC-01 状态未知(采集数据已过期)。已按计划排入,开工前请人工确认设备可用。',
        },
      ],
    })
    expect(deriveLegendSemantics(risky).status.equipmentRisk).toBe(true)
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
