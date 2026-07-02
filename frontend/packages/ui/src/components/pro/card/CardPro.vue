<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { cn } from '../../../lib/utils'

/**
 * Pro — copy-rebuilt card (does NOT touch原版 Card). Hairline ring + a faint
 * inset top highlight read as a single crisp surface; `interactive` adds a
 * restrained hover lift for clickable cards.
 */
withDefaults(
  defineProps<{
    interactive?: boolean
    class?: HTMLAttributes['class']
  }>(),
  { interactive: false },
)
</script>

<template>
  <div
    data-slot="card-pro"
    :class="
      cn(
        'ds-card relative rounded-xl bg-card text-card-foreground',
        interactive && 'ds-card-interactive',
        $props.class,
      )
    "
  >
    <slot />
  </div>
</template>

<style scoped>
.ds-card {
  box-shadow:
    inset 0 1px 0 0 color-mix(in oklch, white 6%, transparent),
    0 0 0 1px var(--border),
    var(--shadow-xs);
  transition:
    box-shadow 0.22s var(--ease-out-quart, ease-out),
    transform 0.22s var(--ease-out-quart, ease-out);
}
.ds-card-interactive {
  cursor: pointer;
}
.ds-card-interactive:hover {
  transform: translateY(-2px);
  box-shadow:
    inset 0 1px 0 0 color-mix(in oklch, white 8%, transparent),
    0 0 0 1px color-mix(in oklch, var(--foreground) 16%, transparent),
    var(--shadow-md);
}
@media (prefers-reduced-motion: reduce) {
  .ds-card-interactive:hover {
    transform: none;
  }
}
</style>
