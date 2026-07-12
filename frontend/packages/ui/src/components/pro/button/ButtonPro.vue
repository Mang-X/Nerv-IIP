<script setup lang="ts">
import type { PrimitiveProps } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import type { VariantProps } from 'class-variance-authority'
import { cva } from 'class-variance-authority'
import { Primitive } from 'reka-ui'
import { computed } from 'vue'
import { cn } from '../../../lib/utils'
import Loader from '../loader/Loader.vue'

/**
 * Pro — copy-rebuilt button (does NOT touch原版 Button). Restrained, premium:
 * layered surface with a hairline top highlight on solid fills, calibrated
 * hover/active feedback, a first-class dynamic-brand CTA, and built-in loading.
 */
const proButtonVariants = cva(
  'ds-btn group/btn relative inline-flex shrink-0 select-none items-center justify-center gap-1.5 whitespace-nowrap rounded-md text-sm font-medium outline-none transition-[background,box-shadow,transform,color] duration-150 focus-visible:ring-[3px] focus-visible:ring-ring/45 disabled:pointer-events-none disabled:opacity-50 aria-busy:pointer-events-none [&_svg]:pointer-events-none [&_svg:not([class*=size-])]:size-4 [&_svg]:shrink-0',
  {
    variants: {
      variant: {
        default: 'ds-btn-solid bg-primary text-primary-foreground hover:bg-primary/90',
        brand: 'ds-btn-solid bg-brand text-brand-foreground hover:brightness-110',
        destructive:
          'ds-btn-solid bg-destructive text-white hover:brightness-110 focus-visible:ring-destructive/35',
        outline:
          'border border-border bg-card text-foreground shadow-xs hover:bg-muted hover:border-foreground/15 dark:bg-input/30 dark:hover:bg-input/50',
        secondary: 'bg-secondary text-secondary-foreground hover:bg-secondary/70',
        ghost: 'text-foreground hover:bg-muted',
        link: 'text-brand underline-offset-4 hover:underline',
      },
      size: {
        sm: 'h-8 gap-1 rounded-[7px] px-3 text-[0.8rem] [&_svg:not([class*=size-])]:size-3.5',
        default: 'h-9 px-4',
        lg: 'h-10 px-5 text-[0.9rem]',
        icon: 'size-9',
        'icon-sm': 'size-8 rounded-[7px]',
      },
    },
    defaultVariants: { variant: 'default', size: 'default' },
  },
)

type ProButtonVariants = VariantProps<typeof proButtonVariants>

const props = withDefaults(
  defineProps<
    PrimitiveProps & {
      variant?: ProButtonVariants['variant']
      size?: ProButtonVariants['size']
      loading?: boolean
      class?: HTMLAttributes['class']
    }
  >(),
  { as: 'button' },
)

const isSolid = computed(() => {
  const v = props.variant ?? 'default'
  return v === 'default' || v === 'brand' || v === 'destructive'
})
</script>

<template>
  <Primitive
    data-slot="button-pro"
    :as="as"
    :as-child="asChild"
    :aria-busy="loading || undefined"
    :class="cn(proButtonVariants({ variant, size }), props.class)"
  >
    <Loader v-if="loading" variant="ring" size="sm" class="text-current" />
    <slot v-else name="leading" />
    <slot />
    <slot v-if="!loading" name="trailing" />
    <span v-if="isSolid" class="ds-btn-sheen" aria-hidden="true" />
  </Primitive>
</template>

<style scoped>
@layer nv-components {
  .ds-btn {
    /* press feedback: a crisp 1px nudge, no layout shift */
    will-change: transform;
  }
  .ds-btn:active:not(:disabled) {
    transform: translateY(0.5px) scale(0.992);
  }

  /* Solid fills get a hairline top highlight + a defined contact shadow.
   Not the banned "1px border + soft 16px+ shadow" combo — no border, shadow ≤8px. */
  .ds-btn-solid {
    box-shadow:
      inset 0 1px 0 0 color-mix(in oklch, white 16%, transparent),
      0 1px 2px 0 color-mix(in oklch, black 28%, transparent);
  }
  .ds-btn-solid:hover:not(:disabled) {
    box-shadow:
      inset 0 1px 0 0 color-mix(in oklch, white 22%, transparent),
      0 2px 6px -1px color-mix(in oklch, black 32%, transparent);
  }

  /* A faint sheen that wipes across solid fills on hover. */
  .ds-btn-sheen {
    position: absolute;
    inset: 0;
    border-radius: inherit;
    overflow: hidden;
    pointer-events: none;
    opacity: 0;
    background: linear-gradient(
      100deg,
      transparent 30%,
      color-mix(in oklch, white 18%, transparent) 50%,
      transparent 70%
    );
    background-size: 220% 100%;
    background-position: 120% 0;
    transition:
      opacity 0.2s ease,
      background-position 0.6s var(--nv-ease-out-expo, ease-out);
  }
  .ds-btn-solid:hover .ds-btn-sheen {
    opacity: 1;
    background-position: -40% 0;
  }

  @media (prefers-reduced-motion: reduce) {
    .ds-btn:active:not(:disabled) {
      transform: none;
    }
    .ds-btn-sheen {
      display: none;
    }
  }
}
</style>
