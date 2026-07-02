<script setup lang="ts">
import type { Component } from 'vue'
import type { RouteLocationRaw } from 'vue-router'
import { RouterLink } from 'vue-router'
import {
  Separator,
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarHeader,
  SidebarInset,
  SidebarProBrand,
  SidebarProvider,
  SidebarRail,
  SidebarTrigger,
} from '@nerv-iip/ui'
import NavMain from './NavMain.vue'
import NavUser from './NavUser.vue'

export interface NavSubItem {
  title: string
  to: RouteLocationRaw
}

export interface NavItem {
  title: string
  to?: RouteLocationRaw
  icon?: Component
  isActive?: boolean
  items?: NavSubItem[]
}

defineProps<{
  title: string
  navItems: NavItem[]
  navLabel?: string
  signOutLabel?: string
  user?: {
    name: string
    email?: string
  }
}>()

defineEmits<{
  signOut: []
}>()
</script>

<template>
  <SidebarProvider>
    <Sidebar collapsible="icon">
      <SidebarHeader>
        <SidebarProBrand :as="RouterLink" :to="{ path: '/' }" :name="title" logo="N" :caret="false" />
      </SidebarHeader>
      <SidebarContent>
        <NavMain :items="navItems" :label="navLabel" />
      </SidebarContent>
      <SidebarFooter v-if="user">
        <NavUser :sign-out-label="signOutLabel" :user="user" @sign-out="$emit('signOut')" />
      </SidebarFooter>
      <SidebarRail />
    </Sidebar>

    <SidebarInset>
      <header class="flex h-16 shrink-0 items-center gap-2 border-b px-4 transition-[width,height] ease-linear group-has-data-[collapsible=icon]/sidebar-wrapper:h-12">
        <SidebarTrigger class="-ml-1" />
        <Separator
          orientation="vertical"
          class="mr-2 data-[orientation=vertical]:h-4 data-[orientation=vertical]:self-auto"
        />
        <slot name="header" />
      </header>
      <div class="flex flex-1 flex-col gap-4 p-4">
        <slot />
      </div>
    </SidebarInset>
  </SidebarProvider>
</template>
