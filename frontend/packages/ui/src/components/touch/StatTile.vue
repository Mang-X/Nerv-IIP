<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed } from 'vue'
import { cn } from '../../lib/utils'

/**
 * Touch — oversized, glanceable KPI tile for station boards. Big number readable
 * across a workshop; an optional tone tints the surface for at-a-glance health.
 */
type Tone = 'neutral' | 'brand' | 'success' | 'warning' | 'danger'

const props = withDefaults(
  defineProps<{
    label: string
    value: string | number
    unit?: string
    tone?: Tone
    class?: HTMLAttributes['class']
  }>(),
  { tone: 'neutral' },
)

const toneClass = computed(
  () =>
    ({
      neutral: 'border-border bg-card',
      brand: 'border-brand/25 bg-brand/8',
      success: 'border-success/25 bg-success/8',
      warning: 'border-warning/30 bg-warning/10',
      danger: 'border-destructive/30 bg-destructive/10',
    })[props.tone],
)
const valueTone = computed(
  () =>
    ({
      neutral: 'text-foreground',
      brand: 'text-brand',
      success: 'text-success',
      warning: 'text-warning',
      danger: 'text-destructive',
    })[props.tone],
)
</script>

<template>
  <div
    data-slot="stat-tile"
    :class="cn('flex flex-col justify-between rounded-2xl border p-5', toneClass, props.class)"
  >
    <p class="text-sm font-medium text-muted-foreground">{{ label }}</p>
    <p class="mt-2 flex items-baseline gap-1.5">
      <span :class="cn('text-4xl font-semibold tracking-tight tabular-nums', valueTone)">{{
        value
      }}</span>
      <span v-if="unit" class="text-base text-muted-foreground">{{ unit }}</span>
    </p>
    <slot />
  </div>
</template>
