<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { cn } from '../../lib/utils'

/**
 * Mobile Skeleton — placeholder block with a subtle left-to-right shimmer while
 * content loads. `variant` picks the base shape; pass sizing via `class`
 * (e.g. `class="w-32"`). Shimmer is suppressed under reduced-motion.
 */
type Variant = 'text' | 'rect' | 'circle'

withDefaults(
  defineProps<{
    variant?: Variant
    class?: HTMLAttributes['class']
  }>(),
  { variant: 'text' },
)

const variantClass: Record<Variant, string> = {
  text: 'h-3.5 w-full rounded',
  rect: 'h-20 w-full rounded-[10px]',
  circle: 'size-10 rounded-full',
}
</script>

<template>
  <span
    data-slot="skeleton"
    aria-hidden="true"
    :class="cn('ds-skeleton block bg-muted', variantClass[variant], $props.class)"
  />
</template>

<style scoped>
.ds-skeleton {
  position: relative;
  overflow: hidden;
}
.ds-skeleton::after {
  content: '';
  position: absolute;
  inset: 0;
  transform: translateX(-100%);
  background: linear-gradient(
    90deg,
    transparent,
    color-mix(in oklab, var(--foreground) 8%, transparent),
    transparent
  );
  animation: ds-skeleton-shimmer 1.4s var(--ease-out-quart, ease-out) infinite;
}
@keyframes ds-skeleton-shimmer {
  100% {
    transform: translateX(100%);
  }
}
@media (prefers-reduced-motion: reduce) {
  .ds-skeleton::after {
    animation: none;
  }
}
</style>
