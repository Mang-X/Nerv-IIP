<script setup lang="ts">
import type { TooltipContentEmits, TooltipContentProps } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import { reactiveOmit } from '@vueuse/core'
import { TooltipArrow, TooltipContent, TooltipPortal, useForwardPropsEmits } from 'reka-ui'
import { cn } from '../../../lib/utils'

/**
 * Pro — tooltip content: high-contrast inverted surface, tight padding, arrow,
 * exponential fade/zoom in. Rebuilt on reka primitives; never edits原版.
 */
const props = withDefaults(
  defineProps<TooltipContentProps & { class?: HTMLAttributes['class'] }>(),
  { sideOffset: 6 },
)
const emits = defineEmits<TooltipContentEmits>()
const forwarded = useForwardPropsEmits(reactiveOmit(props, 'class'), emits)
</script>

<template>
  <TooltipPortal>
    <TooltipContent
      data-slot="tooltip-pro-content"
      v-bind="forwarded"
      :class="
        cn(
          'ds-tip data-[state=delayed-open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=delayed-open]:fade-in-0 data-[state=delayed-open]:zoom-in-95 data-[state=closed]:zoom-out-95 z-50 w-fit rounded-md bg-foreground px-2.5 py-1.5 text-xs font-medium text-background shadow-lg duration-150',
          props.class,
        )
      "
    >
      <slot />
      <TooltipArrow class="fill-foreground/90" :width="11" :height="5" />
    </TooltipContent>
  </TooltipPortal>
</template>

<style scoped>
@layer nv-components {
  /* Subtle glass: a faint top highlight + inner sheen over the translucent fill.
   Kept understated — depth, not spectacle. */
  .ds-tip {
    box-shadow:
      0 8px 28px -10px color-mix(in oklch, black 45%, transparent),
      inset 0 1px 0 0 color-mix(in oklch, white 14%, transparent);
  }
}
</style>
