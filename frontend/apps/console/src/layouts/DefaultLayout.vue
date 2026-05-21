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
import { ActivityIcon, KeyRoundIcon, LayersIcon, ShieldIcon, UsersIcon } from 'lucide-vue-next'
import { storeToRefs } from 'pinia'
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'

const navItems: NavItem[] = [
  { title: 'Instances', icon: LayersIcon, to: { name: '/' } },
  {
    title: 'IAM',
    icon: ShieldIcon,
    isActive: true,
    items: [
      { title: 'Users', to: { path: '/iam/users' } },
      { title: 'Roles', to: { path: '/iam/roles' } },
      { title: 'Sessions', to: { path: '/iam/sessions' } },
    ],
  },
]

const auth = useAuthStore()
const { principal } = storeToRefs(auth)
const router = useRouter()
const route = useRoute()

const breadcrumbs = computed(() => {
  const path = route?.path ?? '/'
  const title = (route?.meta?.title as string) ?? 'Dashboard'

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
    name: p.loginName ?? p.principalId ?? 'Authenticated user',
    email: p.email,
  }
})

async function signOut() {
  await auth.logout()
  await router.push('/login')
}
</script>

<template>
  <AppShell title="Nerv-IIP" :nav-items="navItems" :user="shellUser" @sign-out="signOut">
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
