import type { SideNav } from '@nerv-iip/app-shell'
import { ShieldAlertIcon } from '@lucide/vue'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'

export const opsSideNav: SideNav = [
  {
    items: [
      {
        title: '集成事件死信',
        icon: ShieldAlertIcon,
        to: { path: '/ops/dead-letters' },
        requiredPermissions: [P.deadLettersRead],
      },
    ],
  },
]
