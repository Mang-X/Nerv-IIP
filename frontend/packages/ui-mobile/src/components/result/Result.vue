<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed } from 'vue'
import { CircleCheck, CircleX } from 'lucide-vue-next'
import { cn } from '../../lib/utils'

const props = defineProps<{
  status: 'success' | 'error'
  title: string
  description?: string
  class?: HTMLAttributes['class']
}>()
const tone = computed(() => (props.status === 'success' ? 'text-success' : 'text-destructive'))
</script>

<template>
  <div
    data-result
    :data-status="status"
    :class="
      cn('flex flex-col items-center justify-center gap-4 px-6 py-10 text-center', $props.class)
    "
  >
    <component
      :is="status === 'success' ? CircleCheck : CircleX"
      :class="cn('ds-result-icon size-16', tone)"
      aria-hidden="true"
    />
    <div class="space-y-1">
      <h2 class="text-xl font-semibold text-foreground">{{ title }}</h2>
      <p v-if="description" class="text-sm text-muted-foreground">{{ description }}</p>
    </div>
    <div v-if="$slots.actions" class="w-full max-w-xs space-y-2 pt-2">
      <slot name="actions" />
    </div>
  </div>
</template>

<style scoped>
@layer nv-components {
  .ds-result-icon {
    animation: ds-result-pop 0.42s var(--nv-ease-out-quart) both;
  }
  @keyframes ds-result-pop {
    from {
      opacity: 0;
      transform: scale(0.4);
    }
    to {
      opacity: 1;
      transform: scale(1);
    }
  }
  @media (prefers-reduced-motion: reduce) {
    .ds-result-icon {
      animation: none;
    }
  }
}
</style>
