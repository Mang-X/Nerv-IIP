<script setup lang="ts">
import { useSlots } from 'vue'
import { cn } from '../../lib/utils'

defineProps<{ class?: string }>()
const slots = useSlots()
</script>

<template>
  <div :class="cn('flex h-dvh flex-col bg-background text-foreground', $props.class)">
    <header
      v-if="slots.header"
      data-shell="header"
      class="pt-safe px-safe sticky top-0 z-20 shrink-0 border-b border-border bg-background"
    >
      <slot name="header" />
    </header>

    <main data-shell="content" class="px-safe min-h-0 flex-1 overflow-y-auto">
      <slot />
    </main>

    <footer
      v-if="slots.footer"
      data-shell="footer"
      class="pb-safe px-safe sticky bottom-0 z-20 shrink-0 border-t border-border bg-background"
    >
      <slot name="footer" />
    </footer>
  </div>
</template>
