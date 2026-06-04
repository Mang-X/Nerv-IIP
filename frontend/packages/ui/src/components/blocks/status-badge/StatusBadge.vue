<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed } from 'vue'
import { Badge } from '../../ui/badge'
import { cn } from '../../../lib/utils'
import { resolveStatus, type StatusTone } from './statusMap'

const props = defineProps<{
  /** Raw status value (e.g. 'running', 'ready', 'blocked'); mapped to a localized label + tone. */
  value?: string | null
  /** Override the resolved label. */
  label?: string
  /** Override the resolved tone. */
  tone?: StatusTone
  class?: HTMLAttributes['class']
}>()

const resolved = computed(() => resolveStatus(props.value))
const tone = computed<StatusTone>(() => props.tone ?? resolved.value.tone)
const label = computed(() => props.label ?? resolved.value.label)

// Tones render on the unmodified original Badge via semantic token classes —
// success/warning/info do NOT rely on Badge variant customization (FE-1 rule).
const toneClass: Record<StatusTone, string> = {
  success: 'border-success/30 bg-success/10 text-success',
  warning: 'border-warning/30 bg-warning/10 text-warning',
  danger: 'border-destructive/30 bg-destructive/10 text-destructive',
  info: 'border-brand/30 bg-brand/10 text-brand',
  neutral: 'border-border bg-muted text-muted-foreground',
}
</script>

<template>
  <Badge
    variant="outline"
    :aria-label="`状态：${label}`"
    :class="cn('max-w-40 truncate rounded-sm', toneClass[tone], props.class)"
  >
    {{ label }}
  </Badge>
</template>
