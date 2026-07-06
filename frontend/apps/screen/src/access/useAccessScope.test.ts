import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it } from 'vitest'
import { useAccessScope } from './useAccessScope'

describe('useAccessScope', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('默认 plant-admin：可见全部大屏与两个工厂', () => {
    const s = useAccessScope()
    expect(s.allowedScreens).toEqual(['factory', 'equipment', 'line'])
    expect(s.factories.map((f) => f.id)).toEqual(['F01', 'F02'])
    expect(s.canSeeScreen('equipment')).toBe(true)
  })

  it('switchFactory 只接受 scope 内工厂，越界忽略', () => {
    const s = useAccessScope()
    s.switchFactory('F02')
    expect(s.currentFactoryId).toBe('F02')
    s.switchFactory('F99')
    expect(s.currentFactoryId).toBe('F02')
  })

  it('workshop-lead persona：仅本车间产线，仅放行 line 屏', () => {
    const s = useAccessScope()
    // 先切换至 F02，验证初始非 F01
    s.switchFactory('F02')
    expect(s.currentFactoryId).toBe('F02')
    // 再切到 workshop-lead，验证 setPersona 触发 factory reset
    s.setPersona('workshop-lead')
    expect(s.allowedScreens).toEqual(['line'])
    expect(s.canSeeScreen('factory')).toBe(false)
    expect(s.currentFactoryId).toBe('F01')
    // 可见车间收窄到 1 个，且可见产线均属 WS-BATTERY
    expect(s.visibleWorkshops.length).toBe(1)
    expect(s.visibleWorkshops[0].id).toBe('WS-BATTERY')
    expect(s.visibleLines.every((l) => l.workshopId === 'WS-BATTERY')).toBe(true)
    expect(s.visibleLines.length).toBeGreaterThan(0)
  })

  it('非法 persona id：setPersona 无操作，personaId 不变', () => {
    const s = useAccessScope()
    const originalPersonaId = s.personaId
    s.setPersona('nonexistent-id')
    expect(s.personaId).toBe(originalPersonaId)
    expect(s.personaId).toBe('plant-admin')
  })
})
