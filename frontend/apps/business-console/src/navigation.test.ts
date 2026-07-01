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

function pathOf(to: unknown) {
  return typeof to === 'object' && to !== null && 'path' in to ? String(to.path) : ''
}
