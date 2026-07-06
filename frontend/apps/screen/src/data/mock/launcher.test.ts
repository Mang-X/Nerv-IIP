import { describe, expect, it } from 'vitest'
import { buildLauncherSummary } from './launcher'
import { devicesByWorkshop, linesByWorkshop, workshopsByFactory } from './masterdata'

// jitter 有随机性：跑多轮验证 clamp 后的不变量恒成立
const ROUNDS = 25

describe('buildLauncherSummary', () => {
  for (const factoryId of ['F01', 'F02']) {
    it(`${factoryId}: 计数与 masterdata 对账、比率界内、glance 结构完整`, () => {
      const expectDevices = workshopsByFactory(factoryId).reduce(
        (n, w) => n + devicesByWorkshop(w.id).length,
        0,
      )
      for (let i = 0; i < ROUNDS; i++) {
        const s = buildLauncherSummary(factoryId)
        expect(s.factoryId).toBe(factoryId)
        expect(s.kpis.totalDevices).toBe(expectDevices)
        expect(s.kpis.runningDevices).toBeGreaterThanOrEqual(0)
        expect(s.kpis.runningDevices).toBeLessThanOrEqual(s.kpis.totalDevices)
        for (const v of [s.kpis.achievement, s.kpis.health]) {
          expect(v).toBeGreaterThanOrEqual(0)
          expect(v).toBeLessThanOrEqual(100)
        }
        expect(s.kpis.openAlarms).toBeGreaterThanOrEqual(0)
        expect(s.glances.map((g) => g.key)).toEqual(['factory', 'equipment', 'line'])
        for (const g of s.glances) {
          expect(g.stats).toHaveLength(3)
          expect(g.chipsLabel).toBeTruthy()
          expect(g.chips.length).toBeGreaterThan(0)
        }
        // 成员区与 masterdata 对账：工厂卡=车间数、产线卡=产线数
        const workshops = workshopsByFactory(factoryId)
        const lines = workshops.flatMap((w) => linesByWorkshop(w.id))
        expect(s.glances[0].chips).toHaveLength(workshops.length)
        expect(s.glances[2].chips).toHaveLength(lines.length)
      }
    })
  }

  it('persona 收窄：只聚合白名单车间（workshop-lead 场景）', () => {
    for (let i = 0; i < ROUNDS; i++) {
      const s = buildLauncherSummary('F01', ['WS-BATTERY'])
      expect(s.kpis.totalDevices).toBe(devicesByWorkshop('WS-BATTERY').length)
      expect(s.glances[0].chips).toHaveLength(1) // 仅电池车间
      expect(s.glances[2].chips).toHaveLength(linesByWorkshop('WS-BATTERY').length) // 仅 2 条线
      // 产量按产线占比折算，必须远小于全厂
      const full = buildLauncherSummary('F01')
      expect(s.kpis.output).toBeLessThan(full.kpis.output)
    }
  })

  it('设备四桶对账：equipment glance 三数非负且不超设备总数', () => {
    for (let i = 0; i < ROUNDS; i++) {
      const s = buildLauncherSummary('F01')
      const eq = s.glances.find((g) => g.key === 'equipment')
      expect(eq).toBeDefined()
      const nums = eq!.stats.map((st) => Number.parseInt(st.value, 10))
      expect(nums.every((n) => Number.isFinite(n) && n >= 0)).toBe(true)
      expect(nums.reduce((a, b) => a + b, 0)).toBeLessThanOrEqual(s.kpis.totalDevices)
    }
  })
})
