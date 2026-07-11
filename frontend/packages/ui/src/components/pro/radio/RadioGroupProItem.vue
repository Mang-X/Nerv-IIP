<script setup lang="ts">
import type { RadioGroupItemProps } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import { reactiveOmit } from '@vueuse/core'
import { RadioGroupIndicator, RadioGroupItem, useForwardProps } from 'reka-ui'
import { useId } from 'vue'
import { cn } from '../../../lib/utils'

/**
 * Pro — radio item with a brand dot indicator. Pairs a reka RadioGroupItem
 * with an associated label slot.
 */
const props = defineProps<RadioGroupItemProps & { class?: HTMLAttributes['class'] }>()
const forwarded = useForwardProps(reactiveOmit(props, 'class', 'id'))
const generatedId = useId()
const id = props.id ?? generatedId
</script>

<template>
  <div class="flex items-center gap-2.5">
    <RadioGroupItem
      :id="id"
      data-slot="radio-group-pro-item"
      v-bind="forwarded"
      :class="
        cn(
          'ds-radio flex size-[18px] shrink-0 items-center justify-center rounded-full border border-input bg-card outline-none focus-visible:ring-[3px] focus-visible:ring-brand/30 focus-visible:border-brand data-[state=checked]:border-brand disabled:cursor-not-allowed disabled:opacity-50 dark:bg-input/30',
          props.class,
        )
      "
    >
      <RadioGroupIndicator class="ds-radio-ind flex items-center justify-center">
        <span class="size-2 rounded-full bg-brand" />
      </RadioGroupIndicator>
    </RadioGroupItem>
    <label :for="id" class="cursor-pointer text-sm select-none"><slot /></label>
  </div>
</template>

<style scoped>
@layer nv-components {
  /* This transition covers border/ring/press-scale — do NOT add the Tailwind
     `transition-colors` utility to the root: as a utility it outranks this layered
     rule and resets `transition-property` to colors only, dropping `transform` so
     the press-scale below fires with no easing (instant, feels like no motion). */
  .ds-radio {
    transition:
      border-color 0.15s var(--nv-ease-out-quart, ease-out),
      box-shadow 0.15s var(--nv-ease-out-quart, ease-out),
      transform 0.18s var(--nv-ease-out-quart, ease-out);
  }
  /* Press: the control depresses while held, then decelerates back — unified with
   Switch/Checkbox. No bounce (per our motion philosophy). */
  .ds-radio:active:not([data-disabled]) {
    transform: scale(0.9);
  }
  .ds-radio-ind {
    animation: ds-radio-in 0.16s var(--nv-ease-out-quart, ease-out);
  }
  @keyframes ds-radio-in {
    from {
      transform: scale(0);
    }
    to {
      transform: scale(1);
    }
  }
  @media (prefers-reduced-motion: reduce) {
    .ds-radio-ind {
      animation: none;
    }
  }
}
</style>
