<script setup lang="ts">
import type { NavLink, SideNav } from './types'
import {
  SidebarGroup,
  SidebarGroupLabel,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
} from '@nerv-iip/ui'
import { RouterLink, useRoute, useRouter } from 'vue-router'

defineProps<{ groups: SideNav }>()

const route = useRoute()
const router = useRouter()

function isActive(link: NavLink): boolean {
  try {
    const target = router.resolve(link.to).path
    const normalized = target.length > 1 && target.endsWith('/') ? target.slice(0, -1) : target
    const nested = normalized.split('/').filter(Boolean).length > 1
    return route.path === normalized || (nested && route.path.startsWith(`${normalized}/`))
  }
  catch {
    return false
  }
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
