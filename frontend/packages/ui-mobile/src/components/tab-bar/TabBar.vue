<script setup lang="ts">
import type { Component, HTMLAttributes } from 'vue'
import { cn } from '../../lib/utils'

/**
 * Mobile TabBar — bottom tab navigation (tdesign-mobile style). Icon + label per
 * tab, brand active state, ≥48px touch targets, safe-area aware via parent.
 */
export interface TabItem {
  value: string
  label: string
  icon?: Component
}

const props = defineProps<{
  modelValue: string
  items: TabItem[]
  class?: HTMLAttributes['class']
}>()
const emit = defineEmits<{ 'update:modelValue': [value: string] }>()
</script>

<template>
  <nav data-slot="tab-bar" :class="cn('flex items-stretch', props.class)">
    <button
      v-for="item in items"
      :key="item.value"
      type="button"
      :aria-current="modelValue === item.value ? 'page' : undefined"
      class="ds-tabbar-btn min-h-touch flex flex-1 flex-col items-center justify-center gap-0.5 py-1.5 text-xs"
      :class="modelValue === item.value ? 'text-brand' : 'text-muted-foreground'"
      @click="emit('update:modelValue', item.value)"
    >
      <!-- Background tap highlight — shows the touch range, no size/layout shift. -->
      <span class="ds-tabbar-hit" aria-hidden="true" />
      <component :is="item.icon" v-if="item.icon" class="relative size-6" aria-hidden="true" />
      <span class="relative">{{ item.label }}</span>
    </button>
  </nav>
</template>

<style scoped>
.ds-tabbar-btn {
  position: relative;
  outline: none;
  -webkit-tap-highlight-color: transparent;
  touch-action: manipulation;
  transition: color 0.15s var(--ease-out-quart, ease-out);
}
.ds-tabbar-hit {
  position: absolute;
  inset: 0.25rem 0.5rem;
  border-radius: 10px;
  background-color: var(--muted);
  opacity: 0;
  transition: opacity 0.2s var(--ease-out-quart, ease-out);
}
.ds-tabbar-btn:active .ds-tabbar-hit {
  opacity: 1;
  transition-duration: 0s;
}
@media (prefers-reduced-motion: reduce) {
  .ds-tabbar-hit {
    transition: none;
  }
}
</style>
