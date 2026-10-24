<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { cn } from '../../../lib/utils'
import { inject } from 'vue'
import { dialogScrollableKey } from './layout'

const props = defineProps<{ class?: HTMLAttributes['class'] }>()
const scrollable = inject(dialogScrollableKey, undefined)
</script>

<template>
  <div
    data-slot="nv-dialog-header"
    :class="
      cn('flex shrink-0 flex-col gap-1.5 pr-6', scrollable && 'px-6 pt-6 pb-4 pr-12', props.class)
    "
  >
    <slot />
  </div>
</template>
