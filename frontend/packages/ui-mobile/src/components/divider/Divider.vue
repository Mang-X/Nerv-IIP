<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { useSlots } from 'vue'
import { cn } from '../../lib/utils'

/**
 * Mobile Divider — thin hairline separator (var(--border)). Horizontal by
 * default with optional centered text via the default slot; vertical renders a
 * short inline rule for separating inline actions / metadata.
 */
withDefaults(
  defineProps<{
    direction?: 'horizontal' | 'vertical'
    class?: HTMLAttributes['class']
  }>(),
  { direction: 'horizontal' },
)
const slots = useSlots()
</script>

<template>
  <div
    v-if="direction === 'vertical'"
    data-slot="divider"
    role="separator"
    aria-orientation="vertical"
    :class="cn('inline-block h-[1em] w-px shrink-0 self-center bg-border align-middle', $props.class)"
  />
  <div
    v-else-if="slots.default"
    data-slot="divider"
    role="separator"
    :class="cn('flex items-center gap-3 py-2 text-xs text-muted-foreground', $props.class)"
  >
    <span class="h-px flex-1 bg-border" aria-hidden="true" />
    <span class="shrink-0 whitespace-nowrap"><slot /></span>
    <span class="h-px flex-1 bg-border" aria-hidden="true" />
  </div>
  <div
    v-else
    data-slot="divider"
    role="separator"
    :class="cn('h-px w-full bg-border', $props.class)"
  />
</template>
