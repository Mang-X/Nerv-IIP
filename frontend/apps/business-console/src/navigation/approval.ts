import type { SideNav } from '@nerv-iip/app-shell'
import { FileCheck2Icon } from '@lucide/vue'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'

export const approvalSideNav: SideNav = [
  {
    items: [
      {
        title: '审批中心',
        icon: FileCheck2Icon,
        to: { path: '/approval' },
        requiredPermissions: [P.approvalsRead, P.approvalsManage],
      },
    ],
  },
]
