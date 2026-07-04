<script setup lang="ts">
import type { NavDomain, SideNav } from '@nerv-iip/app-shell'
import { AppShellT } from '@nerv-iip/app-shell'
import { useAuthStore } from '@/stores/auth'
import { BellIcon, Building2Icon, InboxIcon, LayersIcon, ShieldIcon, TriangleAlertIcon } from 'lucide-vue-next'
import { storeToRefs } from 'pinia'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'

const { t } = useI18n()

const auth = useAuthStore()
const { principal } = storeToRefs(auth)
const router = useRouter()
const route = useRoute()

// 顶部一级能力区（T 型导航的横向部分）。
const topDomains = computed<NavDomain[]>(() => [
  { id: 'instances', title: t('nav.instances'), icon: LayersIcon, to: { name: '/' } },
  { id: 'notifications', title: t('nav.notifications'), icon: BellIcon, to: { path: '/notifications' } },
  { id: 'business', title: t('nav.business'), icon: Building2Icon, to: { path: '/business' } },
  { id: 'iam', title: t('nav.iam'), icon: ShieldIcon, to: { path: '/iam/users' } },
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
    return [{
      items: [
        { title: t('nav.notificationInbox'), icon: InboxIcon, to: { path: '/notifications' } },
        { title: t('nav.notificationDlq'), icon: TriangleAlertIcon, to: { path: '/notifications/dlq' } },
      ],
    }]
  }

  if (currentDomainId.value !== 'iam') return []

  return [{
    items: [
      { title: t('nav.users'), to: { path: '/iam/users' } },
      { title: t('nav.roles'), to: { path: '/iam/roles' } },
      { title: t('nav.sessions'), to: { path: '/iam/sessions' } },
    ],
  }]
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
    <slot />
  </AppShellT>
</template>
