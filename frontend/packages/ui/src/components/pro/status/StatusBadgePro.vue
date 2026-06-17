<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed } from 'vue'
import { cn } from '../../../lib/utils'
import { resolveStatus, type StatusTone } from '../../blocks/status-badge/statusMap'
import StatusDot from './StatusDot.vue'

/**
 * Pro — status badge with a leading tone dot. Reuses the shared status map
 * (label + tone resolution) so it stays consistent with the原版 StatusBadge,
 * but reads richer: dot + tinted pill, optional live pulse for active states.
 */
const props = defineProps<{
  value?: string | null
  label?: string
  tone?: StatusTone
  pulse?: boolean
  class?: HTMLAttributes['class']
}>()

const resolved = computed(() => resolveStatus(props.value))
const tone = computed<StatusTone>(() => props.tone ?? resolved.value.tone)
const label = computed(() => props.label ?? resolved.value.label)

const toneClass: Record<StatusTone, string> = {
  success: 'border-success/25 bg-success/10 text-success-strong',
  warning: 'border-warning/25 bg-warning/10 text-warning-strong',
  danger: 'border-destructive/25 bg-destructive/10 text-destructive-strong',
  info: 'border-brand/25 bg-brand/10 text-brand-strong',
  neutral: 'border-border bg-muted text-muted-foreground',
}
</script>

<template>
  <span
    :aria-label="`状态：${label}`"
    :class="
      cn(
        'inline-flex h-6 max-w-44 items-center gap-1.5 truncate rounded-full border px-2.5 text-xs font-medium',
        toneClass[tone],
        props.class,
      )
    "
  >
    <StatusDot :tone="tone" :pulse="pulse" size="sm" />
    <span class="truncate">{{ label }}</span>
  </span>
</template>
