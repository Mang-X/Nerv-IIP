<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { cn } from '../../../lib/utils'
import { Label } from '../../ui/label'

const props = defineProps<{
  class?: HTMLAttributes['class']
}>()
</script>

<template>
  <Label
    data-slot="nv-field-label"
    :class="
      cn(
        'has-data-checked:bg-primary/5 has-data-checked:border-primary/30 dark:has-data-checked:border-primary/20 dark:has-data-checked:bg-primary/10 gap-2 group-data-[disabled=true]/field:opacity-50 has-[>[data-slot=nv-field]]:rounded-lg has-[>[data-slot=nv-field]]:border *:data-[slot=nv-field]:p-2.5 group/field-label peer/field-label flex w-fit leading-snug',
        'has-[>[data-slot=nv-field]]:w-full has-[>[data-slot=nv-field]]:flex-col',
        props.class,
      )
    "
  >
    <slot />
  </Label>
</template>
