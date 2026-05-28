import { describe, expect, it } from 'vitest'
import {
  inferPartnerRole,
  matchesLinkedResourceSelectors,
  roleLabel,
  type ResourceHierarchyFilter,
  type ResourceScenarioRelations,
} from './masterDataPageHelpers'
import shockAbsorberFixture from '../../../../../../docs/superpowers/fixtures/2026-05-27-shock-absorber-master-data.json'

const relations: ResourceScenarioRelations = {
  'LINE-FRT-A': { siteCode: 'PLANT-NB' },
  'WC-TUBE-WELD': { siteCode: 'PLANT-NB', lineCode: 'LINE-FRT-A' },
  'EQ-WELD-01': { siteCode: 'PLANT-NB', lineCode: 'LINE-FRT-A', workCenterCode: 'WC-TUBE-WELD' },
}

describe('master data page helpers', () => {
  it('keeps non-hierarchical resource rows when hierarchy filters are selected', () => {
    const filter: ResourceHierarchyFilter = {
      siteCode: 'PLANT-NB',
      lineCode: 'LINE-FRT-A',
      workCenterCode: 'WC-TUBE-WELD',
    }

    expect(matchesLinkedResourceSelectors({ resourceType: 'shift', code: 'SHIFT-DAY' }, filter, relations)).toBe(true)
    expect(matchesLinkedResourceSelectors({ resourceType: 'work-calendar', code: 'CAL-SAD-STD' }, filter, relations)).toBe(true)
    expect(matchesLinkedResourceSelectors({ resourceType: 'team', code: 'TEAM-FRT-DAY' }, filter, relations)).toBe(true)
    expect(matchesLinkedResourceSelectors({ resourceType: 'personnel-skill', code: 'user-ops-01:WELD' }, filter, relations)).toBe(true)
  })

  it('matches hierarchical resource rows against selected factory, line, and work center', () => {
    const filter: ResourceHierarchyFilter = {
      siteCode: 'PLANT-NB',
      lineCode: 'LINE-FRT-A',
      workCenterCode: 'WC-TUBE-WELD',
    }

    expect(matchesLinkedResourceSelectors({ resourceType: 'site', code: 'PLANT-NB' }, filter, relations)).toBe(true)
    expect(matchesLinkedResourceSelectors({ resourceType: 'production-line', code: 'LINE-FRT-A' }, filter, relations)).toBe(true)
    expect(matchesLinkedResourceSelectors({ resourceType: 'work-center', code: 'WC-TUBE-WELD' }, filter, relations)).toBe(true)
    expect(matchesLinkedResourceSelectors({ resourceType: 'device-asset', code: 'EQ-WELD-01' }, filter, relations)).toBe(true)
    expect(matchesLinkedResourceSelectors({ resourceType: 'device-asset', code: 'EQ-MISSING' }, filter, relations)).toBe(false)
  })

  it('uses an explicit unknown partner role instead of reusing the all filter value', () => {
    expect(inferPartnerRole({ resourceType: 'business-partner', code: 'CUST-GAC' })).toBe('customer')
    expect(inferPartnerRole({ resourceType: 'business-partner', code: 'SUP-OIL-CC' })).toBe('supplier')
    expect(inferPartnerRole({ resourceType: 'business-partner', code: 'OEM-001' })).toBe('unknown')
    expect(roleLabel('unknown')).toBe('未分配')
  })

  it('keeps fixture work-center line references resolvable', () => {
    const lineCodes = new Set(shockAbsorberFixture.resources.productionLines.map((line) => line.code))

    expect(shockAbsorberFixture.resources.workCenters.every((workCenter) => lineCodes.has(workCenter.lineCode))).toBe(true)
  })
})
