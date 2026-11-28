import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import type { NavLink, SideNav } from '@nerv-iip/app-shell'
import { workbenchSideNav } from './navigation/workbench'
import { masterDataSideNav } from './navigation/master-data'
import { engineeringSideNav } from './navigation/engineering'
import { planningSideNav } from './navigation/planning'
import { erpSideNav } from './navigation/erp'
import { mesSideNav, mesViewGroups } from './navigation/mes'
import { qualitySideNav } from './navigation/quality'
import { inventorySideNav } from './navigation/inventory'
import { wmsSideNav } from './navigation/wms'
import { barcodeSideNav } from './navigation/barcode'
import { equipmentSideNav } from './navigation/equipment'
import { approvalSideNav } from './navigation/approval'
import { opsSideNav } from './navigation/ops'

export { BUSINESS_DOMAINS, WORKBENCH_DOMAIN_ID } from './navigation/domains'

/** Domain-local side navigation, in display order. */
export const DOMAIN_SIDE_NAV: Record<string, SideNav> = {
  workbench: workbenchSideNav,
  'master-data': masterDataSideNav,
  engineering: engineeringSideNav,
  planning: planningSideNav,
  erp: erpSideNav,
  mes: mesSideNav,
  quality: qualitySideNav,
  inventory: inventorySideNav,
  wms: wmsSideNav,
  barcode: barcodeSideNav,
  equipment: equipmentSideNav,
  approval: approvalSideNav,
  ops: opsSideNav,
}

/** True when `path` is exactly `base` or a descendant route of it (segment-boundary safe). */
function isUnder(path: string, base: string): boolean {
  return path === base || path.startsWith(`${base}/`)
}

/** Resolve the active top domain id from a route path. */
export function resolveDomainId(path: string): string {
  if (path === '/' || path === '') return 'workbench'
  if (isUnder(path, '/master-data')) return 'master-data'
  if (isUnder(path, '/engineering')) return 'engineering'
  if (isUnder(path, '/planning')) return 'planning'
  if (isUnder(path, '/scheduling')) return 'planning'
  if (isUnder(path, '/erp')) return 'erp'
  if (isUnder(path, '/mes')) return 'mes'
  if (isUnder(path, '/quality')) return 'quality'
  if (isUnder(path, '/inventory')) return 'inventory'
  if (isUnder(path, '/wms')) return 'wms'
  if (isUnder(path, '/barcode')) return 'barcode'
  if (isUnder(path, '/equipment')) return 'equipment'
  if (isUnder(path, '/maintenance')) return 'equipment'
  if (isUnder(path, '/approval')) return 'approval'
  if (isUnder(path, '/ops')) return 'ops'
  return 'workbench'
}

/** Keep entries the principal may see. Undefined codes means the caller has no loaded principal yet. */
export function permittedBy<T extends { requiredPermissions?: string[] }>(
  entries: T[],
  permissionCodes: string[] | undefined,
): T[] {
  const codes = new Set(permissionCodes)
  return entries.filter(
    (e) => !e.requiredPermissions?.length || e.requiredPermissions.some((p) => codes.has(p)),
  )
}

export function sideNavForDomain(domainId: string, permissionCodes: string[] | undefined): SideNav {
  // 仅有看板或检查读权限时，保留该视图的侧栏入口和原有排序。
  const foldedViews =
    domainId === 'mes'
      ? mesViewGroups
          .filter(({ entry }) => permittedBy([entry], permissionCodes).length > 0)
          .map<NavLink>(({ alternate }) => alternate)
      : []

  return (DOMAIN_SIDE_NAV[domainId] ?? [])
    .map((group) => ({
      ...group,
      items: permittedBy(group.items, permissionCodes).filter(
        (item) => !foldedViews.includes(item),
      ),
    }))
    .filter((group) => group.items.length > 0)
}

export function mesViewsForPath(path: string, permissionCodes: string[] | undefined) {
  const group = mesViewGroups.find(
    ({ entry, alternate }) => entry.to.path === path || alternate.to.path === path,
  )
  if (!group) return []

  return permittedBy(
    [{ ...group.entry, title: group.entryViewLabel }, group.alternate],
    permissionCodes,
  )
}

const jobHomePages = [
  [P.erpProcurementManage, P.erpProcurementRead, '/erp'],
  [P.erpSalesManage, P.erpSalesRead, '/erp/sales'],
  [P.erpFinanceManage, P.erpFinanceRead, '/erp/finance'],
  ['business.planning.demands.manage', P.planningDemandsRead, '/planning'],
  [P.mesDowntimeManage, P.mesDowntimeRead, '/mes/downtime'],
  [P.maintenanceWorkOrdersManage, P.maintenanceWorkOrdersRead, '/maintenance/work-orders'],
] as const

export function jobHomePath(canAll: (codes: readonly string[]) => boolean) {
  const homes = jobHomePages.filter(([manage, read]) => canAll([manage, read]))
  return homes.length === 1 ? homes[0]![2] : '/'
}
