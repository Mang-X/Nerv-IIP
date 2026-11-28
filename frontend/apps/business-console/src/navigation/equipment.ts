import type { SideNav } from '@nerv-iip/app-shell'
import {
  ActivityIcon,
  BellRingIcon,
  CalendarClockIcon,
  CalendarRangeIcon,
  CircleSlashIcon,
  ClipboardCheckIcon,
  CpuIcon,
  GaugeIcon,
  HashIcon,
  LineChartIcon,
  NetworkIcon,
  PackageSearchIcon,
  Settings2Icon,
  TrendingUpIcon,
  WrenchIcon,
} from '@lucide/vue'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'

export const equipmentSideNav: SideNav = [
  {
    label: '运行监控',
    items: [
      {
        title: '设备运行看板',
        icon: ActivityIcon,
        to: { path: '/equipment' },
        requiredPermissions: [P.iiotTelemetryRead],
      },
      {
        title: '设备报警',
        icon: BellRingIcon,
        to: { path: '/equipment/alarms' },
        requiredPermissions: [P.iiotAlarmsRead],
      },
      {
        title: '采集点位',
        icon: HashIcon,
        to: { path: '/equipment/telemetry/tags' },
        requiredPermissions: [P.iiotTelemetryRead],
      },
      {
        title: '连接与采集配置',
        icon: NetworkIcon,
        to: { path: '/equipment/telemetry/connectors' },
        requiredPermissions: [P.iiotTelemetryRead],
      },
      {
        title: '报警规则',
        icon: Settings2Icon,
        to: { path: '/equipment/telemetry/alarm-rules' },
        requiredPermissions: [P.iiotAlarmsRead, P.iiotAlarmRulesManage],
      },
      {
        title: '历史趋势',
        icon: LineChartIcon,
        to: { path: '/equipment/telemetry/history' },
        requiredPermissions: [P.iiotTelemetryRead],
      },
      {
        title: 'OEE 趋势与横比',
        icon: GaugeIcon,
        to: { path: '/equipment/telemetry/oee' },
        requiredPermissions: [P.iiotTelemetryRead],
      },
      {
        title: '设备控制通道绑定',
        icon: CpuIcon,
        to: { path: '/equipment/telemetry/control-bindings' },
        requiredPermissions: [P.iiotDeviceControlRead],
      },
    ],
  },
  {
    label: '维护保养',
    items: [
      {
        title: '维护工单',
        icon: WrenchIcon,
        to: { path: '/maintenance/work-orders' },
        requiredPermissions: [P.maintenanceWorkOrdersRead],
      },
      {
        title: '保养计划',
        icon: CalendarClockIcon,
        to: { path: '/maintenance/plans' },
        requiredPermissions: [P.maintenancePlansRead],
      },
      {
        title: '点检记录',
        icon: ClipboardCheckIcon,
        to: { path: '/maintenance/inspections' },
        requiredPermissions: [P.maintenancePlansRead],
      },
      {
        title: '备件需求',
        icon: PackageSearchIcon,
        to: { path: '/maintenance/spare-parts' },
        requiredPermissions: [P.maintenanceWorkOrdersRead],
      },
      {
        title: '停机原因',
        icon: CircleSlashIcon,
        to: { path: '/maintenance/downtime-reasons' },
        requiredPermissions: [P.maintenanceDowntimeReasonsRead],
      },
      {
        title: '可靠性指标',
        icon: TrendingUpIcon,
        to: { path: '/maintenance/reliability' },
        requiredPermissions: [P.maintenanceWorkOrdersRead],
      },
      {
        title: '可用窗口',
        icon: CalendarRangeIcon,
        to: { path: '/maintenance/availability' },
        requiredPermissions: [P.maintenanceWorkOrdersRead],
      },
    ],
  },
]
