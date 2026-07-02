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

describe('business console telemetry navigation', () => {
  it('groups formal telemetry pages under equipment monitoring', () => {
    const equipmentItems = DOMAIN_SIDE_NAV.equipment?.flatMap((section) => section.items) ?? []
    const paths = equipmentItems.map((item) => pathOf(item.to))

    expect(resolveDomainId('/equipment/telemetry/tags')).toBe('equipment')
    expect(resolveDomainId('/equipment/telemetry/alarm-rules')).toBe('equipment')
    expect(resolveDomainId('/equipment/telemetry/history')).toBe('equipment')
    expect(resolveDomainId('/equipment/telemetry/oee')).toBe('equipment')
    expect(paths).toContain('/equipment/telemetry/tags')
    expect(paths).toContain('/equipment/telemetry/alarm-rules')
    expect(paths).toContain('/equipment/telemetry/history')
    expect(paths).toContain('/equipment/telemetry/oee')
  })

  it('uses read permissions for telemetry analysis and manage permission for alarm-rule maintenance', () => {
    const equipmentItems = DOMAIN_SIDE_NAV.equipment?.flatMap((section) => section.items) ?? []
    const tags = equipmentItems.find((item) => pathOf(item.to) === '/equipment/telemetry/tags')
    const alarmRules = equipmentItems.find((item) => pathOf(item.to) === '/equipment/telemetry/alarm-rules')
    const history = equipmentItems.find((item) => pathOf(item.to) === '/equipment/telemetry/history')
    const oee = equipmentItems.find((item) => pathOf(item.to) === '/equipment/telemetry/oee')

    expect(tags?.requiredPermissions).toEqual([P.iiotTelemetryRead])
    expect(alarmRules?.requiredPermissions).toEqual([P.iiotAlarmsRead, P.iiotAlarmRulesManage])
    expect(history?.requiredPermissions).toEqual([P.iiotTelemetryRead])
    expect(oee?.requiredPermissions).toEqual([P.iiotTelemetryRead])
  })
})

function pathOf(to: unknown) {
  return typeof to === 'object' && to !== null && 'path' in to ? String(to.path) : ''
}
