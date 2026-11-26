import type { NavDomain } from '@nerv-iip/app-shell'
import {
  ActivityIcon,
  BoxesIcon,
  CalendarRangeIcon,
  ClipboardCheckIcon,
  FactoryIcon,
  FileCheck2Icon,
  GitBranchIcon,
  HashIcon,
  LayoutDashboardIcon,
  PackageSearchIcon,
  ReceiptTextIcon,
  ShieldAlertIcon,
  WarehouseIcon,
} from '@lucide/vue'
import { BUSINESS_DOMAIN_PERMISSIONS } from '@/permissions'

export const WORKBENCH_DOMAIN_ID = 'workbench'
/** Top capability areas (the horizontal "top" of the T), in display order. */
export const BUSINESS_DOMAINS: Omit<NavDomain, 'to'>[] = [
  {
    id: 'workbench',
    title: '数字化工作台',
    icon: LayoutDashboardIcon,
    requiredPermissions: [...BUSINESS_DOMAIN_PERMISSIONS.workbench],
  },
  {
    id: 'master-data',
    title: '基础数据',
    icon: BoxesIcon,
    requiredPermissions: [...BUSINESS_DOMAIN_PERMISSIONS.masterData],
  },
  {
    id: 'engineering',
    title: '产品工程',
    icon: GitBranchIcon,
    requiredPermissions: [...BUSINESS_DOMAIN_PERMISSIONS.engineering],
  },
  {
    id: 'planning',
    title: '需求与计划',
    icon: CalendarRangeIcon,
    requiredPermissions: [...BUSINESS_DOMAIN_PERMISSIONS.planning],
  },
  {
    id: 'mes',
    title: '制造执行',
    icon: FactoryIcon,
    requiredPermissions: [...BUSINESS_DOMAIN_PERMISSIONS.mes],
  },
  {
    id: 'quality',
    title: '质量管理',
    icon: ClipboardCheckIcon,
    requiredPermissions: [...BUSINESS_DOMAIN_PERMISSIONS.quality],
  },
  {
    id: 'inventory',
    title: '库存管理',
    icon: PackageSearchIcon,
    requiredPermissions: [...BUSINESS_DOMAIN_PERMISSIONS.inventory],
  },
  {
    id: 'wms',
    title: '仓储作业',
    icon: WarehouseIcon,
    requiredPermissions: [...BUSINESS_DOMAIN_PERMISSIONS.wms],
  },
  {
    id: 'erp',
    title: '经营管理',
    icon: ReceiptTextIcon,
    requiredPermissions: [...BUSINESS_DOMAIN_PERMISSIONS.erp],
  },
  {
    id: 'barcode',
    title: '条码标签',
    icon: HashIcon,
    requiredPermissions: [...BUSINESS_DOMAIN_PERMISSIONS.barcode],
  },
  {
    id: 'equipment',
    title: '设备监控',
    icon: ActivityIcon,
    requiredPermissions: [...BUSINESS_DOMAIN_PERMISSIONS.equipment],
  },
  {
    id: 'approval',
    title: '审批中心',
    icon: FileCheck2Icon,
    requiredPermissions: [...BUSINESS_DOMAIN_PERMISSIONS.approval],
  },
  {
    id: 'ops',
    title: '集成运维',
    icon: ShieldAlertIcon,
    requiredPermissions: [...BUSINESS_DOMAIN_PERMISSIONS.ops],
  },
]
