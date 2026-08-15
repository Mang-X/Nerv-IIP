<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { cn } from '../../../lib/utils'

const props = defineProps<{ class?: HTMLAttributes['class'] }>()
</script>

<template>
  <!-- pr-12 clears the absolute close button (top-4 right-4, size-7) so the title
       and description never run under it — mirrors NvDialogHeader's pr reservation.
       sticky top-0 + bg-card:抽屉本体是滚动容器,标题不跟着内容滚走,长内容里也
       始终看得到「这是哪个抽屉」(#1418)。 -->
  <div
    data-slot="nv-sheet-header"
    :class="cn('sticky top-0 z-10 flex flex-col gap-0.5 bg-card p-4 pr-12', props.class)"
  >
    <slot />
  </div>
</template>
