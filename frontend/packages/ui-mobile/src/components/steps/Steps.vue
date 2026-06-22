<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { Check } from 'lucide-vue-next'
import { cn } from '../../lib/utils'

/**
 * Mobile Steps — horizontal process indicator (Vant / tdesign-mobile style).
 * Connector renders as a track + a brand fill that animates as progress
 * advances; nodes are opaque so the line never bleeds through the circle.
 */
export interface StepItem {
  label: string
  note?: string
}

withDefaults(
  defineProps<{
    steps: StepItem[]
    current?: number
    class?: HTMLAttributes['class']
  }>(),
  { current: 0 },
)
</script>

<template>
  <ol data-slot="steps" :class="cn('flex items-start', $props.class)">
    <li
      v-for="(step, i) in steps"
      :key="i"
      class="relative flex flex-1 flex-col items-center text-center"
    >
      <!-- connector: a border track with an animated brand fill -->
      <span
        v-if="i > 0"
        class="absolute top-3.5 right-1/2 left-[-50%] h-0.5 -translate-y-1/2 overflow-hidden rounded-full bg-border"
        aria-hidden="true"
      >
        <span
          class="ds-step-fill block h-full rounded-full bg-brand"
          :style="{ width: i <= current ? '100%' : '0%' }"
        />
      </span>
      <!-- node (opaque, sits above the connector) -->
      <span
        :class="
          cn(
            'ds-step-node relative z-10 grid size-7 place-items-center rounded-full border-2 text-xs font-medium tabular-nums',
            i < current && 'border-brand bg-brand text-brand-foreground',
            i === current && 'border-brand bg-card text-brand ring-4 ring-brand/12',
            i > current && 'border-border bg-card text-muted-foreground',
          )
        "
      >
        <Transition name="ds-step-check" mode="out-in">
          <Check v-if="i < current" key="done" class="size-4" aria-hidden="true" />
          <span v-else :key="`n${i}`">{{ i + 1 }}</span>
        </Transition>
      </span>
      <span
        :class="
          cn(
            'mt-2 px-1 text-xs transition-colors',
            i === current ? 'font-medium text-foreground' : 'text-muted-foreground',
          )
        "
      >
        {{ step.label }}
      </span>
      <span v-if="step.note" class="px-1 text-[11px] text-brand">{{ step.note }}</span>
    </li>
  </ol>
</template>

<style scoped>
.ds-step-fill {
  transition: width 0.45s var(--ease-out-expo, cubic-bezier(0.16, 1, 0.3, 1));
}
.ds-step-node {
  transition:
    background-color 0.3s var(--ease-out-quart, ease-out),
    border-color 0.3s var(--ease-out-quart, ease-out),
    box-shadow 0.3s var(--ease-out-quart, ease-out);
}
.ds-step-check-enter-active,
.ds-step-check-leave-active {
  transition:
    opacity 0.18s ease,
    transform 0.18s var(--ease-out-back, cubic-bezier(0.34, 1.4, 0.64, 1));
}
.ds-step-check-enter-from,
.ds-step-check-leave-to {
  opacity: 0;
  transform: scale(0.5);
}
@media (prefers-reduced-motion: reduce) {
  .ds-step-fill,
  .ds-step-node,
  .ds-step-check-enter-active,
  .ds-step-check-leave-active {
    transition: none;
  }
}
</style>
