<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import type { ChartConfig } from '.'
import { cn } from '../../../lib/utils'
defineProps<{ id?: HTMLAttributes['id'], class?: HTMLAttributes['class'], config: ChartConfig }>()
</script>
<template>
  <div data-slot="chart" :data-chart="id" :class="cn('cn-chart flex aspect-video h-full w-full flex-col justify-center text-xs', $props.class)">
    <slot />
  </div>
</template>
