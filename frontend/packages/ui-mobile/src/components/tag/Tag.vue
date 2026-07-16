<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { X } from '@lucide/vue'
import { cn } from '../../lib/utils'

/**
 * Mobile Tag — a small labeled chip (status / category), distinct from Badge
 * 角标 which pins a count to a corner. Soft tinted background with matching
 * strong text; optional closable affordance.
 */
type Variant = 'default' | 'brand' | 'success' | 'warning' | 'danger'
type Size = 'sm' | 'md'

withDefaults(
  defineProps<{
    variant?: Variant
    size?: Size
    closable?: boolean
    class?: HTMLAttributes['class']
  }>(),
  { variant: 'default', size: 'md', closable: false },
)
const emit = defineEmits<{ close: [] }>()

const variantClass: Record<Variant, string> = {
  default: 'bg-muted text-muted-foreground',
  brand: 'bg-brand/12 text-brand-strong',
  success: 'bg-success/15 text-success-strong',
  warning: 'bg-warning/18 text-warning-strong',
  danger: 'bg-destructive/12 text-destructive-strong',
}
const sizeClass: Record<Size, string> = {
  sm: 'h-5 gap-0.5 px-1.5 text-[11px]',
  md: 'h-6 gap-1 px-2 text-xs',
}
</script>

<template>
  <span
    data-slot="tag"
    :class="
      cn(
        'inline-flex items-center rounded-md font-medium whitespace-nowrap [&_svg]:shrink-0',
        variantClass[variant],
        sizeClass[size],
        $props.class,
      )
    "
  >
    <slot />
    <button
      v-if="closable"
      type="button"
      class="nv-m-tag-close -mr-0.5 inline-flex items-center justify-center rounded-full outline-none"
      aria-label="移除标签"
      @click.stop="emit('close')"
    >
      <X :class="size === 'sm' ? 'size-3' : 'size-3.5'" aria-hidden="true" />
    </button>
  </span>
</template>

<style scoped>
@layer nv-components {
  .nv-m-tag-close {
    -webkit-tap-highlight-color: transparent;
    opacity: 0.7;
    transition: opacity 0.15s var(--nv-ease-out-quart, ease-out);
  }
  .nv-m-tag-close:hover,
  .nv-m-tag-close:active {
    opacity: 1;
  }
  @media (prefers-reduced-motion: reduce) {
    .nv-m-tag-close {
      transition: none;
    }
  }
}
</style>
