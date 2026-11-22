import type { SideNav } from '@nerv-iip/app-shell'
import {
  ClipboardCheckIcon,
  HashIcon,
  ListChecksIcon,
  LineChartIcon,
  RulerIcon,
  ShieldAlertIcon,
  WrenchIcon,
} from '@lucide/vue'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'

export const qualitySideNav: SideNav = [
  {
    items: [
      {
        title: '待检工作台',
        icon: ListChecksIcon,
        to: { path: '/quality/inspection-tasks' },
        requiredPermissions: [P.qualityInspectionRecordsRead],
      },
      {
        title: '检验任务与记录',
        icon: ClipboardCheckIcon,
        to: { path: '/quality/inspections' },
        requiredPermissions: [P.qualityInspectionRecordsRead],
      },
      {
        title: '不合格品处理',
        icon: ShieldAlertIcon,
        to: { path: '/quality/ncrs' },
        requiredPermissions: [P.qualityNcrRead],
      },
      {
        title: '质量分析',
        icon: LineChartIcon,
        to: { path: '/quality/analysis' },
        requiredPermissions: [P.qualityNcrRead],
      },
      {
        title: '计量与校准',
        icon: RulerIcon,
        to: { path: '/quality/calibration' },
        requiredPermissions: [P.qualityInspectionRecordsRead],
      },
      {
        title: '纠正措施',
        icon: WrenchIcon,
        to: { path: '/quality/capas' },
        requiredPermissions: [P.qualityNcrRead],
      },
      {
        title: '原因码目录',
        icon: HashIcon,
        to: { path: '/quality/reason-codes' },
        requiredPermissions: [P.qualityNcrManage],
      },
    ],
  },
]
