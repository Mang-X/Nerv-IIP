<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { useSlots } from 'vue'
import { cn } from '../../lib/utils'

defineProps<{ class?: HTMLAttributes['class'] }>()
const slots = useSlots()
</script>

<template>
  <div :class="cn('flex h-dvh flex-col bg-background text-foreground', $props.class)">
    <header
      v-if="slots.header"
      data-shell="header"
      class="pt-safe px-safe sticky top-0 z-20 shrink-0 border-b border-border/70 bg-background/80 backdrop-blur-xl supports-[backdrop-filter]:bg-background/70"
    >
      <slot name="header" />
    </header>

    <main
      data-shell="content"
      class="px-safe min-h-0 flex-1 overflow-y-auto overscroll-contain [scrollbar-width:none] [&::-webkit-scrollbar]:hidden"
    >
      <slot />
    </main>

    <footer
      v-if="slots.footer"
      data-shell="footer"
      class="pb-safe px-safe sticky bottom-0 z-20 shrink-0 border-t border-border/70 bg-background/80 backdrop-blur-xl supports-[backdrop-filter]:bg-background/70"
    >
      <slot name="footer" />
    </footer>
  </div>
</template>
