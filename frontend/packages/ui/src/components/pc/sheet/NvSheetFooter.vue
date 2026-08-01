<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { cn } from '../../../lib/utils'

const props = defineProps<{ class?: HTMLAttributes['class'] }>()
</script>

<template>
  <!-- sticky bottom-0 + bg-card:抽屉本体自身 overflow-y-auto,原先 `mt-auto` 只在内容
       短于抽屉时把按钮推到底;内容一长按钮就跟着滚出视口够不着(#1418)。 -->
  <div
    data-slot="nv-sheet-footer"
    :class="cn('sticky bottom-0 z-10 mt-auto flex flex-col gap-2 bg-card p-4', props.class)"
  >
    <slot />
  </div>
</template>
