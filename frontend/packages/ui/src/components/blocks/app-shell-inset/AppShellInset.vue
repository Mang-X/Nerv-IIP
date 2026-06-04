<script setup lang="ts">
import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarHeader,
  SidebarInset,
  SidebarProvider,
  SidebarRail,
  SidebarTrigger,
} from '../../ui/sidebar'
import { Separator } from '../../ui/separator'

withDefaults(
  defineProps<{
    /** Sidebar collapse behaviour. */
    collapsible?: 'offcanvas' | 'icon' | 'none'
  }>(),
  { collapsible: 'icon' },
)
</script>

<template>
  <SidebarProvider>
    <Sidebar variant="inset" :collapsible="collapsible">
      <SidebarHeader v-if="$slots['sidebar-header']">
        <slot name="sidebar-header" />
      </SidebarHeader>
      <SidebarContent>
        <slot name="sidebar" />
      </SidebarContent>
      <SidebarFooter v-if="$slots['sidebar-footer']">
        <slot name="sidebar-footer" />
      </SidebarFooter>
      <SidebarRail />
    </Sidebar>

    <SidebarInset>
      <header
        class="flex h-14 shrink-0 items-center gap-2 border-b px-4 transition-[width,height] ease-linear"
      >
        <SidebarTrigger class="-ml-1" />
        <Separator
          v-if="$slots.header"
          orientation="vertical"
          class="mr-2 data-[orientation=vertical]:h-4 data-[orientation=vertical]:self-center"
        />
        <slot name="header" />
      </header>
      <main class="flex flex-1 flex-col gap-4 p-4 md:gap-6 md:p-6">
        <slot />
      </main>
    </SidebarInset>
  </SidebarProvider>
</template>
