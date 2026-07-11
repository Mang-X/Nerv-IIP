import { describe, expect, it } from 'vitest'
import { fetchFactoryOverview } from './factory'

describe('fetchFactoryOverview', () => {
  it('返回工厂总览形状（kpis/workshops/oee/双流 完整）', async () => {
    const ov = await fetchFactoryOverview('F01')
    expect(ov.factoryId).toBe('F01')
    expect(ov.kpis.achievement).toBeGreaterThanOrEqual(0)
    expect(ov.workshops.length).toBeGreaterThan(0)
    expect(ov.workshops[0]).toHaveProperty('health')
    expect(ov.oee.length).toBe(3)
    expect(ov.alarms.length).toBeGreaterThan(0)
    expect(ov.downtimes.length).toBeGreaterThan(0)
  })
})
