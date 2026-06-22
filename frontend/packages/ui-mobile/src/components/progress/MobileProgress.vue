<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed } from 'vue'
import { cn } from '../../lib/utils'

/**
 * Mobile Progress — linear determinate bar. Rounded muted track with a tone-colored
 * fill that glides on value change (reduced-motion safe). Optional trailing percent
 * label. For PDA flows like 报工进度 / 上传进度.
 */
type Tone = 'brand' | 'success' | 'warning' | 'danger'

const props = withDefaults(
  defineProps<{
    value?: number
    tone?: Tone
    showLabel?: boolean
    class?: HTMLAttributes['class']
  }>(),
  { value: 0, tone: 'brand', showLabel: false },
)

const pct = computed(() => Math.min(100, Math.max(0, props.value)))

const fillClass: Record<Tone, string> = {
  brand: 'bg-brand',
  success: 'bg-success',
  warning: 'bg-warning',
  danger: 'bg-destructive',
}
</script>

<template>
  <div
    data-slot="progress"
    :class="cn('flex items-center gap-2.5', props.class)"
  >
    <div
      class="h-2 min-w-0 flex-1 overflow-hidden rounded-full bg-muted"
      role="progressbar"
      :aria-valuenow="Math.round(pct)"
      aria-valuemin="0"
      aria-valuemax="100"
    >
      <div
        class="ds-progress-fill h-full rounded-full"
        :class="fillClass[tone]"
        :style="{ width: `${pct}%` }"
      />
    </div>
    <span
      v-if="showLabel"
      class="shrink-0 text-[13px] font-medium tabular-nums text-muted-foreground"
    >
      {{ Math.round(pct) }}%
    </span>
  </div>
</template>

<style scoped>
.ds-progress-fill {
  transition: width 0.4s var(--ease-out-quart, ease-out);
}
@media (prefers-reduced-motion: reduce) {
  .ds-progress-fill {
    transition: none;
  }
}
</style>
