<script setup lang="ts">
import { AppShellT } from '@nerv-iip/app-shell'
import { ThemePicker, ThemeToggle } from '@nerv-iip/ui'
import { storeToRefs } from 'pinia'
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import {
  BUSINESS_DOMAINS,
  DOMAIN_SIDE_NAV,
  permittedBy,
  resolveDomainId,
} from '@/navigation'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const { principal } = storeToRefs(auth)

const permissionCodes = computed(() => principal.value ? principal.value.permissionCodes ?? [] : undefined)

const currentDomainId = computed(() => resolveDomainId(route.path))

// Top capability areas, RBAC-filtered (permissive default — see navigation.ts).
const topDomains = computed(() => permittedBy(BUSINESS_DOMAINS, permissionCodes.value))

// Current domain's side nav, with empty groups dropped after permission filtering.
const sideNav = computed(() =>
  (DOMAIN_SIDE_NAV[currentDomainId.value] ?? [])
    .map((group) => ({ ...group, items: permittedBy(group.items, permissionCodes.value) }))
    .filter((group) => group.items.length > 0),
)

const shellUser = computed(() => {
  const p = principal.value
  if (!p) return undefined
  return { name: p.loginName ?? p.principalId ?? '已登录用户', email: p.email }
})

async function signOut() {
  await auth.logout()
  await router.push('/login')
}

function openSearch() {
  // Command/object search entry placeholder — implemented in FE-13 (backend #271).
}
</script>

<template>
  <AppShellT
    title="Nerv-IIP 业务控制台"
    :top-domains="topDomains"
    :current-domain-id="currentDomainId"
    :side-nav="sideNav"
    :user="shellUser"
    sign-out-label="退出登录"
    @sign-out="signOut"
    @open-search="openSearch"
  >
    <template #header-actions>
      <ThemePicker />
      <ThemeToggle />
    </template>
    <slot />
  </AppShellT>
</template>
