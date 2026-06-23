import type { NavDomain, SideNav } from '@nerv-iip/app-shell'
import {
  ActivityIcon,
  ArrowRightLeftIcon,
  AwardIcon,
  BellRingIcon,
  BookMarkedIcon,
  BoxesIcon,
  BoxIcon,
  Building2Icon,
  CalendarClockIcon,
  CalendarCogIcon,
  CalendarRangeIcon,
  CheckCheckIcon,
  ClipboardCheckIcon,
  ClipboardListIcon,
  CpuIcon,
  FactoryIcon,
  FileTextIcon,
  FolderTreeIcon,
  GaugeIcon,
  GitBranchIcon,
  GitForkIcon,
  GitPullRequestIcon,
  GraduationCapIcon,
  HashIcon,
  LayersIcon,
  LayoutDashboardIcon,
  NetworkIcon,
  PackageCheckIcon,
  PackageIcon,
  PackageSearchIcon,
  PlayIcon,
  ReceiptTextIcon,
  RouteIcon,
  RulerIcon,
  SearchIcon,
  ShieldAlertIcon,
  ShieldCheckIcon,
  TrendingUpIcon,
  UserCheckIcon,
  UsersRoundIcon,
  WarehouseIcon,
  WrenchIcon,
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
  { id: 'engineering', title: '产品工程', icon: GitBranchIcon, to: { path: '/engineering/production-versions' } },
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
  'workbench': [{ items: [{ title: '工作台首页', icon: LayoutDashboardIcon, to: { path: '/' } }] }],
  'master-data': [
    {
      label: '主对象',
      items: [
        { title: '物料与产品', icon: PackageIcon, to: { path: '/master-data/skus' } },
        { title: '业务伙伴', icon: Building2Icon, to: { path: '/master-data/partners' } },
        { title: '工厂结构', icon: FactoryIcon, to: { path: '/master-data/facilities' } },
        { title: '设备台账', icon: CpuIcon, to: { path: '/master-data/devices' } },
      ],
    },
    {
      label: '组织与排班',
      items: [
        { title: '组织与班组', icon: UsersRoundIcon, to: { path: '/master-data/organization' } },
        { title: '排班与日历', icon: CalendarRangeIcon, to: { path: '/master-data/scheduling' } },
        { title: '技能目录', icon: GraduationCapIcon, to: { path: '/master-data/skill-catalog' } },
        { title: '人员技能', icon: AwardIcon, to: { path: '/master-data/skills' } },
      ],
    },
    {
      label: '受控数据',
      items: [
        { title: '产品分类', icon: FolderTreeIcon, to: { path: '/master-data/product-categories' } },
        { title: '计量单位', icon: RulerIcon, to: { path: '/master-data/units' } },
        { title: '数据字典', icon: BookMarkedIcon, to: { path: '/master-data/reference-data' } },
        { title: '编码规则', icon: HashIcon, to: { path: '/master-data/code-rules' } },
      ],
    },
  ],
  'engineering': [
    {
      label: '物料与结构',
      items: [
        { title: '工程物料', icon: BoxIcon, to: { path: '/engineering/items' } },
        { title: '设计 BOM', icon: NetworkIcon, to: { path: '/engineering/ebom' } },
        { title: '制造 BOM', icon: GitForkIcon, to: { path: '/engineering/mbom' } },
      ],
    },
    {
      label: '工艺与版本',
      items: [
        { title: '标准工序', icon: WrenchIcon, to: { path: '/engineering/standard-operations' } },
        { title: '工艺路线', icon: RouteIcon, to: { path: '/engineering/routings' } },
        { title: '生产版本', icon: LayersIcon, to: { path: '/engineering/production-versions' } },
      ],
    },
    {
      label: '变更与文档',
      items: [
        { title: '工程变更', icon: GitPullRequestIcon, to: { path: '/engineering/eco' } },
        { title: '工程文档', icon: FileTextIcon, to: { path: '/engineering/documents' } },
      ],
    },
  ],
  'planning': [{ items: [{ title: '需求与物料计划', icon: CalendarRangeIcon, to: { path: '/planning' } }] }],
  'erp': [{ items: [{ title: '采购与供应', icon: ReceiptTextIcon, to: { path: '/erp' } }] }],
  'mes': [
    {
      label: '计划与工单',
      items: [
        { title: '生产驾驶舱', icon: GaugeIcon, to: { path: '/mes' } },
        { title: '生产计划', icon: CalendarRangeIcon, to: { path: '/mes/plans' } },
        { title: '工单与派工', icon: ClipboardListIcon, to: { path: '/mes/work-orders' } },
        { title: '派工看板', icon: UserCheckIcon, to: { path: '/mes/dispatch' } },
      ],
    },
    {
      label: '执行与齐套',
      items: [
        { title: '领料与齐套', icon: PackageIcon, to: { path: '/mes/materials' } },
        { title: '工序执行', icon: PlayIcon, to: { path: '/mes/operation-tasks' } },
        { title: '在制跟踪', icon: ActivityIcon, to: { path: '/mes/wip' } },
      ],
    },
    {
      label: '报工与完工',
      items: [
        { title: '报工记录', icon: ClipboardCheckIcon, to: { path: '/mes/production-reports' } },
        { title: '完工入库', icon: PackageCheckIcon, to: { path: '/mes/receipts' } },
      ],
    },
    {
      label: '异常与协同',
      items: [
        { title: '质量与不良', icon: ShieldCheckIcon, to: { path: '/mes/quality' } },
        { title: '设备与停机', icon: WrenchIcon, to: { path: '/mes/downtime' } },
        { title: '异常与产能', icon: TrendingUpIcon, to: { path: '/mes/capacity' } },
        { title: '规则排程', icon: CalendarCogIcon, to: { path: '/mes/schedules' } },
        { title: '班次交接', icon: ArrowRightLeftIcon, to: { path: '/mes/handovers' } },
      ],
    },
    {
      label: '追溯与诊断',
      items: [
        { title: '追溯查询', icon: SearchIcon, to: { path: '/mes/traceability' } },
        { title: '生产准备检查', icon: CheckCheckIcon, to: { path: '/mes/foundation' } },
      ],
    },
  ],
  'quality': [
    {
      items: [
        { title: '检验任务与记录', icon: ClipboardCheckIcon, to: { path: '/quality/inspections' } },
        { title: '不合格品处理', icon: ShieldAlertIcon, to: { path: '/quality/ncrs' } },
        { title: '原因码目录', icon: HashIcon, to: { path: '/quality/reason-codes' } },
      ],
    },
  ],
  'inventory': [
    {
      items: [
        { title: '库存可用量', icon: PackageSearchIcon, to: { path: '/inventory/availability' } },
        { title: '库存移动', icon: ArrowRightLeftIcon, to: { path: '/inventory/movements' } },
        { title: '库存盘点', icon: ClipboardListIcon, to: { path: '/inventory/counts' } },
      ],
    },
  ],
  'wms': [
    {
      items: [
        { title: '收货入库', icon: PackageCheckIcon, to: { path: '/wms/inbound' } },
        { title: '上架任务', icon: LayersIcon, to: { path: '/wms/putaway' } },
        { title: '出库发货', icon: PackageIcon, to: { path: '/wms/outbound' } },
        { title: '拣货任务', icon: PackageSearchIcon, to: { path: '/wms/picking' } },
        { title: 'WCS 任务', icon: CpuIcon, to: { path: '/wms/wcs' } },
        { title: '盘点执行', icon: ClipboardCheckIcon, to: { path: '/wms/counts' } },
      ],
    },
  ],
  'equipment': [
    {
      label: '运行监控',
      items: [
        { title: '设备运行看板', icon: ActivityIcon, to: { path: '/equipment' } },
        { title: '设备报警', icon: BellRingIcon, to: { path: '/equipment/alarms' } },
      ],
    },
    {
      label: '维护保养',
      items: [
        { title: '维护工单', icon: WrenchIcon, to: { path: '/maintenance/work-orders' } },
        { title: '保养计划', icon: CalendarClockIcon, to: { path: '/maintenance/plans' } },
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
  if (isUnder(path, '/master-data')) return 'master-data'
  if (isUnder(path, '/engineering')) return 'engineering'
  if (isUnder(path, '/planning')) return 'planning'
  if (isUnder(path, '/erp')) return 'erp'
  if (isUnder(path, '/mes')) return 'mes'
  if (isUnder(path, '/quality')) return 'quality'
  if (isUnder(path, '/inventory')) return 'inventory'
  if (isUnder(path, '/wms')) return 'wms'
  if (isUnder(path, '/equipment')) return 'equipment'
  if (isUnder(path, '/maintenance')) return 'equipment'
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
