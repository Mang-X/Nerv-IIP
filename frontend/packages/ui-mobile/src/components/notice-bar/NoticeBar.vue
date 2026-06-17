<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed } from 'vue'
import { CircleAlertIcon, InfoIcon, TriangleAlertIcon } from 'lucide-vue-next'
import { cn } from '../../lib/utils'

/**
 * Mobile NoticeBar — a single-line notice strip (tdesign-mobile style) with a
 * leading icon and tone. Text truncates; pair with a tap handler for detail.
 */
type Tone = 'info' | 'warning' | 'danger'

const props = withDefaults(
  defineProps<{
    tone?: Tone
    class?: HTMLAttributes['class']
  }>(),
  { tone: 'info' },
)

const icon = computed(
  () => ({ info: InfoIcon, warning: TriangleAlertIcon, danger: CircleAlertIcon })[props.tone],
)
const toneClass = computed(
  () =>
    ({
      info: 'bg-brand/10 text-brand-strong',
      warning: 'bg-warning/12 text-warning-strong',
      danger: 'bg-destructive/10 text-destructive-strong',
    })[props.tone],
)
</script>

<template>
  <div
    data-slot="notice-bar"
    :class="cn('flex items-center gap-2 px-4 py-2.5 text-sm', toneClass, props.class)"
  >
    <component :is="icon" class="size-4 shrink-0" aria-hidden="true" />
    <span class="truncate"><slot /></span>
  </div>
</template>
