<script setup lang="ts">
import type { SelectItemProps } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import { reactiveOmit } from '@vueuse/core'
import { CheckIcon } from '@lucide/vue'
import { SelectItem, SelectItemIndicator, SelectItemText, useForwardProps } from 'reka-ui'
import { cn } from '../../../lib/utils'

const props = defineProps<SelectItemProps & { class?: HTMLAttributes['class'] }>()
const delegated = reactiveOmit(props, 'class')
const forwarded = useForwardProps(delegated)
</script>

<template>
  <SelectItem
    data-slot="nv-select-item"
    v-bind="forwarded"
    :class="
      cn(
        'relative flex h-8 w-full cursor-pointer items-center gap-2 rounded-md pr-8 pl-2.5 text-sm outline-hidden transition-colors select-none data-highlighted:bg-accent data-highlighted:text-accent-foreground data-[state=checked]:font-medium data-[state=checked]:text-brand data-disabled:pointer-events-none data-disabled:opacity-50 [&_svg]:pointer-events-none [&_svg]:shrink-0',
        props.class,
      )
    "
  >
    <SelectItemText>
      <slot />
    </SelectItemText>
    <span class="absolute right-2 flex size-4 items-center justify-center">
      <SelectItemIndicator>
        <CheckIcon class="size-4 text-brand" />
      </SelectItemIndicator>
    </span>
  </SelectItem>
</template>
