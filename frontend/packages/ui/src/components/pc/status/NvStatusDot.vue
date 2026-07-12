<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed } from 'vue'
import { cn } from '../../../lib/utils'
import type { StatusTone } from '../../blocks/status-badge/statusMap'

/**
 * Pro — a status dot with an optional live pulse. The pulse ring is purely
 * decorative feedback for "live/active" states and stops under reduced-motion.
 */
const props = withDefaults(
  defineProps<{
    tone?: StatusTone
    pulse?: boolean
    size?: 'sm' | 'default'
    class?: HTMLAttributes['class']
  }>(),
  { tone: 'neutral', pulse: false, size: 'default' },
)

const toneVar = computed(
  () =>
    ({
      success: 'var(--nv-success)',
      warning: 'var(--nv-warning)',
      danger: 'var(--destructive)',
      info: 'var(--nv-brand)',
      neutral: 'var(--muted-foreground)',
    })[props.tone],
)
</script>

<template>
  <span
    :class="
      cn('nv-dot relative inline-flex shrink-0', size === 'sm' ? 'size-1.5' : 'size-2', props.class)
    "
    :style="{ '--dot': toneVar }"
  >
    <span v-if="pulse" class="nv-dot-ping absolute inset-0 rounded-full" aria-hidden="true" />
    <span class="relative size-full rounded-full" :style="{ background: 'var(--dot)' }" />
  </span>
</template>

<style scoped>
@layer nv-components {
  .nv-dot-ping {
    background: var(--dot);
    animation: nv-ping 1.6s var(--nv-ease-out-expo) infinite;
  }
  @keyframes nv-ping {
    0% {
      transform: scale(1);
      opacity: 0.55;
    }
    70%,
    100% {
      transform: scale(2.4);
      opacity: 0;
    }
  }
  @media (prefers-reduced-motion: reduce) {
    .nv-dot-ping {
      animation: none;
      opacity: 0;
    }
  }
}
</style>
