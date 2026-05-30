<script setup lang="ts">
import { computed } from 'vue'

const props = withDefaults(
  defineProps<{
    action?: string
    description: string
    title: string
    tone?: 'neutral' | 'attention'
  }>(),
  {
    action: '',
    tone: 'neutral',
  },
)

const markerClass = computed(() =>
  props.tone === 'attention' ? 'bg-warning/80' : 'bg-primary/80',
)
</script>

<template>
  <div class="grid min-h-44 place-items-center rounded-md border border-dashed bg-muted/20 px-4 py-10 text-center">
    <div class="grid max-w-xl gap-2">
      <div class="mx-auto h-1.5 w-10 rounded-sm" :class="markerClass" aria-hidden="true" />
      <p class="break-words text-sm font-medium text-foreground">{{ title }}</p>
      <p class="text-sm leading-6 text-muted-foreground">{{ description }}</p>
      <p v-if="action" class="text-sm font-medium text-primary">{{ action }}</p>
      <div v-if="$slots.action" class="mt-2 flex flex-wrap justify-center gap-2">
        <slot name="action" />
      </div>
    </div>
  </div>
</template>
