<script setup lang="ts">
import type { ProgressRootProps } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import { reactiveOmit } from '@vueuse/core'
import { ProgressIndicator, ProgressRoot } from 'reka-ui'
import { computed } from 'vue'
import { cn } from '../../../lib/utils'

const props = withDefaults(
  defineProps<ProgressRootProps & { class?: HTMLAttributes['class'] }>(),
  {
    modelValue: 0,
  },
)

const delegatedProps = reactiveOmit(props, 'class')
const indicatorStyle = computed(() => ({
  transform: `translateX(-${100 - (props.modelValue ?? 0)}%)`,
}))
</script>

<template>
  <ProgressRoot
    data-slot="progress"
    v-bind="delegatedProps"
    :class="cn('bg-muted h-1 rounded-full relative flex w-full items-center overflow-x-hidden', props.class)"
  >
    <ProgressIndicator
      data-slot="progress-indicator"
      class="bg-primary size-full flex-1 transition-all"
      :style="indicatorStyle"
    />
  </ProgressRoot>
</template>
