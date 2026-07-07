import { describe, expect, it } from 'vitest'
import { buildFactoryOverview, composeHealth, HEALTH_RULES } from './factory'
import { workshopsByFactory } from './masterdata'

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

const ROUNDS = 20

describe('buildFactoryOverview', () => {
  it('F01：车间数对账、红卡置顶、KPI 聚合一致、双流非空', () => {
    for (let i = 0; i < ROUNDS; i++) {
      const s = buildFactoryOverview('F01')
      expect(s.factoryId).toBe('F01')
      expect(s.workshops).toHaveLength(workshopsByFactory('F01').length)
      // 红卡置顶：健康度序 red→yellow→green 单调不减
      const order = { red: 0, yellow: 1, green: 2 }
      const seq = s.workshops.map((w) => order[w.health])
      expect([...seq].sort((a, b) => a - b)).toEqual(seq)
      // KPI 聚合与矩阵一致
      expect(s.kpis.todayOutput).toBe(s.workshops.reduce((n, w) => n + w.actualQty, 0))
      expect(s.kpis.todayPlan).toBe(s.workshops.reduce((n, w) => n + w.planQty, 0))
      expect(s.kpis.criticalAlarms).toBe(s.workshops.reduce((n, w) => n + w.critAlarms, 0))
      expect(s.kpis.openDowntime).toBe(s.workshops.reduce((n, w) => n + w.openDowntime, 0))
      expect(s.kpis.wipOrders).toBe(s.workshops.reduce((n, w) => n + w.wip, 0))
      expect(s.kpis.achievement).toBeGreaterThanOrEqual(0)
      expect(s.kpis.achievement).toBeLessThanOrEqual(100)
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

  it('scope 收窄：只聚合白名单车间，流内容同步收窄', () => {
    const s = buildFactoryOverview('F01', ['WS-BATTERY'])
    expect(s.workshops).toHaveLength(1)
    expect(s.workshops[0].id).toBe('WS-BATTERY')
    // 流里不得出现其它车间名
    const otherNames = workshopsByFactory('F01')
      .filter((w) => w.id !== 'WS-BATTERY')
      .map((w) => w.name)
    for (const item of [...s.alarms, ...s.downtimes]) {
      for (const name of otherNames) expect(item.text).not.toContain(name)
    }
  })
})
