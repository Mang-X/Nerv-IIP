<script setup lang="ts">
import type { NavDomain, SideNav } from '@nerv-iip/app-shell'
import { AppShellT } from '@nerv-iip/app-shell'
import { NvDropdownMenuItem } from '@nerv-iip/ui'
import { usePermission } from '@nerv-iip/auth'
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

const { can } = usePermission(() => principal.value?.permissionCodes)
const iamItems = computed(() =>
  (['users', 'roles', 'sessions'] as const)
    .filter((section) => can(`iam.${section}.read`))
    .map((section) => ({ title: t(`nav.${section}`), to: { path: `/iam/${section}` } })),
)

// 顶部一级能力区（T 型导航的横向部分）。
const topDomains = computed<NavDomain[]>(() => [
  { id: 'instances', title: t('nav.instances'), icon: LayersIcon, to: { name: '/' } },
  {
    id: 'notifications',
    title: t('nav.notifications'),
    icon: BellIcon,
    to: { path: '/notifications' },
  },
  { id: 'business', title: t('nav.business'), icon: Building2Icon, to: { path: '/business' } },
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
  if (currentDomainId.value === 'notifications') {
    return [
      {
        items: [
          { title: t('nav.notificationInbox'), icon: InboxIcon, to: { path: '/notifications' } },
          {
            title: t('nav.notificationDlq'),
            icon: TriangleAlertIcon,
            to: { path: '/notifications/dlq' },
          },
        ],
      },
    ]
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

function openSearch() {
  // 命令/对象搜索入口占位（与 Business Console 一致，后续接入）。
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
    @open-search="openSearch"
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
