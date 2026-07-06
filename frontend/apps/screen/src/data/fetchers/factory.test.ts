import { describe, expect, it } from 'vitest'
import { fetchFactoryOverview } from './factory'

describe('fetchFactoryOverview', () => {
  it('返回工厂总览形状（kpis/workshops/oee/alarms 非空）', async () => {
    const ov = await fetchFactoryOverview()
    expect(ov.kpis.length).toBeGreaterThan(0)
    expect(ov.workshops.length).toBeGreaterThan(0)
    expect(ov.oee.length).toBeGreaterThan(0)
    expect(ov.alarms.length).toBeGreaterThan(0)
    expect(ov.workshops[0]).toHaveProperty('tone')
  })
})
