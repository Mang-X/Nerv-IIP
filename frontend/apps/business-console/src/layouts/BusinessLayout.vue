<script setup lang="ts">
import type { NavItem } from '@nerv-iip/app-shell'
import { AppShell } from '@nerv-iip/app-shell'
import { useAuthStore } from '@/stores/auth'
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from '@nerv-iip/ui'
import { BoxesIcon, ClipboardCheckIcon, FactoryIcon, PackageSearchIcon } from 'lucide-vue-next'
import { storeToRefs } from 'pinia'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'

const { t, te } = useI18n()
const route = useRoute()

const navItems = computed<NavItem[]>(() => [
  {
    title: '主数据',
    icon: BoxesIcon,
    isActive: route.path.startsWith('/master-data'),
    items: [{ title: 'SKU 维护', to: { path: '/master-data/skus' } }],
  },
  {
    title: '库存',
    icon: PackageSearchIcon,
    isActive: route.path.startsWith('/inventory'),
    items: [
      { title: '库存可用量', to: { path: '/inventory/availability' } },
      { title: '库存移动', to: { path: '/inventory/movements' } },
      { title: '库存盘点', to: { path: '/inventory/counts' } },
    ],
  },
  {
    title: '质量',
    icon: ClipboardCheckIcon,
    isActive: route.path.startsWith('/quality'),
    items: [
      { title: '检验记录', to: { path: '/quality/inspections' } },
      { title: '不合格品处理', to: { path: '/quality/ncrs' } },
    ],
  },
  {
    title: 'MES',
    icon: FactoryIcon,
    isActive: route.path.startsWith('/mes'),
    items: [
      { title: 'MES 总览', to: { path: '/mes' } },
      { title: '基础就绪', to: { path: '/mes/foundation' } },
      { title: '工单执行', to: { path: '/mes/work-orders' } },
      { title: '工序任务', to: { path: '/mes/operation-tasks' } },
      { title: '在制状态', to: { path: '/mes/wip' } },
      { title: '生产报工', to: { path: '/mes/production-reports' } },
      { title: '完工入库', to: { path: '/mes/receipts' } },
      { title: '产能影响', to: { path: '/mes/capacity' } },
      { title: '规则排程', to: { path: '/mes/schedules' } },
    ],
  },
])

const auth = useAuthStore()
const { principal } = storeToRefs(auth)
const router = useRouter()

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
    items.push({ label: segment.replaceAll('-', ' ').replace(/^\w/, (c) => c.toUpperCase()) })
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
