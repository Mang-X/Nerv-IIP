<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed, useSlots } from 'vue'
import { cn } from '../../../lib/utils'

/**
 * Pro — form section: a titled, optionally-described group that lays its fields
 * out on a responsive grid (1 column on narrow screens). Pure presentational
 * container, no state. Use it to break long forms into semantic groups.
 */
const props = withDefaults(
  defineProps<{
    title: string
    description?: string
    columns?: 1 | 2 | 3
    class?: HTMLAttributes['class']
  }>(),
  { columns: 2 },
)

const slots = useSlots()

const gridClass = computed(
  () =>
    ({
      1: 'grid-cols-1',
      2: 'sm:grid-cols-2',
      3: 'sm:grid-cols-2 lg:grid-cols-3',
    })[props.columns],
)
</script>

<template>
  <section data-slot="nv-form-section" :class="cn('flex flex-col gap-4', props.class)">
    <div class="flex items-start justify-between gap-3 border-b border-border pb-3">
      <div class="min-w-0">
        <h3 class="text-sm font-semibold text-foreground">{{ title }}</h3>
        <p v-if="description" class="mt-1 text-sm text-muted-foreground">
          {{ description }}
        </p>
      </div>
      <div v-if="slots.actions" class="flex shrink-0 items-center gap-2">
        <slot name="actions" />
      </div>
    </div>

    <div :class="cn('grid grid-cols-1 gap-4', gridClass)">
      <slot />
    </div>
  </section>
</template>
