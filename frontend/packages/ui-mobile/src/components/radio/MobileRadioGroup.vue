<script setup lang="ts">
import type { RadioGroupRootEmits, RadioGroupRootProps } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import { reactiveOmit } from '@vueuse/core'
import { RadioGroupRoot, useForwardPropsEmits } from 'reka-ui'
import { cn } from '../../lib/utils'

const props = defineProps<RadioGroupRootProps & { class?: HTMLAttributes['class'] }>()
const emits = defineEmits<RadioGroupRootEmits>()
const forwarded = useForwardPropsEmits(reactiveOmit(props, 'class'), emits)
</script>

<template>
  <RadioGroupRoot
    data-slot="mobile-radio-group"
    v-bind="forwarded"
    :class="cn('overflow-hidden rounded-xl border border-border bg-card', props.class)"
  >
    <slot />
  </RadioGroupRoot>
</template>
