<script setup lang="ts">
import type { NavLink, SideNav } from './types'
import {
  SidebarGroup,
  SidebarGroupLabel,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
} from '@nerv-iip/ui'
import { computed } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'

const props = defineProps<{ groups: SideNav }>()

const route = useRoute()
const router = useRouter()

/** Resolved, trailing-slash-free path of a nav link (undefined when it cannot be resolved). */
function linkPath(link: NavLink): string | undefined {
  try {
    const target = router.resolve(link.to).path
    return target.length > 1 && target.endsWith('/') ? target.slice(0, -1) : target
  } catch {
    return undefined
  }
}

/** A link covers the current route when it matches exactly, or is a nested ancestor of it. */
function covers(path: string): boolean {
  if (route.path === path) return true
  const nested = path.split('/').filter(Boolean).length > 1
  return nested && route.path.startsWith(`${path}/`)
}

// Longest match wins: `/erp/sales` must NOT stay highlighted while `/erp/sales/orders`
// is open, but `/mes/work-orders` must stay highlighted on `/mes/work-orders/WO-1`
// (a detail route with no nav entry of its own).
const activePath = computed(() => {
  let best: string | undefined
  for (const group of props.groups) {
    for (const link of group.items) {
      const path = linkPath(link)
      if (path === undefined || !covers(path)) continue
      if (best === undefined || path.length > best.length) best = path
    }
  }
  return best
})

function isActive(link: NavLink): boolean {
  const path = linkPath(link)
  return path !== undefined && path === activePath.value
}
</script>

<template>
  <SidebarGroup v-for="(group, i) in groups" :key="group.label ?? i">
    <SidebarGroupLabel v-if="group.label">{{ group.label }}</SidebarGroupLabel>
    <SidebarMenu>
      <SidebarMenuItem v-for="link in group.items" :key="link.title">
        <SidebarMenuButton as-child :tooltip="link.title" :is-active="isActive(link)">
          <RouterLink :to="link.to">
            <component :is="link.icon" v-if="link.icon" />
            <span>{{ link.title }}</span>
          </RouterLink>
        </SidebarMenuButton>
      </SidebarMenuItem>
    </SidebarMenu>
  </SidebarGroup>
</template>
