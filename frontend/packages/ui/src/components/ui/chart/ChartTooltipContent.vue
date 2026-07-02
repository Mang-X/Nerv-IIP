<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { cn } from '../../../lib/utils'
defineProps<{ class?: HTMLAttributes['class'] }>()
</script>
<template>
  <div :class="cn('border-border/50 bg-background grid min-w-32 items-start gap-1.5 rounded-lg border px-2.5 py-1.5 text-xs shadow-xl', $props.class)">
    <slot />
  </div>
</template>
