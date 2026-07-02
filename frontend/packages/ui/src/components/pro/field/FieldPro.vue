<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import type { FieldProVariants } from '.'
import { cn } from '../../../lib/utils'
import { fieldProVariants } from '.'

const props = withDefaults(
  defineProps<{
    class?: HTMLAttributes['class']
    orientation?: FieldProVariants['orientation']
  }>(),
  {
    orientation: 'vertical',
  },
)
</script>

<template>
  <div
    role="group"
    data-slot="field-pro"
    :data-orientation="orientation"
    :class="cn(fieldProVariants({ orientation }), props.class)"
  >
    <slot />
  </div>
</template>
