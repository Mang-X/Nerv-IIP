<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed } from 'vue'
import { cn } from '../../lib/utils'

/**
 * Mobile Badge — count / dot overlay (Vant / tdesign-mobile style). Wraps a slot
 * (icon, avatar) and pins a red count or dot to the top-right corner.
 */
const props = withDefaults(
  defineProps<{
    count?: number
    dot?: boolean
    max?: number
    class?: HTMLAttributes['class']
  }>(),
  { count: 0, dot: false, max: 99 },
)

const show = computed(() => props.dot || props.count > 0)
const text = computed(() => (props.count > props.max ? `${props.max}+` : String(props.count)))
</script>

<template>
  <span data-slot="badge" :class="cn('relative inline-flex', $props.class)">
    <slot />
    <span
      v-if="show"
      :class="
        cn(
          'absolute rounded-full bg-destructive text-white ring-2 ring-background',
          dot
            ? '-top-0.5 -right-0.5 size-2.5'
            : 'top-0 right-0 flex h-4 min-w-4 -translate-y-1/3 translate-x-1/3 items-center justify-center px-1 text-[10px] leading-none font-semibold tabular-nums',
        )
      "
    >
      <template v-if="!dot">{{ text }}</template>
    </span>
  </span>
</template>
