<script setup lang="ts">
import { computed } from 'vue'
import { cn } from '../../../lib/utils'
import { Primitive } from 'reka-ui'

const props = defineProps<{
  asChild?: boolean
  isActive?: boolean
  size?: 'sm' | 'md'
  class?: string
}>()

const buttonClass = computed(() =>
  cn(
    'text-sidebar-foreground ring-sidebar-ring hover:bg-sidebar-accent hover:text-sidebar-accent-foreground active:bg-sidebar-accent active:text-sidebar-accent-foreground',
    '[&>svg]:size-4 [&>svg]:shrink-0',
    'flex h-7 min-w-0 -translate-x-px items-center gap-2 overflow-hidden rounded-md px-2 text-sm outline-none focus-visible:ring-2',
    'disabled:pointer-events-none disabled:opacity-50 aria-disabled:pointer-events-none aria-disabled:opacity-50',
    'data-[active=true]:bg-sidebar-accent data-[active=true]:text-sidebar-accent-foreground',
    props.size === 'sm' && 'text-xs',
    props.isActive && 'bg-sidebar-accent text-sidebar-accent-foreground',
    props.class,
  )
)
</script>

<template>
  <Primitive
    data-slot="sidebar-menu-sub-button"
    :data-active="isActive"
    :as="asChild ? 'template' : 'a'"
    :as-child="asChild"
    :class="buttonClass"
  >
    <slot />
  </Primitive>
</template>
