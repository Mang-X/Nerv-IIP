<script setup lang="ts">
import { cn } from '../../../lib/utils'
const props = defineProps<{ class?: string }>()
</script>
<template>
  <div
    data-slot="sidebar-group-label"
    :class="cn(
      'text-sidebar-foreground/70 ring-sidebar-ring flex h-8 shrink-0 items-center rounded-md px-2 text-xs font-medium outline-none transition-[margin,opacity] duration-200 ease-linear focus-visible:ring-2',
      'group-data-[collapsible=icon]:-mt-8 group-data-[collapsible=icon]:opacity-0',
      props.class
    )"
  >
    <slot />
  </div>
</template>
