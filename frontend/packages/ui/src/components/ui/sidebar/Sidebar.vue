<script setup lang="ts">
import { useSidebar } from './utils'
import { cn } from '../../../lib/utils'

const props = defineProps<{
  side?: 'left' | 'right'
  variant?: 'sidebar' | 'floating' | 'inset'
  collapsible?: 'offcanvas' | 'icon' | 'none'
  class?: string
}>()

const { state, isMobile } = useSidebar()
</script>

<template>
  <div
    :data-state="state"
    :data-collapsible="state === 'collapsed' ? (collapsible ?? 'offcanvas') : ''"
    :data-variant="variant ?? 'sidebar'"
    :data-side="side ?? 'left'"
    data-slot="sidebar"
    :class="cn('group peer hidden md:block text-sidebar-foreground', props.class)"
    :style="{ '--sidebar-width': '16rem', '--sidebar-width-icon': '3rem' }"
  >
    <!-- Fixed width holder -->
    <div
      :class="cn(
        'relative w-(--sidebar-width) bg-transparent transition-[width] duration-200 ease-linear',
        'group-data-[collapsible=offcanvas]:w-0',
        'group-data-[collapsible=icon]:w-(--sidebar-width-icon)',
        'group-data-[side=right]:rotate-180',
      )"
    />
    <!-- Actual sidebar -->
    <div
      :class="cn(
        'fixed inset-y-0 z-10 hidden h-svh w-(--sidebar-width) flex-col bg-sidebar',
        'transition-[left,right,width] duration-200 ease-linear md:flex',
        'left-0 group-data-[collapsible=offcanvas]:left-[calc(var(--sidebar-width)*-1)]',
        'group-data-[collapsible=icon]:w-(--sidebar-width-icon) group-data-[collapsible=icon]:overflow-hidden',
        'group-data-[side=right]:right-0 group-data-[side=right]:left-auto group-data-[side=right]:group-data-[collapsible=offcanvas]:right-[calc(var(--sidebar-width)*-1)]',
        variant === 'floating' || variant === 'inset' ? 'p-2 top-2 h-[calc(100svh-theme(spacing.4))] group-data-[side=left]:border-r-0' : 'border-r border-sidebar-border',
      )"
    >
      <slot />
    </div>
  </div>
</template>
