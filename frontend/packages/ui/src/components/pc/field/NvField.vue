<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import type { NvFieldVariants } from '.'
import { cn } from '../../../lib/utils'
import { nvFieldVariants } from '.'

const props = withDefaults(
  defineProps<{
    class?: HTMLAttributes['class']
    orientation?: NvFieldVariants['orientation']
  }>(),
  {
    orientation: 'vertical',
  },
)
</script>

<template>
  <div
    role="group"
    data-slot="nv-field"
    :data-orientation="orientation"
    :class="cn(nvFieldVariants({ orientation }), props.class)"
  >
    <slot />
  </div>
</template>
