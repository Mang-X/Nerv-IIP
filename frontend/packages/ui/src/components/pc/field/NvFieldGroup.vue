<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { cn } from '../../../lib/utils'

const props = defineProps<{
  class?: HTMLAttributes['class']
}>()
</script>

<template>
  <div
    data-slot="nv-field-group"
    :class="
      cn(
        'gap-5 data-[slot=checkbox-group]:gap-3 *:data-[slot=nv-field-group]:gap-4 group/field-group @container/nv-field-group flex w-full flex-col',
        props.class,
      )
    "
  >
    <slot />
  </div>
</template>
