<script setup lang="ts">
import type { PrimitiveProps } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import type { VariantProps } from 'class-variance-authority'
import { cva } from 'class-variance-authority'
import { Primitive } from 'reka-ui'
import { cn } from '../../lib/utils'
import Loader from '../pro/loader/Loader.vue'

/**
 * Touch — large, touch-optimized action button for tablet station boards and
 * workshop kiosks. Big tap targets (≥56px), action-semantic variants, strong
 * press feedback. Reduces operation paths: one obvious tap per intent.
 */
const touchButtonVariants = cva(
  'ds-tbtn relative inline-flex shrink-0 select-none items-center justify-center gap-2.5 rounded-xl text-base font-semibold whitespace-nowrap outline-none transition-[background,box-shadow,transform] duration-150 focus-visible:ring-4 focus-visible:ring-ring/40 disabled:pointer-events-none disabled:opacity-50 aria-busy:pointer-events-none [&_svg:not([class*=size-])]:size-5 [&_svg]:shrink-0',
  {
    variants: {
      variant: {
        brand: 'ds-tbtn-solid bg-brand text-brand-foreground hover:brightness-105',
        default: 'ds-tbtn-solid bg-primary text-primary-foreground hover:brightness-110',
        success: 'ds-tbtn-solid bg-success text-success-foreground hover:brightness-105',
        warning: 'ds-tbtn-solid bg-warning text-warning-foreground hover:brightness-105',
        destructive: 'ds-tbtn-solid bg-destructive text-white hover:brightness-105',
        outline:
          'border-2 border-border bg-card text-foreground hover:bg-muted hover:border-foreground/20',
        ghost: 'text-foreground hover:bg-muted',
      },
      size: {
        md: 'h-11 px-4 text-[15px]',
        lg: 'h-14 px-5 text-base',
        xl: 'h-[72px] px-6 text-lg',
      },
      block: { true: 'w-full', false: '' },
    },
    defaultVariants: { variant: 'default', size: 'lg', block: false },
  },
)

type TouchButtonVariants = VariantProps<typeof touchButtonVariants>

const props = withDefaults(
  defineProps<
    PrimitiveProps & {
      variant?: TouchButtonVariants['variant']
      size?: TouchButtonVariants['size']
      block?: boolean
      loading?: boolean
      class?: HTMLAttributes['class']
    }
  >(),
  { as: 'button', block: false },
)
</script>

<template>
  <Primitive
    data-slot="touch-button"
    :as="as"
    :as-child="asChild"
    :aria-busy="loading || undefined"
    :class="cn(touchButtonVariants({ variant, size, block }), props.class)"
  >
    <Loader v-if="loading" variant="ring" size="default" class="text-current" />
    <slot v-else name="leading" />
    <slot />
  </Primitive>
</template>

<style scoped>
.ds-tbtn {
  -webkit-tap-highlight-color: transparent;
  touch-action: manipulation;
  will-change: transform;
}
.ds-tbtn:active:not(:disabled) {
  transform: scale(0.97);
}
.ds-tbtn-solid {
  box-shadow:
    inset 0 1px 0 0 color-mix(in oklch, white 16%, transparent),
    0 1px 2px 0 color-mix(in oklch, black 22%, transparent);
}
@media (prefers-reduced-motion: reduce) {
  .ds-tbtn:active:not(:disabled) {
    transform: none;
  }
}
</style>
