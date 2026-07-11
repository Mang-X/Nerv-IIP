<script setup lang="ts">
import type { RadioGroupItemProps } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import { reactiveOmit } from '@vueuse/core'
import { Check } from 'lucide-vue-next'
import { RadioGroupIndicator, RadioGroupItem, useForwardProps } from 'reka-ui'
import { cn } from '../../lib/utils'

/**
 * Mobile radio item — full-width tappable list row (iOS settings style): label
 * left, brand checkmark on the right when selected. Inset hairline separator.
 */
const props = defineProps<RadioGroupItemProps & { class?: HTMLAttributes['class'] }>()
const forwarded = useForwardProps(reactiveOmit(props, 'class'))
</script>

<template>
  <RadioGroupItem
    data-slot="mobile-radio-item"
    v-bind="forwarded"
    :class="
      cn(
        'ds-mradio relative flex min-h-touch w-full items-center justify-between gap-3 px-4 text-left text-[15px] outline-none select-none active:bg-accent data-[state=checked]:font-medium data-[disabled]:pointer-events-none data-[disabled]:opacity-40',
        props.class,
      )
    "
  >
    <span><slot /></span>
    <RadioGroupIndicator>
      <Check class="ds-mradio-tick size-5 text-brand" aria-hidden="true" />
    </RadioGroupIndicator>
  </RadioGroupItem>
</template>

<style scoped>
@layer nv-components {
  .ds-mradio-tick {
    animation: ds-mradio-pop 0.2s var(--nv-ease-out-quart);
  }
  @keyframes ds-mradio-pop {
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
    .ds-mradio-tick {
      animation: none;
    }
  }
  .ds-mradio::after {
    content: '';
    position: absolute;
    right: 0;
    bottom: 0;
    left: 0;
    height: 1px;
    background: var(--border);
    pointer-events: none;
  }
  .ds-mradio:last-child::after {
    display: none;
  }
}
</style>
