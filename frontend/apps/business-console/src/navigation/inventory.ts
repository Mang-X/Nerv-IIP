import type { SideNav } from '@nerv-iip/app-shell'
import {
  ArrowRightLeftIcon,
  ClipboardListIcon,
  LayersIcon,
  MapPinIcon,
  PackageSearchIcon,
} from '@lucide/vue'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'

export const inventorySideNav: SideNav = [
  {
    items: [
      {
        title: '库存可用量',
        icon: PackageSearchIcon,
        to: { path: '/inventory/availability' },
        requiredPermissions: [P.inventoryLedgerRead],
      },
      {
        title: '批次与预留',
        icon: LayersIcon,
        to: { path: '/inventory/lots' },
        requiredPermissions: [P.inventoryLedgerRead],
      },
      {
        title: '库存移动',
        icon: ArrowRightLeftIcon,
        to: { path: '/inventory/movements' },
        requiredPermissions: [P.inventoryMovementsCreate],
      },
      {
        title: '库存盘点',
        icon: ClipboardListIcon,
        to: { path: '/inventory/counts' },
        requiredPermissions: [P.inventoryCountsManage],
      },
      {
        title: '库位',
        icon: MapPinIcon,
        to: { path: '/inventory/locations' },
        requiredPermissions: [P.inventoryLocationsManage],
      },
    ],
  },
]
