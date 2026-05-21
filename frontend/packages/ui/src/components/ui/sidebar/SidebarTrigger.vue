<script setup lang="ts">
import { useSidebar } from './utils'
import { PanelLeftIcon } from 'lucide-vue-next'
import { cn } from '../../../lib/utils'

const props = defineProps<{ class?: string }>()
const { toggleSidebar } = useSidebar()
</script>

<template>
  <button
    data-sidebar="trigger"
    data-slot="sidebar-trigger"
    :class="cn('size-7 rounded-[min(var(--radius-md),12px)] inline-flex shrink-0 items-center justify-center whitespace-nowrap transition-all outline-none select-none disabled:pointer-events-none disabled:opacity-50 hover:bg-muted hover:text-foreground [&_svg]:size-4 [&_svg]:shrink-0', props.class)"
    @click="toggleSidebar"
  >
    <PanelLeftIcon />
    <span class="sr-only">Toggle Sidebar</span>
  </button>
</template>
