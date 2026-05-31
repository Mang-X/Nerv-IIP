<script setup lang="ts">
import type { NavItem, NavSubItem } from '@nerv-iip/app-shell'
import { AppShell } from '@nerv-iip/app-shell'
import { useAuthStore } from '@/stores/auth'
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from '@nerv-iip/ui'
import { BoxesIcon, ClipboardCheckIcon, FactoryIcon, GitBranchIcon, PackageSearchIcon, ReceiptTextIcon, ScanLineIcon } from 'lucide-vue-next'
import { storeToRefs } from 'pinia'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'

const { t, te } = useI18n()
const route = useRoute()

const mesNavItems: NavSubItem[] = [
  { title: '生产驾驶舱', to: { path: '/mes' } },
  { title: '生产计划', to: { path: '/mes/plans' } },
  { title: '工单与派工', to: { path: '/mes/work-orders' } },
  { title: '齐套与物料', to: { path: '/mes/materials' } },
  { title: '派工看板', to: { path: '/mes/dispatch' } },
  { title: '工序执行', to: { path: '/mes/operation-tasks' } },
  { title: '在制跟踪', to: { path: '/mes/wip' } },
  { title: '报工记录', to: { path: '/mes/production-reports' } },
  { title: '完工入库', to: { path: '/mes/receipts' } },
]

const qualityInventoryNavItems: NavSubItem[] = [
  { title: '检验任务与记录', to: { path: '/quality/inspections' } },
  { title: '不合格品处理', to: { path: '/quality/ncrs' } },
  { title: '质量与不良', to: { path: '/mes/quality' } },
  { title: '库存可用量', to: { path: '/inventory/availability' } },
  { title: '库存移动', to: { path: '/inventory/movements' } },
  { title: '库存盘点', to: { path: '/inventory/counts' } },
]

const exceptionScheduleNavItems: NavSubItem[] = [
  { title: '设备与停机', to: { path: '/mes/downtime' } },
  { title: '异常与产能', to: { path: '/mes/capacity' } },
  { title: '规则排程', to: { path: '/mes/schedules' } },
  { title: '班次交接', to: { path: '/mes/handovers' } },
]

const traceabilityNavItems: NavSubItem[] = [
  { title: '追溯查询', to: { path: '/mes/traceability' } },
  { title: '生产准备检查', to: { path: '/mes/foundation' } },
]

function navPath(item: NavSubItem) {
  if (typeof item.to === 'string') {
    return item.to
  }

  return 'path' in item.to ? item.to.path : undefined
}

function isRoutePathActive(path: string) {
  const normalizedPath = path.length > 1 && path.endsWith('/') ? path.slice(0, -1) : path
  const supportsNestedRoutes = normalizedPath.split('/').filter(Boolean).length > 1

  return route.path === normalizedPath || (supportsNestedRoutes && route.path.startsWith(`${normalizedPath}/`))
}

function isNavGroupActive(items: NavSubItem[]) {
  return items.some((item) => {
    const path = navPath(item)
    return path ? isRoutePathActive(path) : false
  })
}

const navItems = computed<NavItem[]>(() => [
  {
    title: '基础数据',
    icon: BoxesIcon,
    isActive: route.path.startsWith('/master-data') && !route.path.startsWith('/master-data/process'),
    items: [
      { title: '物料与产品', to: { path: '/master-data/skus' } },
      { title: '客户与供应商', to: { path: '/master-data/partners' } },
      { title: '工厂资源', to: { path: '/master-data/resources' } },
    ],
  },
  {
    title: '工程资料',
    icon: GitBranchIcon,
    isActive: route.path.startsWith('/engineering') || route.path.startsWith('/master-data/process'),
    items: [
      { title: '工艺与版本', to: { path: '/master-data/process' } },
      { title: '发布工程版本', to: { path: '/engineering' } },
    ],
  },
  {
    title: '计划与采购',
    icon: ReceiptTextIcon,
    isActive: route.path.startsWith('/planning') || route.path.startsWith('/erp'),
    items: [
      { title: '需求与物料计划', to: { path: '/planning' } },
      { title: '采购与供应', to: { path: '/erp' } },
    ],
  },
  {
    title: '生产执行',
    icon: FactoryIcon,
    isActive: isNavGroupActive(mesNavItems),
    items: mesNavItems,
  },
  {
    title: '质量与库存',
    icon: ClipboardCheckIcon,
    isActive: isNavGroupActive(qualityInventoryNavItems),
    items: qualityInventoryNavItems,
  },
  {
    title: '设备异常',
    icon: PackageSearchIcon,
    isActive: isNavGroupActive(exceptionScheduleNavItems),
    items: exceptionScheduleNavItems,
  },
  {
    title: '追溯报表',
    icon: ScanLineIcon,
    isActive: isNavGroupActive(traceabilityNavItems),
    items: traceabilityNavItems,
  },
])

const auth = useAuthStore()
const { principal } = storeToRefs(auth)
const router = useRouter()

const breadcrumbSegmentLabels: Record<string, string> = {
  erp: '采购与供应',
  inventory: '质量与库存',
  engineering: '工程资料',
  planning: '计划与采购',
  'master-data': '基础数据',
  mes: '生产执行',
  quality: '质量与库存',
}

const breadcrumbs = computed(() => {
  const titleKey = (route.meta.title as string) ?? 'breadcrumb.dashboard'
  const title = te(titleKey) ? t(titleKey) : titleKey

  if (route.path === '/' || route.path === '/login') {
    return [{ label: title }]
  }

  const segments = route.path.split('/').filter(Boolean)
  const items: { label: string }[] = []

  for (let i = 0; i < segments.length - 1; i++) {
    const segment = segments[i]
    items.push({ label: breadcrumbSegmentLabels[segment] ?? segment.replaceAll('-', ' ') })
  }

  items.push({ label: title })
  return items
})

const shellUser = computed(() => {
  const p = principal.value
  if (!p) return undefined
  return {
    name: p.loginName ?? p.principalId ?? '已登录用户',
    email: p.email,
  }
})

async function signOut() {
  await auth.logout()
  await router.push('/login')
}
</script>

<template>
  <AppShell
    title="Nerv-IIP 业务控制台"
    :nav-items="navItems"
    nav-label="业务模块"
    sign-out-label="退出登录"
    :user="shellUser"
    @sign-out="signOut"
  >
    <template #header>
      <Breadcrumb>
        <BreadcrumbList>
          <template v-for="(crumb, i) in breadcrumbs" :key="`${crumb.label}-${i}`">
            <BreadcrumbSeparator v-if="i > 0" class="hidden md:block" />
            <BreadcrumbItem v-if="i < breadcrumbs.length - 1" class="hidden md:block">
              <span class="text-muted-foreground">{{ crumb.label }}</span>
            </BreadcrumbItem>
            <BreadcrumbItem v-else>
              <BreadcrumbPage>{{ crumb.label }}</BreadcrumbPage>
            </BreadcrumbItem>
          </template>
        </BreadcrumbList>
      </Breadcrumb>
    </template>
    <slot />
  </AppShell>
</template>
