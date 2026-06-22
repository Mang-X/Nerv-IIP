<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed, useSlots } from 'vue'
import { cn } from '../../lib/utils'

/**
 * Page — a responsive layout with optional left and right columns (Nuxt UI
 * style). On large screens it becomes a 10-col grid (left 2 / center 6–8 /
 * right 2); with no asides it's a centered single column. Asides are
 * desktop-only — wrap their content in `PageAside` for sticky behaviour.
 */
const props = defineProps<{ class?: HTMLAttributes['class'] }>()
const slots = useSlots()
const hasLeft = computed(() => !!slots.left)
const hasRight = computed(() => !!slots.right)
const isGrid = computed(() => hasLeft.value || hasRight.value)
const centerSpan = computed(() =>
  hasLeft.value && hasRight.value ? 'lg:col-span-6' : 'lg:col-span-8',
)
</script>

<template>
  <div
    data-slot="page"
    :class="cn('mx-auto w-full max-w-6xl px-4 sm:px-6 lg:px-8', props.class)"
  >
    <div :class="isGrid && 'lg:grid lg:grid-cols-10 lg:gap-8'">
      <aside v-if="hasLeft" class="hidden lg:col-span-2 lg:block">
        <slot name="left" />
      </aside>
      <div class="min-w-0" :class="isGrid && centerSpan">
        <slot />
      </div>
      <aside v-if="hasRight" class="hidden lg:col-span-2 lg:block">
        <slot name="right" />
      </aside>
    </div>
  </div>
</template>
