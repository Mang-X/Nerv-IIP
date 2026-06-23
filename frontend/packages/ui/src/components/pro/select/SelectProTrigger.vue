<script setup lang="ts">
import type { SelectTriggerProps } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import { reactiveOmit } from '@vueuse/core'
import { ChevronDownIcon } from 'lucide-vue-next'
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
    data-slot="select-pro-trigger"
    :data-invalid="invalid || undefined"
    v-bind="forwarded"
    :class="
      cn(
        'ds-strigger flex h-9 w-full items-center justify-between gap-2 rounded-md border border-input bg-card px-3 text-sm outline-none select-none data-placeholder:text-muted-foreground disabled:cursor-not-allowed disabled:opacity-50 dark:bg-input/30 [&>span]:line-clamp-1 [&_svg]:pointer-events-none [&_svg]:size-4 [&_svg]:shrink-0',
        props.class,
      )
    "
  >
    <slot />
    <SelectIcon as-child>
      <ChevronDownIcon class="ds-strigger-chevron text-muted-foreground" aria-hidden="true" />
    </SelectIcon>
  </SelectTrigger>
</template>

<style scoped>
.ds-strigger {
  transition:
    border-color 0.15s var(--ease-out-quart, ease-out),
    box-shadow 0.15s var(--ease-out-quart, ease-out);
}
.ds-strigger:hover {
  border-color: color-mix(in oklch, var(--foreground) 18%, transparent);
}
.ds-strigger:focus-visible,
.ds-strigger[data-state='open'] {
  border-color: var(--brand);
  box-shadow:
    0 0 0 3px color-mix(in oklch, var(--brand) 22%, transparent),
    0 1px 2px 0 color-mix(in oklch, black 6%, transparent);
}
.ds-strigger[data-invalid] {
  border-color: var(--destructive);
}
.ds-strigger-chevron {
  transition: transform 0.2s var(--ease-out-quart, ease-out);
}
.ds-strigger[data-state='open'] .ds-strigger-chevron {
  transform: rotate(180deg);
}
@media (prefers-reduced-motion: reduce) {
  .ds-strigger-chevron {
    transition: none;
  }
}
</style>
