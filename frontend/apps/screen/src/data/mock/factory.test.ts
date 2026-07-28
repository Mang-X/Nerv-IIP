import { describe, expect, it } from 'vitest'
import { buildFactoryOverview, composeHealth, dailyDeliveryPlan, HEALTH_RULES } from './factory'
import { DEFAULT_FACTORY_ID, workshopsByFactory } from './masterdata'
import { FINAL_WORKSHOP_ID } from './world'

describe('composeHealth（spec §二 合成规则）', () => {
  const base = { critAlarms: 0, overdue: 0, openDowntime: 0, rate: 95 }
  it('critical 告警 → 红', () => {
    expect(composeHealth({ ...base, critAlarms: 1 })).toBe('red')
  })
  it('超期工单 → 红（优先于黄条件）', () => {
    expect(composeHealth({ ...base, overdue: 1, openDowntime: 1 })).toBe('red')
  })
  it('Open 停机 → 黄', () => {
    expect(composeHealth({ ...base, openDowntime: 1 })).toBe('yellow')
  })
  it('达成率低于阈值 → 黄', () => {
    expect(composeHealth({ ...base, rate: HEALTH_RULES.rateYellowBelow - 1 })).toBe('yellow')
  })
  it('无异常 → 绿', () => {
    expect(composeHealth(base)).toBe('green')
  })
})

// 设定集 §7 规模：成品日下线 ≈3200 件（3200 单 × 均量 173 件 / 174 工作日）。
describe('dailyDeliveryPlan（车间对外交付口径）', () => {
  it('三车间交付量级符合小型减振器工厂，不是整车厂量级', () => {
    // 机加：精磨线 1600（活塞杆）+ 缸筒两线 2×800 = 3200/班 → 6400/日
    expect(dailyDeliveryPlan('WS-01')).toBe(6400)
    // 装配：前减 3×320 + 后减 2×327 = 1614/班 → 3228/日（阀系预装是车间内部中间件）
    expect(dailyDeliveryPlan('WS-02')).toBe(3228)
    // 表面与包装：仅包装线交付（电泳/性能检测是同一物件的前道工序）
    expect(dailyDeliveryPlan('WS-03')).toBe(3200)
  })
})

const ROUNDS = 20

describe('buildFactoryOverview', () => {
  it('车间数对账、红卡置顶、KPI 聚合一致、双流非空', () => {
    for (let i = 0; i < ROUNDS; i++) {
      const s = buildFactoryOverview()
      expect(s.factoryId).toBe(DEFAULT_FACTORY_ID)
      expect(s.workshops).toHaveLength(workshopsByFactory(DEFAULT_FACTORY_ID).length)
      // 红卡置顶：健康度序 red→yellow→green 单调不减
      const order = { red: 0, yellow: 1, green: 2 }
      const seq = s.workshops.map((w) => order[w.health])
      expect([...seq].sort((a, b) => a - b)).toEqual(seq)
      // 全厂产量 = **末道车间**成品下线，绝不是 Σ 车间（串行价值链会重复计数）
      const final = s.workshops.find((w) => w.id === FINAL_WORKSHOP_ID)!
      expect(s.kpis.todayOutput).toBe(final.actualQty)
      expect(s.kpis.todayPlan).toBe(final.planQty)
      expect(s.kpis.todayOutput).toBeLessThan(s.workshops.reduce((n, w) => n + w.actualQty, 0))
      expect(s.kpis.criticalAlarms).toBe(s.workshops.reduce((n, w) => n + w.critAlarms, 0))
      expect(s.kpis.openDowntime).toBe(s.workshops.reduce((n, w) => n + w.openDowntime, 0))
      expect(s.kpis.wipOrders).toBe(s.workshops.reduce((n, w) => n + w.wip, 0))
      expect(s.kpis.achievement).toBeGreaterThanOrEqual(0)
      expect(s.kpis.achievement).toBeLessThanOrEqual(100)
      // 成品日下线不得越过设定集量级（3200 件/日），杜绝整车厂式的万级数字
      expect(s.kpis.todayPlan).toBeLessThanOrEqual(3200)
      for (const w of s.workshops) {
        expect(w.rate).toBeGreaterThanOrEqual(0)
        expect(w.rate).toBeLessThanOrEqual(100)
        expect(w.actualQty).toBeLessThanOrEqual(w.planQty)
        expect(w.health).toBe(composeHealth(w))
        expect(w.manager).toBeTruthy()
      }
      // 两条流都要溢出可滚（ScrollBoard 不溢出不滚）
      expect(s.alarms.length).toBeGreaterThanOrEqual(8)
      expect(s.downtimes.length).toBeGreaterThanOrEqual(8)
      expect(s.oee.map((o) => o.label)).toEqual(['可用率', '性能率', '良品率'])
    }
  })

  it('车间画像与设备屏勾稽：机加唯一红卡 + 两处停机（换型 / 待修）', () => {
    const s = buildFactoryOverview()
    expect(s.workshops.filter((w) => w.health === 'red').map((w) => w.id)).toEqual(['WS-01'])
    expect(s.kpis.criticalAlarms).toBe(1)
    expect(s.kpis.openDowntime).toBe(2)
    expect(s.alarms.some((a) => a.level === 'critical' && a.text.includes('DEV-CNC-03'))).toBe(true)
    expect(s.downtimes.some((d) => d.text.includes('DEV-CTG-02'))).toBe(true)
    expect(s.downtimes.some((d) => d.text.includes('DEV-ASM-05/06'))).toBe(true)
  })

  it('待处置 NCR 与质量屏同源（跨屏同一事实同一数据源）', () => {
    const s = buildFactoryOverview()
    expect(s.kpis.openNcr).toBe(13)
  })

  it('scope 收窄：只聚合白名单车间，流内容同步收窄', () => {
    const s = buildFactoryOverview(DEFAULT_FACTORY_ID, ['WS-01'])
    expect(s.workshops).toHaveLength(1)
    expect(s.workshops[0].id).toBe('WS-01')
    // 末道车间不可见时退回可见车间合计
    expect(s.kpis.todayOutput).toBe(s.workshops[0].actualQty)
    // 流里不得出现其它车间名
    const otherNames = workshopsByFactory(DEFAULT_FACTORY_ID)
      .filter((w) => w.id !== 'WS-01')
      .map((w) => w.shortName)
    for (const item of [...s.alarms, ...s.downtimes]) {
      for (const name of otherNames) expect(item.text).not.toContain(name)
    }
  })
})
