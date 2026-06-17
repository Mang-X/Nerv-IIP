<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { cn } from '../../lib/utils'

/**
 * AppHeader — the top application bar (Nuxt UI `Header` equivalent): `#leading`
 * (logo / menu toggle), default slot (title or center nav), `#trailing`
 * (actions). Sticky + glass by default; turn off with `:sticky="false"`.
 */
withDefaults(
  defineProps<{ sticky?: boolean; class?: HTMLAttributes['class'] }>(),
  { sticky: true },
)
</script>

<template>
  <header
    data-slot="app-header"
    :class="
      cn(
        'flex h-14 shrink-0 items-center gap-3 border-b border-border px-4',
        sticky &&
          'sticky top-0 z-30 bg-background/80 backdrop-blur-xl supports-[backdrop-filter]:bg-background/65',
        !sticky && 'bg-background',
        $props.class,
      )
    "
  >
    <div v-if="$slots.leading" class="flex items-center gap-2">
      <slot name="leading" />
    </div>
    <slot />
    <div v-if="$slots.trailing" class="ms-auto flex items-center gap-1.5">
      <slot name="trailing" />
    </div>
  </header>
</template>
