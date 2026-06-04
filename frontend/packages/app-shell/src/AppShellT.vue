<script setup lang="ts">
import type { NavDomain, ShellUser, SideNav } from './types'
import {
  AppShellInset,
  Avatar,
  AvatarFallback,
  Button,
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
  SidebarGroup,
  SidebarGroupLabel,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  cn,
} from '@nerv-iip/ui'
import { ClockIcon, LogOutIcon, SearchIcon, StarIcon } from 'lucide-vue-next'
import { computed, onBeforeUnmount, onMounted } from 'vue'
import { RouterLink } from 'vue-router'
import NavSide from './NavSide.vue'
import NavTopDomains from './NavTopDomains.vue'
import type { NavLink } from './types'

const props = withDefaults(
  defineProps<{
    title: string
    topDomains: NavDomain[]
    currentDomainId?: string
    sideNav?: SideNav
    maxVisibleDomains?: number
    user?: ShellUser
    signOutLabel?: string
    /** Command-search button placeholder text. */
    searchLabel?: string
    /** Recent items (already permission-filtered by the consumer). */
    recent?: NavLink[]
    /** Starred items (already permission-filtered by the consumer). */
    starred?: NavLink[]
  }>(),
  {
    signOutLabel: '退出登录',
    searchLabel: '搜索菜单、单号、物料…',
  },
)

const emit = defineEmits<{ signOut: [], openSearch: [] }>()

const initials = computed(() => (props.user?.name ?? '').slice(0, 2).toUpperCase() || 'NN')
const sideGroups = computed<SideNav>(() => props.sideNav ?? [])
const pinned = computed<{ label: string, icon: typeof StarIcon, items: NavLink[] }[]>(() =>
  [
    { label: '星标', icon: StarIcon, items: props.starred ?? [] },
    { label: '最近访问', icon: ClockIcon, items: props.recent ?? [] },
  ].filter((g) => g.items.length > 0),
)

function onKeydown(e: KeyboardEvent) {
  if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === 'k') {
    e.preventDefault()
    emit('openSearch')
  }
}
onMounted(() => window.addEventListener('keydown', onKeydown))
onBeforeUnmount(() => window.removeEventListener('keydown', onKeydown))
</script>

<template>
  <AppShellInset>
    <template #sidebar-header>
      <RouterLink :to="{ path: '/' }" class="flex items-center gap-2 px-1 py-1.5">
        <div class="flex size-8 shrink-0 items-center justify-center rounded-lg bg-sidebar-primary text-sm font-extrabold text-sidebar-primary-foreground">
          N
        </div>
        <span class="truncate text-sm font-semibold">{{ title }}</span>
      </RouterLink>
    </template>

    <template #sidebar>
      <NavSide :groups="sideGroups" />
      <SidebarGroup v-for="group in pinned" :key="group.label">
        <SidebarGroupLabel>{{ group.label }}</SidebarGroupLabel>
        <SidebarMenu>
          <SidebarMenuItem v-for="link in group.items" :key="link.title">
            <SidebarMenuButton as-child :tooltip="link.title">
              <RouterLink :to="link.to">
                <component :is="group.icon" />
                <span>{{ link.title }}</span>
              </RouterLink>
            </SidebarMenuButton>
          </SidebarMenuItem>
        </SidebarMenu>
      </SidebarGroup>
    </template>

    <template #header>
      <NavTopDomains
        :domains="topDomains"
        :current-domain-id="currentDomainId"
        :max-visible="maxVisibleDomains"
        class="flex-1"
      />

      <div class="ml-2 flex shrink-0 items-center gap-1">
        <Button
          type="button"
          variant="outline"
          size="sm"
          class="hidden h-8 gap-2 text-muted-foreground md:inline-flex"
          :aria-label="searchLabel"
          @click="emit('openSearch')"
        >
          <SearchIcon class="size-4" aria-hidden="true" />
          <span class="max-w-40 truncate">{{ searchLabel }}</span>
          <kbd class="ml-2 rounded border bg-muted px-1 text-[10px] font-medium">⌘K</kbd>
        </Button>
        <Button
          type="button"
          variant="ghost"
          size="icon"
          class="md:hidden"
          aria-label="搜索"
          @click="emit('openSearch')"
        >
          <SearchIcon class="size-4" aria-hidden="true" />
        </Button>

        <slot name="header-actions" />

        <DropdownMenu v-if="user">
          <DropdownMenuTrigger as-child>
            <Button type="button" variant="ghost" size="icon" class="rounded-full" aria-label="用户菜单">
              <Avatar class="size-7">
                <AvatarFallback class="text-xs">{{ initials }}</AvatarFallback>
              </Avatar>
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end" class="w-56">
            <DropdownMenuLabel class="font-normal">
              <div class="grid gap-0.5">
                <span class="truncate text-sm font-semibold">{{ user.name }}</span>
                <span v-if="user.email" class="truncate text-xs text-muted-foreground">{{ user.email }}</span>
              </div>
            </DropdownMenuLabel>
            <DropdownMenuSeparator />
            <DropdownMenuItem @select="emit('signOut')">
              <LogOutIcon class="size-4" aria-hidden="true" />
              {{ signOutLabel }}
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      </div>
    </template>

    <slot />
  </AppShellInset>
</template>
