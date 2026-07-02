import { describe, expect, it } from 'vitest'

import { DOMAIN_SIDE_NAV, resolveDomainId } from './navigation'
import { BUSINESS_PERMISSION_CODES as P } from './permissions'

describe('business console scheduling navigation', () => {
  it('places the APS workbench under demand and planning', () => {
    const planningItems = DOMAIN_SIDE_NAV.planning?.flatMap((section) => section.items) ?? []
    const scheduling = planningItems.find((item) => pathOf(item.to) === '/scheduling')

    expect(resolveDomainId('/scheduling')).toBe('planning')
    expect(scheduling?.title).toBe('排产工作台')
    expect(scheduling?.requiredPermissions).toEqual([P.schedulingPlansRead])
  })
})

describe('business console maintenance navigation', () => {
  it('keeps CMMS deep pages under the equipment domain side navigation', () => {
    const equipmentItems = DOMAIN_SIDE_NAV.equipment?.flatMap((section) => section.items) ?? []
    const maintenancePaths = equipmentItems.map((item) => pathOf(item.to)).filter((path) => path.startsWith('/maintenance'))

    expect(resolveDomainId('/maintenance/inspections')).toBe('equipment')
    expect(resolveDomainId('/maintenance/spare-parts')).toBe('equipment')
    expect(resolveDomainId('/maintenance/reliability')).toBe('equipment')
    expect(resolveDomainId('/maintenance/availability')).toBe('equipment')
    expect(maintenancePaths).toEqual([
      '/maintenance/work-orders',
      '/maintenance/plans',
      '/maintenance/inspections',
      '/maintenance/spare-parts',
      '/maintenance/reliability',
      '/maintenance/availability',
    ])
  })
})

function pathOf(to: unknown) {
  return typeof to === 'object' && to !== null && 'path' in to ? String(to.path) : ''
}
