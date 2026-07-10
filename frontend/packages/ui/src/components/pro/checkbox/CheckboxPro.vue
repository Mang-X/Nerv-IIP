<script setup lang="ts">
import type { CheckboxRootEmits, CheckboxRootProps } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import { reactiveOmit } from '@vueuse/core'
import { CheckIcon } from 'lucide-vue-next'
import { CheckboxIndicator, CheckboxRoot, useForwardPropsEmits } from 'reka-ui'
import { cn } from '../../../lib/utils'

/**
 * Pro — checkbox with a brand-filled checked state and a spring-free check
 * reveal. Rebuilt on reka primitives; never edits原版 Checkbox.
 */
const props = defineProps<CheckboxRootProps & { class?: HTMLAttributes['class'] }>()
const emits = defineEmits<CheckboxRootEmits>()
const forwarded = useForwardPropsEmits(reactiveOmit(props, 'class'), emits)
</script>

<template>
  <CheckboxRoot
    data-slot="checkbox-pro"
    v-bind="forwarded"
    :class="
      cn(
        'ds-check peer relative flex size-[18px] shrink-0 items-center justify-center rounded-[5px] border border-input bg-card outline-none transition-colors focus-visible:ring-[3px] focus-visible:ring-brand/30 focus-visible:border-brand data-[state=checked]:border-brand data-[state=checked]:bg-brand data-[state=checked]:text-brand-foreground disabled:cursor-not-allowed disabled:opacity-50 dark:bg-input/30',
        props.class,
      )
    "
  >
    <CheckboxIndicator class="ds-check-ind flex items-center justify-center text-current">
      <CheckIcon class="size-3.5" stroke-width="3" />
    </CheckboxIndicator>
  </CheckboxRoot>
</template>

<style scoped>
@layer nv-components {
  .ds-check {
    transition:
      background-color 0.15s var(--nv-ease-out-quart, ease-out),
      border-color 0.15s var(--nv-ease-out-quart, ease-out),
      box-shadow 0.15s var(--nv-ease-out-quart, ease-out),
      transform 0.18s var(--nv-ease-out-quart, ease-out);
  }
  /* Press: the box depresses while held, then decelerates back — unified with
   Switch/Radio. No bounce (per our motion philosophy). */
  .ds-check:active:not([data-disabled]) {
    transform: scale(0.88);
  }
  .ds-check-ind {
    animation: ds-check-in 0.18s var(--nv-ease-out-quart, ease-out);
  }
  @keyframes ds-check-in {
    from {
      opacity: 0;
      transform: scale(0.6);
    }
    to {
      opacity: 1;
      transform: scale(1);
    }
  }
  @media (prefers-reduced-motion: reduce) {
    .ds-check-ind {
      animation: none;
    }
  }
}
</style>
