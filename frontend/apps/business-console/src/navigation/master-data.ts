import type { SideNav } from '@nerv-iip/app-shell'
import {
  AwardIcon,
  BookMarkedIcon,
  Building2Icon,
  CalendarRangeIcon,
  CpuIcon,
  FactoryIcon,
  FolderTreeIcon,
  GraduationCapIcon,
  HashIcon,
  PackageIcon,
  RulerIcon,
  UsersRoundIcon,
  IdCardIcon,
  WrenchIcon,
} from '@lucide/vue'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'

export const masterDataSideNav: SideNav = [
  {
    label: '主对象',
    items: [
      {
        title: '物料与产品',
        icon: PackageIcon,
        to: { path: '/master-data/skus' },
        requiredPermissions: [P.masterDataProductsRead],
      },
      {
        title: '业务伙伴',
        icon: Building2Icon,
        to: { path: '/master-data/partners' },
        requiredPermissions: [P.masterDataResourcesRead],
      },
      {
        title: '工厂结构',
        icon: FactoryIcon,
        to: { path: '/master-data/facilities' },
        requiredPermissions: [P.masterDataResourcesRead],
      },
      {
        title: '设备台账',
        icon: CpuIcon,
        to: { path: '/master-data/devices' },
        requiredPermissions: [P.masterDataResourcesRead],
      },
      {
        title: '工装与模具',
        icon: WrenchIcon,
        to: { path: '/master-data/tooling' },
        requiredPermissions: [P.masterDataResourcesRead],
      },
    ],
  },
  {
    label: '组织与排班',
    items: [
      {
        title: '员工',
        icon: IdCardIcon,
        to: { path: '/master-data/workers' },
        requiredPermissions: [P.masterDataResourcesRead],
      },
      {
        title: '组织与班组',
        icon: UsersRoundIcon,
        to: { path: '/master-data/organization' },
        requiredPermissions: [P.masterDataResourcesRead],
      },
      {
        title: '排班与日历',
        icon: CalendarRangeIcon,
        to: { path: '/master-data/scheduling' },
        requiredPermissions: [P.masterDataResourcesRead],
      },
      {
        title: '技能目录',
        icon: GraduationCapIcon,
        to: { path: '/master-data/skill-catalog' },
        requiredPermissions: [P.masterDataResourcesRead],
      },
      {
        title: '人员技能',
        icon: AwardIcon,
        to: { path: '/master-data/skills' },
        requiredPermissions: [P.masterDataResourcesRead],
      },
    ],
  },
  {
    label: '受控数据',
    items: [
      {
        title: '产品分类',
        icon: FolderTreeIcon,
        to: { path: '/master-data/product-categories' },
        requiredPermissions: [P.masterDataProductsRead],
      },
      {
        title: '计量单位',
        icon: RulerIcon,
        to: { path: '/master-data/units' },
        requiredPermissions: [P.masterDataProductsRead],
      },
      {
        title: '数据字典',
        icon: BookMarkedIcon,
        to: { path: '/master-data/reference-data' },
        requiredPermissions: [P.masterDataResourcesRead],
      },
      {
        title: '编码规则',
        icon: HashIcon,
        to: { path: '/master-data/code-rules' },
        requiredPermissions: [P.masterDataResourcesRead],
      },
    ],
  },
]
