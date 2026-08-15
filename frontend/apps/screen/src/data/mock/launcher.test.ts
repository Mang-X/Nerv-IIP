import { describe, expect, it } from 'vitest'
import { buildEquipmentOverview } from './equipment'
import { buildFactoryOverview } from './factory'
import { buildLauncherSummary } from './launcher'
import {
  DEFAULT_FACTORY_ID,
  devicesByWorkshop,
  linesByWorkshop,
  workshopsByFactory,
} from './masterdata'

// jitter 有随机性：跑多轮验证 clamp 后的不变量恒成立
const ROUNDS = 25

describe('buildLauncherSummary', () => {
  it('计数与 masterdata 对账、比率界内、glance 结构完整', () => {
    const expectDevices = workshopsByFactory(DEFAULT_FACTORY_ID).reduce(
      (n, w) => n + devicesByWorkshop(w.id).length,
      0,
    )
    for (let i = 0; i < ROUNDS; i++) {
      const s = buildLauncherSummary(DEFAULT_FACTORY_ID)
      expect(s.factoryId).toBe(DEFAULT_FACTORY_ID)
      expect(s.kpis.totalDevices).toBe(expectDevices)
      expect(s.kpis.runningDevices).toBeGreaterThanOrEqual(0)
      expect(s.kpis.runningDevices).toBeLessThanOrEqual(s.kpis.totalDevices)
      for (const v of [s.kpis.achievement, s.kpis.health]) {
        expect(v).toBeGreaterThanOrEqual(0)
        expect(v).toBeLessThanOrEqual(100)
      }
      expect(s.kpis.openAlarms).toBeGreaterThanOrEqual(0)
      expect(s.glances.map((g) => g.key)).toEqual([
        'factory',
        'equipment',
        'line',
        'workshop',
        'warehouse',
        'quality',
      ])
      for (const g of s.glances) {
        expect(g.stats).toHaveLength(3)
        expect(g.chipsLabel).toBeTruthy()
        expect(g.chips.length).toBeGreaterThan(0)
      }
      // 成员区与 masterdata 对账：工厂卡=车间数、产线卡=产线数
      const workshops = workshopsByFactory(DEFAULT_FACTORY_ID)
      const lines = workshops.flatMap((w) => linesByWorkshop(w.id))
      expect(s.glances[0].chips).toHaveLength(workshops.length)
      expect(s.glances[2].chips).toHaveLength(lines.length)
    }
  })

  it('设备四桶与设备屏同源真实计数（门厅一瞥 = 进屏后的同一批数字）', () => {
    const s = buildLauncherSummary(DEFAULT_FACTORY_ID)
    const eq = buildEquipmentOverview(DEFAULT_FACTORY_ID)
    expect(s.kpis.runningDevices).toBe(eq.counts.run)
    expect(s.kpis.totalDevices).toBe(46)
    const glance = s.glances.find((g) => g.key === 'equipment')!
    const nums = glance.stats.map((st) => Number.parseInt(st.value, 10))
    expect(nums.every((n) => Number.isFinite(n) && n >= 0)).toBe(true)
    expect(nums.reduce((a, b) => a + b, 0)).toBeLessThanOrEqual(s.kpis.totalDevices)
  })

  it('今日产量与工厂屏同口径（成品下线，≈3200 件/日的减振器小厂量级）', () => {
    for (let i = 0; i < ROUNDS; i++) {
      const s = buildLauncherSummary(DEFAULT_FACTORY_ID)
      expect(s.kpis.output).toBeGreaterThan(0)
      // 整车厂式的万级数字在本厂物理上不成立（46 台设备）
      expect(s.kpis.output).toBeLessThanOrEqual(3200)
    }
    expect(buildFactoryOverview().kpis.todayPlan).toBeLessThanOrEqual(3200)
  })

  it('persona 收窄：只聚合白名单车间（workshop-lead = 装配车间）', () => {
    for (let i = 0; i < ROUNDS; i++) {
      const s = buildLauncherSummary(DEFAULT_FACTORY_ID, ['WS-02'])
      expect(s.kpis.totalDevices).toBe(devicesByWorkshop('WS-02').length)
      expect(s.glances[0].chips).toHaveLength(1)
      expect(s.glances[2].chips).toHaveLength(linesByWorkshop('WS-02').length)
    }
  })

  it('M2 一瞥与各屏 mock 同源：车间报警状态、仓储 WCS 失败、质量超期 NCR', () => {
    const s = buildLauncherSummary(DEFAULT_FACTORY_ID)
    const ws = s.glances.find((g) => g.key === 'workshop')
    // DEV-CNC-03 振动超限 → 车间总览卡红态、车间 chips 含报警的机加车间
    expect(ws?.state).toBe('alarm')
    expect(ws?.chips.some((c) => c.label === '机加车间' && c.tone === 'alarm')).toBe(true)
    const wh = s.glances.find((g) => g.key === 'warehouse')
    // 仓储常驻 2 条 WCS 失败（午后 3 条），一瞥必须反映
    expect(wh?.state).toBe('alarm')
    expect(wh?.chips.some((c) => c.tone === 'alarm')).toBe(true)
    const q = s.glances.find((g) => g.key === 'quality')
    // 质量屏画像固定 2 条超期 NCR、帕累托 TOP1 为活塞杆振纹
    expect(q?.state).toBe('alarm')
    expect(q?.chips[0]?.label).toBe('活塞杆表面振纹')
  })
})
