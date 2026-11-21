import type { SideNav } from '@nerv-iip/app-shell'
import { LayoutDashboardIcon } from '@lucide/vue'
import { BUSINESS_DOMAIN_PERMISSIONS } from '@/permissions'

export const workbenchSideNav: SideNav = [
  {
    items: [
      {
        title: '工作台首页',
        icon: LayoutDashboardIcon,
        to: { path: '/' },
        requiredPermissions: [...BUSINESS_DOMAIN_PERMISSIONS.workbench],
      },
    ],
  },
]
