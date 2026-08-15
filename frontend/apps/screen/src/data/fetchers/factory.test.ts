import { describe, expect, it } from 'vitest'
import { DEFAULT_FACTORY_ID } from '@/data/mock/masterdata'
import { fetchFactoryOverview } from './factory'

describe('fetchFactoryOverview', () => {
  it('返回工厂总览形状（kpis/workshops/oee/双流 完整）', async () => {
    const ov = await fetchFactoryOverview(DEFAULT_FACTORY_ID)
    expect(ov.factoryId).toBe(DEFAULT_FACTORY_ID)
    expect(ov.kpis.achievement).toBeGreaterThanOrEqual(0)
    expect(ov.workshops.length).toBeGreaterThan(0)
    expect(ov.workshops[0]).toHaveProperty('health')
    expect(ov.oee.length).toBe(3)
    expect(ov.alarms.length).toBeGreaterThan(0)
    expect(ov.downtimes.length).toBeGreaterThan(0)
  })
})
