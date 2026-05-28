import type { BusinessConsoleResourceItem } from '@nerv-iip/api-client'

export type PartnerRole = 'all' | 'customer' | 'supplier' | 'unknown'

export type ResourceHierarchyFilter = {
  siteCode: string
  lineCode: string
  workCenterCode: string
}

export type ResourceScenarioRelations = Record<string, {
  siteCode?: string
  lineCode?: string
  workCenterCode?: string
  shiftCode?: string
}>

const NON_HIERARCHICAL_RESOURCE_TYPES = new Set(['shift', 'work-calendar', 'team', 'personnel-skill'])

export function matchesLinkedResourceSelectors(
  row: BusinessConsoleResourceItem,
  appliedFilter: ResourceHierarchyFilter,
  scenarioRelations: ResourceScenarioRelations,
) {
  if (row.resourceType && NON_HIERARCHICAL_RESOURCE_TYPES.has(row.resourceType)) return true

  const relation = row.code ? scenarioRelations[row.code] : undefined

  if (appliedFilter.siteCode !== 'all') {
    if (row.resourceType === 'site') return row.code === appliedFilter.siteCode
    if (relation?.siteCode !== appliedFilter.siteCode) return false
  }

  if (appliedFilter.lineCode !== 'all') {
    if (row.resourceType === 'production-line') return row.code === appliedFilter.lineCode
    if (relation?.lineCode !== appliedFilter.lineCode) return false
  }

  if (appliedFilter.workCenterCode !== 'all') {
    if (row.resourceType === 'work-center') return row.code === appliedFilter.workCenterCode
    if (relation?.workCenterCode !== appliedFilter.workCenterCode) return false
  }

  return true
}

export function inferPartnerRole(row: BusinessConsoleResourceItem): PartnerRole {
  // Temporary fallback until BusinessGateway returns an explicit partner role field.
  const code = row.code?.toLowerCase() ?? ''
  if (code.includes('cust')) return 'customer'
  if (code.includes('sup')) return 'supplier'
  return 'unknown'
}

export function roleLabel(role: PartnerRole) {
  if (role === 'customer') return '客户'
  if (role === 'supplier') return '供应商'
  return '未分配'
}
