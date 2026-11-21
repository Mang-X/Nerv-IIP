import type { NavLink, SideNav } from '@nerv-iip/app-shell'
import {
  ActivityIcon,
  ArrowRightLeftIcon,
  BellRingIcon,
  CalendarRangeIcon,
  CheckCheckIcon,
  ClipboardCheckIcon,
  ClipboardListIcon,
  GaugeIcon,
  PackageCheckIcon,
  PackageIcon,
  PlayIcon,
  SearchIcon,
  ShieldCheckIcon,
  TrendingUpIcon,
  UserCheckIcon,
  WrenchIcon,
} from '@lucide/vue'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'

const mesDispatch = {
  title: '派工看板',
  icon: UserCheckIcon,
  to: { path: '/mes/dispatch' },
  requiredPermissions: [P.mesDispatchRead],
} satisfies NavLink

const mesWorkOrders = {
  title: '工单与派工',
  icon: ClipboardListIcon,
  to: { path: '/mes/work-orders' },
  activeOn: [mesDispatch.to],
  requiredPermissions: [P.mesWorkOrdersRead],
} satisfies NavLink

const mesFoundation = {
  title: '生产准备检查',
  icon: CheckCheckIcon,
  to: { path: '/mes/foundation' },
  requiredPermissions: [P.mesFoundationRead],
} satisfies NavLink

const mesMaterials = {
  title: '领料与齐套',
  icon: PackageIcon,
  to: { path: '/mes/materials' },
  activeOn: [mesFoundation.to],
  requiredPermissions: [P.mesMaterialsRead],
} satisfies NavLink

export const mesViewGroups = [
  { entry: mesWorkOrders, entryViewLabel: '工单列表', alternate: mesDispatch },
  { entry: mesMaterials, entryViewLabel: '领料', alternate: mesFoundation },
]

export const mesSideNav: SideNav = [
  {
    label: '计划与工单',
    items: [
      {
        title: '生产驾驶舱',
        icon: GaugeIcon,
        to: { path: '/mes' },
        requiredPermissions: [P.mesOverviewRead],
      },
      {
        title: '生产计划',
        icon: CalendarRangeIcon,
        to: { path: '/mes/plans' },
        requiredPermissions: [P.mesPlansRead],
      },
      mesWorkOrders,
      mesDispatch,
    ],
  },
  {
    label: '执行与齐套',
    items: [
      mesMaterials,
      {
        title: '工序执行',
        icon: PlayIcon,
        to: { path: '/mes/operation-tasks' },
        requiredPermissions: [P.mesOperationsRead],
      },
      {
        title: '安灯响应',
        icon: BellRingIcon,
        to: { path: '/mes/andon' },
        requiredPermissions: [P.mesOperationsRead],
      },
      {
        title: '在制跟踪',
        icon: ActivityIcon,
        to: { path: '/mes/wip' },
        requiredPermissions: [P.mesOperationsRead],
      },
    ],
  },
  {
    label: '报工与完工',
    items: [
      {
        title: '报工记录',
        icon: ClipboardCheckIcon,
        to: { path: '/mes/production-reports' },
        requiredPermissions: [P.mesReportingRead],
      },
      {
        title: '生产日报',
        icon: TrendingUpIcon,
        to: { path: '/mes/reports' },
        requiredPermissions: [P.mesReportingRead],
      },
      {
        title: '完工入库',
        icon: PackageCheckIcon,
        to: { path: '/mes/receipts' },
        requiredPermissions: [P.mesReceiptsRead],
      },
    ],
  },
  {
    label: '异常与协同',
    items: [
      {
        title: '质量与不良',
        icon: ShieldCheckIcon,
        to: { path: '/mes/quality' },
        requiredPermissions: [P.mesQualityRead],
      },
      {
        title: '设备与停机',
        icon: WrenchIcon,
        to: { path: '/mes/downtime' },
        requiredPermissions: [P.mesDowntimeRead],
      },
      {
        title: '异常与产能',
        icon: TrendingUpIcon,
        to: { path: '/mes/capacity' },
        requiredPermissions: [P.mesCapacityRead],
      },
      {
        title: '班次交接',
        icon: ArrowRightLeftIcon,
        to: { path: '/mes/handovers' },
        requiredPermissions: [P.mesHandoversRead],
      },
    ],
  },
  {
    label: '追溯与诊断',
    items: [
      {
        title: '追溯查询',
        icon: SearchIcon,
        to: { path: '/mes/traceability' },
        requiredPermissions: [P.mesTraceabilityRead],
      },
      mesFoundation,
    ],
  },
]
