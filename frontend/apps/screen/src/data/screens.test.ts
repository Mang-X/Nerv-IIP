import { describe, expect, it } from 'vitest'
import { SCREENS, screenForPath } from './screens'

describe('screens 注册表 + screenForPath', () => {
  it('三块大屏均注册，route 唯一', () => {
    expect(SCREENS.map((s) => s.key).sort()).toEqual(['equipment', 'factory', 'line'])
    const routes = SCREENS.map((s) => s.route)
    expect(new Set(routes).size).toBe(routes.length)
  })

  it('screenForPath 命中大屏路由与其子路由', () => {
    expect(screenForPath('/factory')).toBe('factory')
    expect(screenForPath('/equipment')).toBe('equipment')
    expect(screenForPath('/line')).toBe('line')
    expect(screenForPath('/line/LN-BAT-1')).toBe('line')
    expect(screenForPath('/')).toBeUndefined()
    expect(screenForPath('/login')).toBeUndefined()
  })
})
