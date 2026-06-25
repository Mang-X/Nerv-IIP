<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { cn } from '../../../lib/utils'

const props = defineProps<{
  class?: HTMLAttributes['class']
}>()
</script>

<template>
  <div
    data-slot="alert-dialog-pro-header"
    :class="cn('grid grid-rows-[auto_1fr] place-items-center gap-1.5 text-center has-data-[slot=alert-dialog-pro-media]:grid-rows-[auto_auto_1fr] has-data-[slot=alert-dialog-pro-media]:gap-x-4 sm:place-items-start sm:text-left sm:has-data-[slot=alert-dialog-pro-media]:grid-rows-[auto_1fr]', props.class)"
  >
    <slot />
  </div>
</template>
