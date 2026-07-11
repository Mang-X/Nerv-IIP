<script setup lang="ts">
import type { PrimitiveProps } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import { computed } from 'vue'
import { Primitive } from 'reka-ui'
import { cn } from '../../lib/utils'

/**
 * Mobile button — compact, native-feeling (iOS / tdesign-mobile). Medium weight,
 * restrained radius, dim-on-press feedback. NOT the oversized kiosk TouchButton.
 */
type Variant = 'primary' | 'default' | 'outline' | 'text' | 'danger'
type Size = 'sm' | 'md' | 'lg'

const props = withDefaults(
  defineProps<
    PrimitiveProps & {
      variant?: Variant
      size?: Size
      block?: boolean
      class?: HTMLAttributes['class']
    }
  >(),
  { as: 'button', variant: 'default', size: 'md', block: false },
)

const variantClass: Record<Variant, string> = {
  primary: 'bg-brand text-brand-foreground',
  default: 'bg-muted text-foreground',
  outline: 'border border-border bg-transparent text-foreground',
  text: 'bg-transparent px-1 text-brand',
  danger: 'bg-destructive/10 text-destructive',
}
const sizeClass: Record<Size, string> = {
  sm: 'h-8 px-3 text-[13px]',
  md: 'h-10 px-4 text-[15px]',
  lg: 'h-12 px-5 text-base',
}

const classes = computed(() =>
  cn(
    'ds-mbtn inline-flex shrink-0 select-none items-center justify-center gap-1.5 rounded-[10px] font-medium whitespace-nowrap outline-none transition-[opacity,background-color] disabled:pointer-events-none disabled:opacity-40 [&_svg:not([class*=size-])]:size-[1.05em] [&_svg]:shrink-0',
    variantClass[props.variant],
    sizeClass[props.size],
    props.block && 'w-full',
    props.class,
  ),
)
</script>

<template>
  <Primitive data-slot="mobile-button" :as="as" :as-child="asChild" :class="classes">
    <slot />
  </Primitive>
</template>

<style scoped>
@layer nv-components {
  .ds-mbtn {
    -webkit-tap-highlight-color: transparent;
    touch-action: manipulation;
  }
  .ds-mbtn:active:not(:disabled) {
    opacity: 0.6;
  }
}
</style>
