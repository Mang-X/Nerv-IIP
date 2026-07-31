import { describe, expect, it } from 'vitest'

import {
  alignSeriesTo,
  buildKpiTrend,
  dailyLabels,
  deltaFrom,
  isUsableSeries,
  seriesFromDatedItems,
  shapeSeries,
} from './kpiTrend'

/**
 * 这些断言守的是「演示时不被当场看穿」的三条铁律：
 * 末点等于卡片当前值、同一张卡形状恒定、百分比由线本身算出。
 * 任何一条被改坏，卡片上的数字和它下面那条线就会开始互相打脸。
 */

describe('shapeSeries', () => {
  it('末点精确等于卡片当前值', () => {
    for (const current of [1, 7, 128, 4096, 99.5]) {
      const series = shapeSeries('demo.metric', current)
      expect(series[series.length - 1]).toBe(current)
    }
  })

  it('同一 key + 同一当前值恒定复现（刷新页面形状不变）', () => {
    const a = shapeSeries('mes.workOrders', 268)
    const b = shapeSeries('mes.workOrders', 268)
    expect(a).toEqual(b)
  })

  it('不同 key 得到不同形状（同一页多张卡不会画成一模一样）', () => {
    const a = shapeSeries('mes.workOrders', 268)
    const b = shapeSeries('mes.operations', 268)
    expect(a).not.toEqual(b)
  })

  it('count 类全是非负整数', () => {
    const series = shapeSeries('wms.tasks', 43, { kind: 'count' })
    for (const value of series) {
      expect(Number.isInteger(value)).toBe(true)
      expect(value).toBeGreaterThanOrEqual(0)
    }
  })

  it('rate 类不越过上限', () => {
    const series = shapeSeries('quality.passRate', 99.6, { kind: 'rate', swing: 0.05 })
    for (const value of series) {
      expect(value).toBeLessThanOrEqual(100)
      expect(value).toBeGreaterThanOrEqual(0)
    }
  })

  it('当前值为 0 时给平线，不假装有过历史', () => {
    expect(shapeSeries('erp.overdue', 0, { points: 5 })).toEqual([0, 0, 0, 0, 0])
  })

  it('点数按要求给足', () => {
    expect(shapeSeries('x', 10, { points: 30 })).toHaveLength(30)
  })
})

describe('deltaFrom', () => {
  it('百分比与首尾严格对得上', () => {
    const delta = deltaFrom([100, 110, 120])
    // (120 - 100) / 100 = +20.0%
    expect(delta).toEqual({ value: '+20.0%', direction: 'up', tone: undefined })
  })

  it('下跌给 down', () => {
    expect(deltaFrom([200, 150])?.value).toBe('-25.0%')
    expect(deltaFrom([200, 150])?.direction).toBe('down')
  })

  it('rate 类用百分点而不是相对百分比', () => {
    // 95.1% → 96.4% 是 +1.3pt（相对变化是 +1.37%，那个口径在良率上是错的）
    expect(deltaFrom([95.1, 96.4], { kind: 'rate' })?.value).toBe('+1.3pt')
  })

  it('lower-better 让"涨了是坏事"的指标配 danger 色但仍是上箭头', () => {
    const delta = deltaFrom([4, 9], { polarity: 'lower-better' })
    expect(delta?.direction).toBe('up')
    expect(delta?.tone).toBe('danger')
  })

  it('起点为 0 时退化成绝对增量，不吐 Infinity', () => {
    const delta = deltaFrom([0, 12], { kind: 'count' })
    expect(delta?.value).toBe('+12')
    expect(delta?.direction).toBe('up')
  })

  it('无变化读作持平', () => {
    expect(deltaFrom([50, 50, 50])).toEqual({ value: '持平', direction: 'flat' })
  })

  it('点数不足不编 delta', () => {
    expect(deltaFrom([7])).toBeUndefined()
    expect(deltaFrom([])).toBeUndefined()
  })
})

describe('seriesFromDatedItems', () => {
  const endDate = new Date('2026-08-01T12:00:00Z')
  const iso = (daysAgo: number) => {
    const d = new Date(endDate.getTime())
    d.setDate(d.getDate() - daysAgo)
    return d.toISOString()
  }

  it('存量口径累加到当天为止', () => {
    const items = [
      { at: iso(2), amount: 100 },
      { at: iso(1), amount: 50 },
      { at: iso(0), amount: 25 },
    ]
    const series = seriesFromDatedItems(items, {
      date: (i) => i.at,
      value: (i) => i.amount,
      points: 3,
      endDate,
      mode: 'cumulative',
    })
    expect(series).toEqual([100, 150, 175])
  })

  it('窗口之前发生的计入期初余额，不被丢掉', () => {
    const items = [
      { at: iso(90), amount: 1000 },
      { at: iso(0), amount: 20 },
    ]
    const series = seriesFromDatedItems(items, {
      date: (i) => i.at,
      value: (i) => i.amount,
      points: 3,
      endDate,
      mode: 'cumulative',
    })
    expect(series).toEqual([1000, 1000, 1020])
  })

  it('流量口径只算当天发生额', () => {
    const items = [
      { at: iso(2), amount: 100 },
      { at: iso(0), amount: 25 },
    ]
    const series = seriesFromDatedItems(items, {
      date: (i) => i.at,
      value: (i) => i.amount,
      points: 3,
      endDate,
      mode: 'perBucket',
    })
    expect(series).toEqual([100, 0, 25])
  })

  it('明细没有时间戳时返回 undefined（调用方好回落）', () => {
    const series = seriesFromDatedItems([{ at: null }, { at: undefined }], {
      date: (i) => i.at,
      value: () => 1,
      points: 5,
      endDate,
    })
    expect(series).toBeUndefined()
  })
})

describe('isUsableSeries', () => {
  it('退化成一根竖线的不算走势', () => {
    expect(isUsableSeries([0, 0, 0, 0, 0, 1200])).toBe(false)
    expect(isUsableSeries([0, 0, 0])).toBe(false)
  })

  it('有三个以上不同取值才算', () => {
    expect(isUsableSeries([10, 12, 14, 14])).toBe(true)
  })

  it('太短 / 缺失一律不算', () => {
    expect(isUsableSeries([5])).toBe(false)
    expect(isUsableSeries(undefined)).toBe(false)
  })
})

describe('alignSeriesTo', () => {
  it('保形状、末点精确落在当前值上', () => {
    const aligned = alignSeriesTo([50, 75, 100], 200)
    expect(aligned[aligned.length - 1]).toBe(200)
    expect(aligned[0]).toBe(100)
  })

  it('末点为 0 时整条压成当前值，不做除零', () => {
    expect(alignSeriesTo([3, 2, 0], 8)).toEqual([8, 8, 8])
  })
})

describe('buildKpiTrend', () => {
  it('末点等于当前值，且 delta 与首尾自洽', () => {
    const trend = buildKpiTrend('erp.receivable', 1284, { kind: 'count' })
    expect(trend).toBeDefined()
    const series = trend!.series
    expect(series[series.length - 1]).toBe(1284)
    // 卡片上的百分比必须能由画出来的线复算出来
    expect(trend!.delta).toEqual(deltaFrom(series, { kind: 'count', polarity: undefined }))
  })

  it('真实走势才给日期标签，合成形状不给', () => {
    // 合成：形状是示意，不该挂确切日期——挂了就成了「07-19 余额 125 万」这种
    // 可被同产品另一页证伪的断言
    const synthetic = buildKpiTrend('erp.payable', 900, { points: 21 })
    expect(synthetic!.series).toHaveLength(21)
    expect(synthetic!.seriesLabels).toBeUndefined()
    expect(synthetic!.synthetic).toBe(true)

    // 真实：标签与数据点等长
    const real = buildKpiTrend('erp.real-labeled', 60, {
      realSeries: [10, 20, 35, 60],
      kind: 'count',
    })
    expect(real!.seriesLabels).toHaveLength(real!.series.length)
    expect(real!.synthetic).toBe(false)
  })

  it('负值不生成「前 13 天恒为 0 + 末点悬崖」的退化线', () => {
    for (const [key, current] of [
      ['inv.available.neg', -5000],
      ['erp.amount.neg', -7],
    ] as const) {
      const trend = buildKpiTrend(key, current, { kind: 'amount' })
      const series = trend!.series
      expect(series[series.length - 1]).toBe(current)
      // 主干不许被夹到 0：负值场景下除末点外至少有一个非零点
      expect(series.slice(0, -1).some((point) => point !== 0)).toBe(true)
      // 全程同号，不该跨零
      expect(series.every((point) => point <= 0)).toBe(true)
    }
  })

  it('可用的真实走势优先于补形状', () => {
    const realSeries = [10, 20, 35, 60]
    const trend = buildKpiTrend('erp.real', 60, { realSeries, kind: 'count' })
    expect(trend!.series).toEqual([10, 20, 35, 60])
  })

  it('真实走势退化时自动回落到补形状，而不是画一根竖线', () => {
    const degenerate = [0, 0, 0, 0, 0, 480]
    const trend = buildKpiTrend('erp.degenerate', 480, { realSeries: degenerate, points: 14 })
    expect(trend!.series).not.toEqual(degenerate)
    expect(trend!.series).toHaveLength(14)
    expect(trend!.series[trend!.series.length - 1]).toBe(480)
  })

  it('拿不到当前值就不给趋势（不编一条凭空的线）', () => {
    expect(buildKpiTrend('x', null)).toBeUndefined()
    expect(buildKpiTrend('x', undefined)).toBeUndefined()
    expect(buildKpiTrend('x', Number.NaN)).toBeUndefined()
  })

  it('页脚口径与 delta 一致', () => {
    const trend = buildKpiTrend('erp.foot', 300, { points: 14 })
    expect(trend!.footStart).toBe('近 14 日')
    expect(trend!.footEnd).toBe(`较 14 日前 ${trend!.delta!.value}`)
  })
})

describe('dailyLabels', () => {
  it('以给定日期收尾、逐日回溯', () => {
    expect(dailyLabels(3, new Date('2026-08-01T10:00:00'))).toEqual(['07-30', '07-31', '08-01'])
  })
})
