import type { NavDomain, SideNav } from '@nerv-iip/app-shell'
import {
  ActivityIcon,
  BookMarkedIcon,
  BoxesIcon,
  Building2Icon,
  CalendarRangeIcon,
  ClipboardCheckIcon,
  CpuIcon,
  FactoryIcon,
  GitBranchIcon,
  LayoutDashboardIcon,
  PackageIcon,
  PackageSearchIcon,
  ReceiptTextIcon,
  UsersRoundIcon,
  WarehouseIcon,
} from 'lucide-vue-next'

/**
 * Business Console navigation model (T-shaped). Source of truth = the capability
 * catalog + current visible scope in docs/architecture/frontend-navigation-map.md.
 *
 * Only route-ready domains/pages are listed; new large domains (ERP sales/finance,
 * WMS pages, BarcodeLabel, Approval, Telemetry rules, Maintenance pages) must clear
 * the menu upgrade gate before being added here. `requiredPermissions` is the RBAC
 * hook — left unset for now (permissive default); the actual permission codes are
 * attached per domain/page when wired. Gateway enforcement stays authoritative.
 */

export const WORKBENCH_DOMAIN_ID = 'workbench'

/** Top capability areas (the horizontal "top" of the T), in display order. */
export const BUSINESS_DOMAINS: NavDomain[] = [
  { id: 'workbench', title: '数字化工作台', icon: LayoutDashboardIcon, to: { path: '/' } },
  { id: 'master-data', title: '基础数据', icon: BoxesIcon, to: { path: '/master-data/skus' } },
  { id: 'engineering', title: '产品工程', icon: GitBranchIcon, to: { path: '/engineering' } },
  { id: 'planning', title: '需求与计划', icon: CalendarRangeIcon, to: { path: '/planning' } },
  { id: 'mes', title: '制造执行', icon: FactoryIcon, to: { path: '/mes' } },
  { id: 'quality', title: '质量管理', icon: ClipboardCheckIcon, to: { path: '/quality/inspections' } },
  { id: 'inventory', title: '库存管理', icon: PackageSearchIcon, to: { path: '/inventory/availability' } },
  { id: 'wms', title: '仓储作业', icon: WarehouseIcon, to: { path: '/wms/inbound' } },
  { id: 'erp', title: '经营管理', icon: ReceiptTextIcon, to: { path: '/erp' } },
  { id: 'equipment', title: '设备监控', icon: ActivityIcon, to: { path: '/equipment' } },
]

/** Domain-local side navigation (the left of the T), per domain id. */
export const DOMAIN_SIDE_NAV: Record<string, SideNav> = {
  'workbench': [{ items: [{ title: '工作台首页', to: { path: '/' } }] }],
  'master-data': [
    {
      items: [
        { title: '物料与产品', icon: PackageIcon, to: { path: '/master-data/skus' } },
        { title: '客户与供应商', icon: Building2Icon, to: { path: '/master-data/partners' } },
        { title: '工厂与产线', icon: FactoryIcon, to: { path: '/master-data/facilities' } },
        { title: '设备台账', icon: CpuIcon, to: { path: '/master-data/devices' } },
        { title: '数据字典', icon: BookMarkedIcon, to: { path: '/master-data/reference-data' } },
        { title: '组织与人员', icon: UsersRoundIcon, to: { path: '/master-data/organization' } },
      ],
    },
  ],
  'engineering': [
    {
      items: [
        { title: '工程版本', to: { path: '/engineering' } },
      ],
    },
  ],
  'planning': [{ items: [{ title: '需求与物料计划', to: { path: '/planning' } }] }],
  'erp': [{ items: [{ title: '采购与供应', to: { path: '/erp' } }] }],
  'mes': [
    {
      label: '计划与工单',
      items: [
        { title: '生产驾驶舱', to: { path: '/mes' } },
        { title: '生产计划', to: { path: '/mes/plans' } },
        { title: '工单与派工', to: { path: '/mes/work-orders' } },
        { title: '派工看板', to: { path: '/mes/dispatch' } },
      ],
    },
    {
      label: '执行与齐套',
      items: [
        { title: '齐套与物料', to: { path: '/mes/materials' } },
        { title: '工序执行', to: { path: '/mes/operation-tasks' } },
        { title: '在制跟踪', to: { path: '/mes/wip' } },
      ],
    },
    {
      label: '报工与完工',
      items: [
        { title: '报工记录', to: { path: '/mes/production-reports' } },
        { title: '报工与完工汇总', to: { path: '/mes/reports' } },
        { title: '完工入库', to: { path: '/mes/receipts' } },
      ],
    },
    {
      label: '异常与协同',
      items: [
        { title: '质量与不良', to: { path: '/mes/quality' } },
        { title: '设备与停机', to: { path: '/mes/downtime' } },
        { title: '异常与产能', to: { path: '/mes/capacity' } },
        { title: '规则排程', to: { path: '/mes/schedules' } },
        { title: '班次交接', to: { path: '/mes/handovers' } },
      ],
    },
    {
      label: '追溯与诊断',
      items: [
        { title: '追溯查询', to: { path: '/mes/traceability' } },
        { title: '生产准备检查', to: { path: '/mes/foundation' } },
      ],
    },
  ],
  'quality': [
    {
      items: [
        { title: '检验任务与记录', to: { path: '/quality/inspections' } },
        { title: '不合格品处理', to: { path: '/quality/ncrs' } },
      ],
    },
  ],
  'inventory': [
    {
      items: [
        { title: '库存可用量', to: { path: '/inventory/availability' } },
        { title: '库存移动', to: { path: '/inventory/movements' } },
        { title: '库存盘点', to: { path: '/inventory/counts' } },
      ],
    },
  ],
  'wms': [
    {
      items: [
        { title: '收货入库', to: { path: '/wms/inbound' } },
        { title: '出库发货', to: { path: '/wms/outbound' } },
        { title: 'WCS 任务', to: { path: '/wms/wcs' } },
      ],
    },
  ],
  'equipment': [
    {
      items: [
        { title: '设备运行看板', to: { path: '/equipment' } },
        { title: '设备报警', to: { path: '/equipment/alarms' } },
      ],
    },
  ],
}

/** True when `path` is exactly `base` or a descendant route of it (segment-boundary safe). */
function isUnder(path: string, base: string): boolean {
  return path === base || path.startsWith(`${base}/`)
}

/** Resolve the active top domain id from a route path. */
export function resolveDomainId(path: string): string {
  if (path === '/' || path === '') return 'workbench'
  // 工艺与版本 lives under /master-data/process but belongs to 产品工程.
  if (isUnder(path, '/master-data/process')) return 'engineering'
  if (isUnder(path, '/master-data')) return 'master-data'
  if (isUnder(path, '/engineering')) return 'engineering'
  if (isUnder(path, '/planning')) return 'planning'
  if (isUnder(path, '/erp')) return 'erp'
  if (isUnder(path, '/mes')) return 'mes'
  if (isUnder(path, '/quality')) return 'quality'
  if (isUnder(path, '/inventory')) return 'inventory'
  if (isUnder(path, '/wms')) return 'wms'
  if (isUnder(path, '/equipment')) return 'equipment'
  return 'workbench'
}

/** Keep entries the principal may see. Unset `requiredPermissions` = always visible. */
export function permittedBy<T extends { requiredPermissions?: string[] }>(
  entries: T[],
  permissionCodes: string[] | undefined,
): T[] {
  const codes = permissionCodes ?? []
  return entries.filter(
    (e) => !e.requiredPermissions?.length || e.requiredPermissions.some((p) => codes.includes(p)),
  )
}
