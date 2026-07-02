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
  FileCheck2Icon,
  FolderTreeIcon,
  GaugeIcon,
  GitBranchIcon,
  GitForkIcon,
  GitPullRequestIcon,
  GraduationCapIcon,
  HandshakeIcon,
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
  WalletIcon,
  WarehouseIcon,
  WrenchIcon,
} from 'lucide-vue-next'
import { BUSINESS_DOMAIN_PERMISSIONS, BUSINESS_PERMISSION_CODES as P } from '@/permissions'

/**
 * Business Console navigation model (T-shaped). Source of truth = the capability
 * catalog + current visible scope in docs/architecture/frontend-navigation-map.md.
 *
 * Only route-ready domains/pages are listed; new large domains must clear the menu
 * upgrade gate before being added here. `requiredPermissions` mirrors the
 * BusinessGateway/IAM catalog for RBAC trimming. Gateway enforcement stays
 * authoritative for every request.
 */

export const WORKBENCH_DOMAIN_ID = 'workbench'

/** Top capability areas (the horizontal "top" of the T), in display order. */
export const BUSINESS_DOMAINS: NavDomain[] = [
  { id: 'workbench', title: '数字化工作台', icon: LayoutDashboardIcon, to: { path: '/' }, requiredPermissions: [...BUSINESS_DOMAIN_PERMISSIONS.workbench] },
  { id: 'master-data', title: '基础数据', icon: BoxesIcon, to: { path: '/master-data/skus' }, requiredPermissions: [...BUSINESS_DOMAIN_PERMISSIONS.masterData] },
  { id: 'engineering', title: '产品工程', icon: GitBranchIcon, to: { path: '/engineering/production-versions' }, requiredPermissions: [...BUSINESS_DOMAIN_PERMISSIONS.engineering] },
  { id: 'planning', title: '需求与计划', icon: CalendarRangeIcon, to: { path: '/planning' }, requiredPermissions: [...BUSINESS_DOMAIN_PERMISSIONS.planning] },
  { id: 'mes', title: '制造执行', icon: FactoryIcon, to: { path: '/mes' }, requiredPermissions: [...BUSINESS_DOMAIN_PERMISSIONS.mes] },
  { id: 'quality', title: '质量管理', icon: ClipboardCheckIcon, to: { path: '/quality/inspections' }, requiredPermissions: [...BUSINESS_DOMAIN_PERMISSIONS.quality] },
  { id: 'inventory', title: '库存管理', icon: PackageSearchIcon, to: { path: '/inventory/availability' }, requiredPermissions: [...BUSINESS_DOMAIN_PERMISSIONS.inventory] },
  { id: 'wms', title: '仓储作业', icon: WarehouseIcon, to: { path: '/wms/inbound' }, requiredPermissions: [...BUSINESS_DOMAIN_PERMISSIONS.wms] },
  { id: 'erp', title: '经营管理', icon: ReceiptTextIcon, to: { path: '/erp' }, requiredPermissions: [...BUSINESS_DOMAIN_PERMISSIONS.erp] },
  { id: 'barcode', title: '条码标签', icon: HashIcon, to: { path: '/barcode/rules' }, requiredPermissions: [...BUSINESS_DOMAIN_PERMISSIONS.barcode] },
  { id: 'equipment', title: '设备监控', icon: ActivityIcon, to: { path: '/equipment' }, requiredPermissions: [...BUSINESS_DOMAIN_PERMISSIONS.equipment] },
  { id: 'approval', title: '审批中心', icon: FileCheck2Icon, to: { path: '/approval' }, requiredPermissions: [...BUSINESS_DOMAIN_PERMISSIONS.approval] },
]

/** Domain-local side navigation (the left of the T), per domain id. */
export const DOMAIN_SIDE_NAV: Record<string, SideNav> = {
  'workbench': [{ items: [{ title: '工作台首页', icon: LayoutDashboardIcon, to: { path: '/' }, requiredPermissions: [...BUSINESS_DOMAIN_PERMISSIONS.workbench] }] }],
  'master-data': [
    {
      label: '主对象',
      items: [
        { title: '物料与产品', icon: PackageIcon, to: { path: '/master-data/skus' }, requiredPermissions: [P.masterDataProductsRead] },
        { title: '业务伙伴', icon: Building2Icon, to: { path: '/master-data/partners' }, requiredPermissions: [P.masterDataResourcesRead] },
        { title: '工厂结构', icon: FactoryIcon, to: { path: '/master-data/facilities' }, requiredPermissions: [P.masterDataResourcesRead] },
        { title: '设备台账', icon: CpuIcon, to: { path: '/master-data/devices' }, requiredPermissions: [P.masterDataResourcesRead] },
      ],
    },
    {
      label: '组织与排班',
      items: [
        { title: '组织与班组', icon: UsersRoundIcon, to: { path: '/master-data/organization' }, requiredPermissions: [P.masterDataResourcesRead] },
        { title: '排班与日历', icon: CalendarRangeIcon, to: { path: '/master-data/scheduling' }, requiredPermissions: [P.masterDataResourcesRead] },
        { title: '技能目录', icon: GraduationCapIcon, to: { path: '/master-data/skill-catalog' }, requiredPermissions: [P.masterDataResourcesRead] },
        { title: '人员技能', icon: AwardIcon, to: { path: '/master-data/skills' }, requiredPermissions: [P.masterDataResourcesRead] },
      ],
    },
    {
      label: '受控数据',
      items: [
        { title: '产品分类', icon: FolderTreeIcon, to: { path: '/master-data/product-categories' }, requiredPermissions: [P.masterDataProductsRead] },
        { title: '计量单位', icon: RulerIcon, to: { path: '/master-data/units' }, requiredPermissions: [P.masterDataProductsRead] },
        { title: '数据字典', icon: BookMarkedIcon, to: { path: '/master-data/reference-data' }, requiredPermissions: [P.masterDataResourcesRead] },
        { title: '编码规则', icon: HashIcon, to: { path: '/master-data/code-rules' }, requiredPermissions: [P.masterDataResourcesRead] },
      ],
    },
  ],
  'engineering': [
    {
      label: '物料与结构',
      items: [
        { title: '工程物料', icon: BoxIcon, to: { path: '/engineering/items' }, requiredPermissions: [P.engineeringItemsRead] },
        { title: '设计 BOM', icon: NetworkIcon, to: { path: '/engineering/ebom' }, requiredPermissions: [P.engineeringBomsRead] },
        { title: '制造 BOM', icon: GitForkIcon, to: { path: '/engineering/mbom' }, requiredPermissions: [P.engineeringBomsRead] },
        { title: 'BOM 分析', icon: FolderTreeIcon, to: { path: '/engineering/bom-analysis' }, requiredPermissions: [P.engineeringBomsRead] },
      ],
    },
    {
      label: '工艺与版本',
      items: [
        { title: '标准工序', icon: WrenchIcon, to: { path: '/engineering/standard-operations' }, requiredPermissions: [P.engineeringStandardOperationsRead] },
        { title: '工艺路线', icon: RouteIcon, to: { path: '/engineering/routings' }, requiredPermissions: [P.engineeringRoutingsRead] },
        { title: '生产版本', icon: LayersIcon, to: { path: '/engineering/production-versions' }, requiredPermissions: [P.engineeringProductionVersionsRead] },
      ],
    },
    {
      label: '变更与文档',
      items: [
        { title: '工程变更', icon: GitPullRequestIcon, to: { path: '/engineering/eco' }, requiredPermissions: [P.engineeringChangesRead] },
        { title: '工程文档', icon: FileTextIcon, to: { path: '/engineering/documents' }, requiredPermissions: [P.engineeringDocumentsRead] },
      ],
    },
  ],
  'planning': [
    {
      items: [
        { title: '需求与物料计划', icon: CalendarRangeIcon, to: { path: '/planning' }, requiredPermissions: [P.planningDemandsRead, P.planningMrpRead, P.planningSuggestionsManage] },
        { title: '排产工作台', icon: CalendarCogIcon, to: { path: '/scheduling' }, requiredPermissions: [P.schedulingPlansRead] },
      ],
    },
  ],
  'erp': [
    {
      items: [
        { title: '采购与供应', icon: ReceiptTextIcon, to: { path: '/erp' }, requiredPermissions: [P.erpProcurementRead] },
        { title: '销售管理', icon: HandshakeIcon, to: { path: '/erp/sales' }, requiredPermissions: [P.erpSalesRead] },
        { title: '财务', icon: WalletIcon, to: { path: '/erp/finance' }, requiredPermissions: [P.erpFinanceRead] },
      ],
    },
  ],
  'mes': [
    {
      label: '计划与工单',
      items: [
        { title: '生产驾驶舱', icon: GaugeIcon, to: { path: '/mes' }, requiredPermissions: [P.mesOverviewRead] },
        { title: '生产计划', icon: CalendarRangeIcon, to: { path: '/mes/plans' }, requiredPermissions: [P.mesPlansRead] },
        { title: '工单与派工', icon: ClipboardListIcon, to: { path: '/mes/work-orders' }, requiredPermissions: [P.mesWorkOrdersRead] },
        { title: '派工看板', icon: UserCheckIcon, to: { path: '/mes/dispatch' }, requiredPermissions: [P.mesDispatchRead] },
      ],
    },
    {
      label: '执行与齐套',
      items: [
        { title: '领料与齐套', icon: PackageIcon, to: { path: '/mes/materials' }, requiredPermissions: [P.mesMaterialsRead] },
        { title: '工序执行', icon: PlayIcon, to: { path: '/mes/operation-tasks' }, requiredPermissions: [P.mesOperationsRead] },
        { title: '在制跟踪', icon: ActivityIcon, to: { path: '/mes/wip' }, requiredPermissions: [P.mesOperationsRead] },
      ],
    },
    {
      label: '报工与完工',
      items: [
        { title: '报工记录', icon: ClipboardCheckIcon, to: { path: '/mes/production-reports' }, requiredPermissions: [P.mesReportingRead] },
        { title: '完工入库', icon: PackageCheckIcon, to: { path: '/mes/receipts' }, requiredPermissions: [P.mesReceiptsRead] },
      ],
    },
    {
      label: '异常与协同',
      items: [
        { title: '质量与不良', icon: ShieldCheckIcon, to: { path: '/mes/quality' }, requiredPermissions: [P.mesQualityRead] },
        { title: '设备与停机', icon: WrenchIcon, to: { path: '/mes/downtime' }, requiredPermissions: [P.mesDowntimeRead] },
        { title: '异常与产能', icon: TrendingUpIcon, to: { path: '/mes/capacity' }, requiredPermissions: [P.mesCapacityRead] },
        { title: '规则排程', icon: CalendarCogIcon, to: { path: '/mes/schedules' }, requiredPermissions: [P.mesSchedulesRead, P.mesSchedulesManage] },
        { title: '班次交接', icon: ArrowRightLeftIcon, to: { path: '/mes/handovers' }, requiredPermissions: [P.mesHandoversRead] },
      ],
    },
    {
      label: '追溯与诊断',
      items: [
        { title: '追溯查询', icon: SearchIcon, to: { path: '/mes/traceability' }, requiredPermissions: [P.mesTraceabilityRead] },
        { title: '生产准备检查', icon: CheckCheckIcon, to: { path: '/mes/foundation' }, requiredPermissions: [P.mesFoundationRead] },
      ],
    },
  ],
  'quality': [
    {
      items: [
        { title: '检验任务与记录', icon: ClipboardCheckIcon, to: { path: '/quality/inspections' }, requiredPermissions: [P.qualityInspectionRecordsRead] },
        { title: '不合格品处理', icon: ShieldAlertIcon, to: { path: '/quality/ncrs' }, requiredPermissions: [P.qualityNcrRead] },
        { title: '原因码目录', icon: HashIcon, to: { path: '/quality/reason-codes' }, requiredPermissions: [P.qualityNcrManage] },
      ],
    },
  ],
  'inventory': [
    {
      items: [
        { title: '库存可用量', icon: PackageSearchIcon, to: { path: '/inventory/availability' }, requiredPermissions: [P.inventoryLedgerRead] },
        { title: '库存移动', icon: ArrowRightLeftIcon, to: { path: '/inventory/movements' }, requiredPermissions: [P.inventoryMovementsCreate] },
        { title: '库存盘点', icon: ClipboardListIcon, to: { path: '/inventory/counts' }, requiredPermissions: [P.inventoryCountsManage] },
      ],
    },
  ],
  'wms': [
    {
      items: [
        { title: '收货入库', icon: PackageCheckIcon, to: { path: '/wms/inbound' }, requiredPermissions: [P.wmsReceiptsRead] },
        { title: '上架任务', icon: LayersIcon, to: { path: '/wms/putaway' }, requiredPermissions: [P.wmsReceiptsRead] },
        { title: '出库发货', icon: PackageIcon, to: { path: '/wms/outbound' }, requiredPermissions: [P.wmsShipmentsRead] },
        { title: '拣货任务', icon: PackageSearchIcon, to: { path: '/wms/picking' }, requiredPermissions: [P.wmsShipmentsRead] },
        { title: 'WCS 任务', icon: CpuIcon, to: { path: '/wms/wcs' }, requiredPermissions: [P.wmsAutomationManage] },
        { title: '盘点执行', icon: ClipboardCheckIcon, to: { path: '/wms/counts' }, requiredPermissions: [P.wmsReceiptsRead] },
      ],
    },
  ],
  'barcode': [
    {
      items: [
        { title: '条码规则', icon: HashIcon, to: { path: '/barcode/rules' }, requiredPermissions: [P.barcodeTemplatesManage] },
        { title: '标签模板', icon: FileTextIcon, to: { path: '/barcode/templates' }, requiredPermissions: [P.barcodeTemplatesManage] },
      ],
    },
  ],
  'equipment': [
    {
      label: '运行监控',
      items: [
        { title: '设备运行看板', icon: ActivityIcon, to: { path: '/equipment' }, requiredPermissions: [P.iiotTelemetryRead] },
        { title: '设备报警', icon: BellRingIcon, to: { path: '/equipment/alarms' }, requiredPermissions: [P.iiotAlarmsRead] },
      ],
    },
    {
      label: '维护保养',
      items: [
        { title: '维护工单', icon: WrenchIcon, to: { path: '/maintenance/work-orders' }, requiredPermissions: [P.maintenanceWorkOrdersRead] },
        { title: '保养计划', icon: CalendarClockIcon, to: { path: '/maintenance/plans' }, requiredPermissions: [P.maintenancePlansRead] },
        { title: '点检记录', icon: ClipboardCheckIcon, to: { path: '/maintenance/inspections' }, requiredPermissions: [P.maintenancePlansRead] },
        { title: '备件需求', icon: PackageSearchIcon, to: { path: '/maintenance/spare-parts' }, requiredPermissions: [P.maintenanceWorkOrdersRead] },
        { title: '可靠性指标', icon: TrendingUpIcon, to: { path: '/maintenance/reliability' }, requiredPermissions: [P.maintenanceWorkOrdersRead] },
        { title: '可用窗口', icon: CalendarRangeIcon, to: { path: '/maintenance/availability' }, requiredPermissions: [P.maintenanceWorkOrdersRead] },
      ],
    },
  ],
  'approval': [
    {
      items: [
        { title: '审批中心', icon: FileCheck2Icon, to: { path: '/approval' }, requiredPermissions: [P.approvalsRead, P.approvalsManage] },
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
  return 'workbench'
}

/** Keep entries the principal may see. Undefined codes means the caller has no loaded principal yet. */
export function permittedBy<T extends { requiredPermissions?: string[] }>(
  entries: T[],
  permissionCodes: string[] | undefined,
): T[] {
  if (permissionCodes === undefined) {
    return entries
  }

  const codes = permissionCodes
  return entries.filter(
    (e) => !e.requiredPermissions?.length || e.requiredPermissions.some((p) => codes.includes(p)),
  )
}
