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
    title: t('nav.masterData'),
    icon: BoxesIcon,
    isActive: route.path.startsWith('/master-data'),
    items: [{ title: t('nav.skus'), to: { path: '/master-data/skus' } }],
  },
  {
    title: t('nav.inventory'),
    icon: PackageSearchIcon,
    isActive: route.path.startsWith('/inventory'),
    items: [
      { title: t('nav.availability'), to: { path: '/inventory/availability' } },
      { title: t('nav.movements'), to: { path: '/inventory/movements' } },
      { title: t('nav.counts'), to: { path: '/inventory/counts' } },
    ],
  },
  {
    title: t('nav.quality'),
    icon: ClipboardCheckIcon,
    isActive: route.path.startsWith('/quality'),
    items: [
      { title: t('nav.inspections'), to: { path: '/quality/inspections' } },
      { title: t('nav.ncrs'), to: { path: '/quality/ncrs' } },
    ],
  },
  {
    title: t('nav.mes'),
    icon: FactoryIcon,
    isActive: route.path.startsWith('/mes'),
    items: [
      { title: t('nav.workOrders'), to: { path: '/mes/work-orders' } },
      { title: t('nav.schedules'), to: { path: '/mes/schedules' } },
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
    name: p.loginName ?? p.principalId ?? t('nav.authenticatedUser'),
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
    title="Nerv-IIP Business"
    :nav-items="navItems"
    :nav-label="t('nav.business')"
    :sign-out-label="t('nav.signOut')"
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
