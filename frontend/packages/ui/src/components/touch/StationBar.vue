<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { cn } from '../../lib/utils'
import type { StatusTone } from '../blocks/status-badge/statusMap'
import StatusDot from '../pro/status/StatusDot.vue'

/**
 * Touch — station board header for tablet/kiosk. Big station identity + live
 * status on the left, free slot (clock, shift, operator) on the right.
 */
withDefaults(
  defineProps<{
    station: string
    statusLabel?: string
    tone?: StatusTone
    pulse?: boolean
    class?: HTMLAttributes['class']
  }>(),
  { tone: 'success', pulse: true },
)
</script>

<template>
  <header
    data-slot="station-bar"
    :class="
      cn(
        'flex items-center justify-between gap-4 rounded-2xl border border-border bg-card px-6 py-4',
        $props.class,
      )
    "
  >
    <div class="flex items-center gap-3">
      <h1 class="text-2xl font-semibold tracking-tight">{{ station }}</h1>
      <span
        v-if="statusLabel"
        class="inline-flex items-center gap-2 rounded-full border border-border bg-muted px-3 py-1 text-sm font-medium"
      >
        <StatusDot :tone="tone" :pulse="pulse" />
        {{ statusLabel }}
      </span>
    </div>
    <div class="flex items-center gap-4 text-right">
      <slot name="right" />
    </div>
  </header>
</template>
