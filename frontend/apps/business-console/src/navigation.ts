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

export function sideNavForDomain(
  domainId: string,
  permissionCodes: string[] | undefined,
  roleIds: readonly string[] = [],
): SideNav {
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
        (item) => !foldedViews.includes(item) && isJobMenuPath(navPath(item), roleIds),
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

type Job = {
  home: string
  includes: (path: string) => boolean
}

function job(home: string, includes: (path: string) => boolean): Job {
  return { home, includes }
}

function domainJob(home: string, ...bases: string[]): Job {
  return job(home, (path) => bases.some((base) => isUnder(path, base)))
}

const jobs = new Map<string, Job>([
  [
    'role-erp-procurement',
    job('/erp', (path) => path === '/erp' || isUnder(path, '/erp/procurement')),
  ],
  ['role-erp-sales', domainJob('/erp/sales', '/erp/sales')],
  ['role-erp-finance', domainJob('/erp/finance', '/erp/finance')],
  ['role-production-planner', domainJob('/planning', '/planning', '/scheduling')],
  ['role-pda-warehouse', domainJob('/wms/inbound', '/wms', '/inventory')],
  ['role-pda-operator', domainJob('/mes/operation-tasks', '/mes')],
  ['role-mes-downtime-team-leader', job('/mes/downtime', (path) => path === '/mes/downtime')],
  [
    'role-pda-inspector',
    job('/quality/inspection-tasks', (path) =>
      ['/quality/inspection-tasks', '/quality/inspections'].includes(path),
    ),
  ],
  ['role-quality-manager', domainJob('/quality/ncrs', '/quality')],
  ['role-maintenance-technician', domainJob('/maintenance/work-orders', '/maintenance')],
  ['role-masterdata-admin', domainJob('/master-data/skus', '/master-data')],
])

function activeJobs(roleIds: readonly string[]) {
  return [...new Set(roleIds)].flatMap((id) => {
    const assignment = jobs.get(id)
    return assignment ? [assignment] : []
  })
}

function navPath({ to }: NavLink): string | undefined {
  if (typeof to === 'string') return to
  return 'path' in to ? to.path : undefined
}

function isJobMenuPath(path: string | undefined, roleIds: readonly string[]) {
  if (roleIds.includes('role-platform-admin')) return true
  const assignments = activeJobs(roleIds)
  if (path === '/' && assignments.length > 1) return true
  return (
    assignments.length === 0 ||
    (path !== undefined && assignments.some((assignment) => assignment.includes(path)))
  )
}

export function resolveBusinessEntry(
  roleIds: readonly string[] = [],
  permissionCodes?: string[],
  domainId?: string,
  canOpen: (path: string) => boolean = () => true,
): string | undefined {
  const assignments = activeJobs(roleIds)
  const preferred =
    !roleIds.includes('role-platform-admin') && assignments.length === 1
      ? assignments[0]?.home
      : undefined
  const domain = domainId ?? (preferred ? resolveDomainId(preferred) : 'workbench')
  const paths = sideNavForDomain(domain, permissionCodes, roleIds)
    .flatMap(({ items }) => items.map(navPath))
    .filter((path): path is string => path !== undefined && canOpen(path))
  if (preferred && paths.includes(preferred)) return preferred
  if (!domainId && !preferred) return '/'
  return paths[0]
}
