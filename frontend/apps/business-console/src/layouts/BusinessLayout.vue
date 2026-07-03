<script setup lang="ts">
import { AppShellT } from '@nerv-iip/app-shell'
import { ThemePicker, ThemeToggle } from '@nerv-iip/ui'
import { storeToRefs } from 'pinia'
import { computed, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useBusinessContextStore } from '@/stores/businessContext'
import {
  BUSINESS_DOMAINS,
  DOMAIN_SIDE_NAV,
  permittedBy,
  resolveDomainId,
} from '@/navigation'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const businessContext = useBusinessContextStore()
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

const hasSelectedBusinessContext = computed(() =>
  businessContext.organizationId.trim().length > 0 && businessContext.environmentId.trim().length > 0,
)
const showBusinessContextEmptyState = computed(() =>
  Boolean(principal.value) && !hasSelectedBusinessContext.value,
)

watch(
  principal,
  (value) => {
    businessContext.patchContext({
      organizationId: value?.organizationId ?? '',
      environmentId: value?.environmentId ?? '',
    })
  },
  { immediate: true },
)

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
    <section
      v-if="showBusinessContextEmptyState"
      class="mx-4 mt-4 rounded-md border border-warning/40 bg-warning/10 px-4 py-3 text-sm text-foreground"
      role="status"
    >
      <div class="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <p class="font-medium">未选择业务上下文</p>
          <p class="text-muted-foreground">
            当前账号未返回组织或环境。请联系管理员补齐业务上下文，或退出后使用已配置账号登录。
          </p>
        </div>
        <button
          class="inline-flex h-9 items-center justify-center rounded-md border border-border bg-background px-3 text-sm font-medium text-foreground hover:bg-muted"
          type="button"
          @click="signOut"
        >
          重新登录
        </button>
      </div>
    </section>
    <slot />
  </AppShellT>
</template>
