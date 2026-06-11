<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { ChevronRight } from 'lucide-vue-next'
import { cn } from '../../lib/utils'

withDefaults(defineProps<{ title: string; subtitle?: string; interactive?: boolean; class?: HTMLAttributes['class'] }>(), {
  interactive: true,
})
const emit = defineEmits<{ select: [] }>()
</script>

<template>
  <div
    data-row
    :role="interactive ? 'button' : undefined"
    :tabindex="interactive ? 0 : undefined"
    :class="cn(
      'min-h-row flex w-full items-center gap-3 border-b border-border bg-card px-4 py-3 text-left',
      interactive && 'active:bg-accent',
      $props.class,
    )"
    @click="interactive && emit('select')"
    @keydown.enter="interactive && emit('select')"
  >
    <div class="min-w-0 flex-1">
      <div class="truncate text-base font-medium text-foreground">{{ title }}</div>
      <div v-if="subtitle" class="truncate text-sm text-muted-foreground">{{ subtitle }}</div>
      <slot name="meta" />
    </div>
    <slot name="trailing" />
    <ChevronRight v-if="interactive" data-chevron class="size-5 shrink-0 text-muted-foreground" aria-hidden="true" />
  </div>
</template>
