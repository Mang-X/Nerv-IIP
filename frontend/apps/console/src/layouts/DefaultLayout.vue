<script setup lang="ts">
import type { NavDomain, SideNav } from '@nerv-iip/app-shell'
import { AppShellT } from '@nerv-iip/app-shell'
import { NvDropdownMenuItem } from '@nerv-iip/ui'
import { canAccess, consolePagePermissions } from '@/router/access'
import { useAuthStore } from '@/stores/auth'
import {
  BellIcon,
  Building2Icon,
  InboxIcon,
  KeyRoundIcon,
  LayersIcon,
  ShieldIcon,
  TriangleAlertIcon,
} from '@lucide/vue'
import { storeToRefs } from 'pinia'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'

const { t } = useI18n()

const auth = useAuthStore()
const { principal } = storeToRefs(auth)
const router = useRouter()
const route = useRoute()

function canOpen(path: keyof typeof consolePagePermissions) {
  return canAccess(principal.value?.permissionCodes, consolePagePermissions[path])
}

const notificationItems = computed(() =>
  (
    [
      { title: t('nav.notificationInbox'), icon: InboxIcon, path: '/notifications' },
      { title: t('nav.notificationDlq'), icon: TriangleAlertIcon, path: '/notifications/dlq' },
    ] as const
  )
    .filter(({ path }) => canOpen(path))
    .map(({ path, ...item }) => ({ ...item, to: { path } })),
)

const iamItems = computed(() =>
  (['users', 'roles', 'sessions'] as const)
    .filter((section) => canOpen(`/iam/${section}`))
    .map((section) => ({ title: t(`nav.${section}`), to: { path: `/iam/${section}` } })),
)

// 顶部一级能力区（T 型导航的横向部分）。
const topDomains = computed<NavDomain[]>(() => [
  ...(canOpen('/')
    ? [{ id: 'instances', title: t('nav.instances'), icon: LayersIcon, to: { path: '/' } }]
    : []),
  ...notificationItems.value.slice(0, 1).map(({ to }) => ({
    id: 'notifications',
    title: t('nav.notifications'),
    icon: BellIcon,
    to,
  })),
  ...(canOpen('/business')
    ? [{ id: 'business', title: t('nav.business'), icon: Building2Icon, to: { path: '/business' } }]
    : []),
  ...iamItems.value.slice(0, 1).map(({ to }) => ({
    id: 'iam',
    title: t('nav.iam'),
    icon: ShieldIcon,
    to,
  })),
])

function resolveDomainId(path: string): string {
  if (isUnder(path, '/iam')) return 'iam'
  if (isUnder(path, '/notifications')) return 'notifications'
  if (isUnder(path, '/business')) return 'business'
  return 'instances'
}
function isUnder(path: string, base: string) {
  return path === base || path.startsWith(`${base}/`)
}

const currentDomainId = computed(() => resolveDomainId(route?.path ?? '/'))

const sideNav = computed<SideNav>(() => {
  if (currentDomainId.value === 'notifications' && notificationItems.value.length) {
    return [{ items: notificationItems.value }]
  }

  return currentDomainId.value === 'iam' && iamItems.value.length ? [{ items: iamItems.value }] : []
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
  <AppShellT
    title="Nerv-IIP"
    :top-domains="topDomains"
    :current-domain-id="currentDomainId"
    :side-nav="sideNav"
    :user="shellUser"
    :sign-out-label="t('nav.signOut')"
    @sign-out="signOut"
    :search-enabled="false"
  >
    <template #user-menu-items>
      <NvDropdownMenuItem @select="router.push('/change-password')">
        <KeyRoundIcon class="size-4" aria-hidden="true" />
        {{ t('nav.changePassword') }}
      </NvDropdownMenuItem>
    </template>
    <slot />
  </AppShellT>
</template>
