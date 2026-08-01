<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { cn } from '../../../lib/utils'

const props = defineProps<{
  class?: HTMLAttributes['class']
}>()
</script>

<template>
  <div
    data-slot="nv-alert-dialog-footer"
    :class="
      cn(
        'sticky bottom-0 z-10 -mx-6 -mb-6 flex flex-col-reverse gap-2 rounded-b-xl bg-card px-6 pt-3 pb-6 sm:flex-row sm:justify-end',
        props.class,
      )
    "
  >
    <slot />
  </div>
</template>
