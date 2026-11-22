import type { SideNav } from '@nerv-iip/app-shell'
import {
  BoxIcon,
  FileTextIcon,
  FolderTreeIcon,
  GitForkIcon,
  GitPullRequestIcon,
  LayersIcon,
  NetworkIcon,
  RouteIcon,
  WrenchIcon,
} from '@lucide/vue'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'

export const engineeringSideNav: SideNav = [
  {
    label: '物料与结构',
    items: [
      {
        title: '工程物料',
        icon: BoxIcon,
        to: { path: '/engineering/items' },
        requiredPermissions: [P.engineeringItemsRead],
      },
      {
        title: '设计 BOM',
        icon: NetworkIcon,
        to: { path: '/engineering/ebom' },
        requiredPermissions: [P.engineeringBomsRead],
      },
      {
        title: '制造 BOM',
        icon: GitForkIcon,
        to: { path: '/engineering/mbom' },
        requiredPermissions: [P.engineeringBomsRead],
      },
      {
        title: 'BOM 分析',
        icon: FolderTreeIcon,
        to: { path: '/engineering/bom-analysis' },
        requiredPermissions: [P.engineeringBomsRead],
      },
    ],
  },
  {
    label: '工艺与版本',
    items: [
      {
        title: '标准工序',
        icon: WrenchIcon,
        to: { path: '/engineering/standard-operations' },
        requiredPermissions: [P.engineeringStandardOperationsRead],
      },
      {
        title: '工艺路线',
        icon: RouteIcon,
        to: { path: '/engineering/routings' },
        requiredPermissions: [P.engineeringRoutingsRead],
      },
      {
        title: '生产版本',
        icon: LayersIcon,
        to: { path: '/engineering/production-versions' },
        requiredPermissions: [P.engineeringProductionVersionsRead],
      },
    ],
  },
  {
    label: '变更与文档',
    items: [
      {
        title: '工程变更',
        icon: GitPullRequestIcon,
        to: { path: '/engineering/eco' },
        requiredPermissions: [P.engineeringChangesRead],
      },
      {
        title: '工程文档',
        icon: FileTextIcon,
        to: { path: '/engineering/documents' },
        requiredPermissions: [P.engineeringDocumentsRead],
      },
    ],
  },
]
