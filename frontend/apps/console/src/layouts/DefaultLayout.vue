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
import { BellIcon, LayersIcon, ShieldIcon } from 'lucide-vue-next'
import { storeToRefs } from 'pinia'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'

const { t, te } = useI18n()

const navItems = computed<NavItem[]>(() => [
  { title: t('nav.instances'), icon: LayersIcon, to: { name: '/' } },
  { title: t('nav.notifications'), icon: BellIcon, to: { path: '/notifications' } },
  {
    title: t('nav.iam'),
    icon: ShieldIcon,
    isActive: true,
    items: [
      { title: t('nav.users'), to: { path: '/iam/users' } },
      { title: t('nav.roles'), to: { path: '/iam/roles' } },
      { title: t('nav.sessions'), to: { path: '/iam/sessions' } },
    ],
  },
])

const auth = useAuthStore()
const { principal } = storeToRefs(auth)
const router = useRouter()
const route = useRoute()

const breadcrumbs = computed(() => {
  const path = route?.path ?? '/'
  const titleKey = (route?.meta?.title as string) ?? 'breadcrumb.dashboard'
  const title = te(titleKey) ? t(titleKey) : titleKey

  if (path === '/' || path === '/login') {
    return [{ label: title }]
  }

  const segments = path.split('/').filter(Boolean)
  const items: { label: string }[] = []

  for (let i = 0; i < segments.length - 1; i++) {
    const s = segments[i]
    items.push({ label: s.charAt(0).toUpperCase() + s.slice(1) })
  }

  const parentLabel = items.at(-1)?.label
  const pageLabel = parentLabel && title.startsWith(`${parentLabel} `)
    ? title.slice(parentLabel.length + 1)
    : title

  items.push({ label: pageLabel })
  return items
})

const shellUser = computed(() => {
  const p = principal.value
  if (!p) return undefined
  return {
    name: p.loginName ?? p.principalId ?? t('nav.users'),
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
    title="Nerv-IIP"
    :nav-items="navItems"
    :nav-label="t('nav.platform')"
    :sign-out-label="t('nav.signOut')"
    :user="shellUser"
    @sign-out="signOut"
  >
    <template #header>
      <Breadcrumb>
        <BreadcrumbList>
          <template v-for="(crumb, i) in breadcrumbs" :key="i">
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
