import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it } from 'vitest'
import { useAccessScope } from './useAccessScope'

describe('useAccessScope', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('默认 plant-admin：可见全部大屏；设定集只有一个基地 SITE-001', () => {
    const s = useAccessScope()
    expect(s.allowedScreens).toEqual([
      'factory',
      'equipment',
      'line',
      'workshop',
      'warehouse',
      'quality',
    ])
    expect(s.factories.map((f) => f.id)).toEqual(['SITE-001'])
    expect(s.currentFactoryId).toBe('SITE-001')
    expect(s.canSeeScreen('equipment')).toBe(true)
    expect(s.canSeeScreen('quality')).toBe(true)
  })

  it('switchFactory 只接受 scope 内工厂，越界忽略', () => {
    const s = useAccessScope()
    s.switchFactory('SITE-001')
    expect(s.currentFactoryId).toBe('SITE-001')
    s.switchFactory('SITE-999')
    expect(s.currentFactoryId).toBe('SITE-001')
  })

  it('workshop-lead persona（装配车间主任）：仅本车间产线，仅放行产线/车间屏', () => {
    const s = useAccessScope()
    s.setPersona('workshop-lead')
    expect(s.allowedScreens).toEqual(['line', 'workshop'])
    expect(s.canSeeScreen('factory')).toBe(false)
    expect(s.canSeeScreen('warehouse')).toBe(false)
    expect(s.currentFactoryId).toBe('SITE-001')
    // 可见车间收窄到 1 个（二车间 · 装配车间），可见产线 6 条（设定集 §2）
    expect(s.visibleWorkshops.length).toBe(1)
    expect(s.visibleWorkshops[0].id).toBe('WS-02')
    expect(s.visibleLines.every((l) => l.workshopId === 'WS-02')).toBe(true)
    expect(s.visibleLines).toHaveLength(6)
  })

  it('非法 persona id：setPersona 无操作，personaId 不变', () => {
    const s = useAccessScope()
    const originalPersonaId = s.personaId
    s.setPersona('nonexistent-id')
    expect(s.personaId).toBe(originalPersonaId)
    expect(s.personaId).toBe('plant-admin')
  })
})
