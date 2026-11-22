import type { SideNav } from '@nerv-iip/app-shell'
import {
  ClipboardCheckIcon,
  CpuIcon,
  LayersIcon,
  PackageCheckIcon,
  PackageIcon,
  PackageSearchIcon,
  UsersRoundIcon,
} from '@lucide/vue'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'

export const wmsSideNav: SideNav = [
  {
    items: [
      {
        title: '收货入库',
        icon: PackageCheckIcon,
        to: { path: '/wms/inbound' },
        requiredPermissions: [P.wmsReceiptsRead],
      },
      {
        title: '上架任务',
        icon: LayersIcon,
        to: { path: '/wms/putaway' },
        requiredPermissions: [P.wmsReceiptsRead],
      },
      {
        title: '出库发货',
        icon: PackageIcon,
        to: { path: '/wms/outbound' },
        requiredPermissions: [P.wmsShipmentsRead],
      },
      {
        title: '拣货任务',
        icon: PackageSearchIcon,
        to: { path: '/wms/picking' },
        requiredPermissions: [P.wmsShipmentsRead],
      },
      {
        title: 'WCS 任务',
        icon: CpuIcon,
        to: { path: '/wms/wcs' },
        requiredPermissions: [P.wmsAutomationManage],
      },
      {
        title: '盘点执行',
        icon: ClipboardCheckIcon,
        to: { path: '/wms/counts' },
        requiredPermissions: [P.wmsCountsRead],
      },
      {
        title: '作业池',
        icon: UsersRoundIcon,
        to: { path: '/wms/work-pools' },
        requiredPermissions: [P.wmsWorkPoolsManage],
      },
    ],
  },
]
