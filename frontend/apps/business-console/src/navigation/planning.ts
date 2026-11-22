import type { SideNav } from '@nerv-iip/app-shell'
import { CalendarCogIcon, CalendarRangeIcon } from '@lucide/vue'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'

export const planningSideNav: SideNav = [
  {
    items: [
      {
        title: '需求与物料计划',
        icon: CalendarRangeIcon,
        to: { path: '/planning' },
        requiredPermissions: [
          P.planningDemandsRead,
          P.planningMpsRead,
          P.planningMrpRead,
          P.planningSuggestionsManage,
        ],
      },
      {
        title: '排产工作台',
        icon: CalendarCogIcon,
        to: { path: '/scheduling' },
        requiredPermissions: [P.schedulingPlansRead],
      },
    ],
  },
]
