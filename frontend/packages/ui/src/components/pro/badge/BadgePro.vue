<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import type { VariantProps } from 'class-variance-authority'
import { cva } from 'class-variance-authority'
import { Primitive } from 'reka-ui'
import { cn } from '../../../lib/utils'

/**
 * Pro — badge with restrained tonal fills (soft tint + matching border) plus a
 * solid primary. Heights/radius tuned to sit beside ButtonPro. Never edits原版.
 */
const badgeProVariants = cva(
  'inline-flex h-[22px] w-fit shrink-0 items-center justify-center gap-1 rounded-full border px-2 text-xs font-medium whitespace-nowrap transition-colors [&_svg]:pointer-events-none [&_svg:not([class*=size-])]:size-3',
  {
    variants: {
      variant: {
        neutral: 'border-border bg-muted text-muted-foreground',
        solid: 'border-transparent bg-primary text-primary-foreground',
        brand: 'border-brand/25 bg-brand/10 text-brand-strong',
        success: 'border-success/25 bg-success/10 text-success-strong',
        warning: 'border-warning/25 bg-warning/10 text-warning-strong',
        danger: 'border-destructive/25 bg-destructive/10 text-destructive-strong',
      },
    },
    defaultVariants: { variant: 'neutral' },
  },
)

type BadgeProVariants = VariantProps<typeof badgeProVariants>

const props = defineProps<{
  variant?: BadgeProVariants['variant']
  class?: HTMLAttributes['class']
}>()
</script>

<template>
  <Primitive data-slot="badge-pro" :class="cn(badgeProVariants({ variant }), props.class)">
    <slot />
  </Primitive>
</template>
