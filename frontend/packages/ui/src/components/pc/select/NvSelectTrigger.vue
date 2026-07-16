<script setup lang="ts">
import type { SelectTriggerProps } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import { reactiveOmit } from '@vueuse/core'
import { ChevronDownIcon } from '@lucide/vue'
import { SelectIcon, SelectTrigger, useForwardProps } from 'reka-ui'
import { cn } from '../../../lib/utils'

const props = defineProps<
  SelectTriggerProps & { invalid?: boolean; class?: HTMLAttributes['class'] }
>()
const delegated = reactiveOmit(props, 'class', 'invalid')
const forwarded = useForwardProps(delegated)
</script>

<template>
  <SelectTrigger
    data-slot="nv-select-trigger"
    :data-invalid="invalid || undefined"
    v-bind="forwarded"
    :class="
      cn(
        'nv-strigger flex h-9 w-full items-center justify-between gap-2 rounded-md border border-input bg-card px-3 text-sm outline-none select-none data-placeholder:text-muted-foreground disabled:cursor-not-allowed disabled:opacity-50 dark:bg-input/30 [&>span]:line-clamp-1 [&_svg]:pointer-events-none [&_svg]:size-4 [&_svg]:shrink-0',
        props.class,
      )
    "
  >
    <slot />
    <SelectIcon as-child>
      <ChevronDownIcon class="nv-strigger-chevron text-muted-foreground" aria-hidden="true" />
    </SelectIcon>
  </SelectTrigger>
</template>

<style scoped>
@layer nv-components {
  .nv-strigger {
    transition:
      border-color 0.15s var(--nv-ease-out-quart, ease-out),
      box-shadow 0.15s var(--nv-ease-out-quart, ease-out);
  }
  .nv-strigger:hover {
    border-color: color-mix(in oklch, var(--foreground) 18%, transparent);
  }
  .nv-strigger:focus-visible,
  .nv-strigger[data-state='open'] {
    border-color: var(--nv-brand);
    box-shadow:
      0 0 0 3px color-mix(in oklch, var(--nv-brand) 22%, transparent),
      0 1px 2px 0 color-mix(in oklch, black 6%, transparent);
  }
  .nv-strigger[data-invalid] {
    border-color: var(--destructive);
  }
  .nv-strigger-chevron {
    transition: transform 0.2s var(--nv-ease-out-quart, ease-out);
  }
  .nv-strigger[data-state='open'] .nv-strigger-chevron {
    transform: rotate(180deg);
  }
  @media (prefers-reduced-motion: reduce) {
    .nv-strigger-chevron {
      transition: none;
    }
  }
}
</style>
