<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { cn } from '../../../lib/utils'

const props = defineProps<{ class?: HTMLAttributes['class'] }>()
</script>

<template>
  <div
    data-slot="sheet-footer"
    :class="cn('gap-2 p-4 mt-auto flex flex-col sm:flex-row sm:justify-end', props.class)"
  >
    <slot />
  </div>
</template>
