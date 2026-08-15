<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed } from 'vue'
import { cn } from '../../../lib/utils'
import { resolveStatus, type StatusTone } from '../../blocks/status-badge/statusMap'
import NvStatusDot from './NvStatusDot.vue'

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

// 传了 `label` 时词表结果不上屏（只还拿它的 tone），漏词就不是可见缺陷——照报会把
// 开发期告警刷成噪声，真漏词反而看不见。
const resolved = computed(() =>
  resolveStatus(props.value, { warnOnMissing: props.label === undefined }),
)
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
    <NvStatusDot :tone="tone" :pulse="pulse" size="sm" />
    <span class="truncate">{{ label }}</span>
  </span>
</template>
