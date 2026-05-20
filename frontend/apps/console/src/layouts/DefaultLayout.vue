<script setup lang="ts">
import { AppShell } from '@nerv-iip/app-shell'
import { useAuthStore } from '@/stores/auth'
import { storeToRefs } from 'pinia'
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import type { RouteLocationRaw } from 'vue-router'

interface ConsoleNavLinkItem {
  label: string
  to: RouteLocationRaw
}

interface ConsoleNavGroupItem {
  children: ConsoleNavLinkItem[]
  label: string
}

type ConsoleNavItem = ConsoleNavLinkItem | ConsoleNavGroupItem

const navItems = [
  { label: 'Instances', to: { name: '/' } },
  {
    label: 'IAM',
    children: [
      { label: 'Users', to: { path: '/iam/users' } },
      { label: 'Roles', to: { path: '/iam/roles' } },
      { label: 'Sessions', to: { path: '/iam/sessions' } },
    ],
  },
] satisfies ConsoleNavItem[]
const auth = useAuthStore()
const { principal } = storeToRefs(auth)
const router = useRouter()

const shellUser = computed(() => {
  const currentPrincipal = principal.value

  if (!currentPrincipal) {
    return undefined
  }

  return {
    email: currentPrincipal.email,
    loginName: currentPrincipal.loginName ?? currentPrincipal.principalId ?? 'Authenticated user',
  }
})

async function signOut() {
  await auth.logout()
  await router.push('/login')
}
</script>

<template>
  <AppShell title="Nerv-IIP" :nav-items="navItems" :user="shellUser" @sign-out="signOut">
    <slot />
  </AppShell>
</template>
