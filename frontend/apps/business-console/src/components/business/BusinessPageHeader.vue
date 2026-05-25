<script setup lang="ts">
import { Badge } from '@nerv-iip/ui'

withDefaults(
  defineProps<{
    domain: string
    title: string
    summary: string
    badge?: string
  }>(),
  {
    badge: 'MVP',
  },
)
</script>

<template>
  <div class="flex flex-col gap-3 border-b pb-4 sm:flex-row sm:items-end sm:justify-between">
    <div class="min-w-0">
      <p class="text-xs font-bold uppercase text-primary">{{ domain }}</p>
      <h1 class="mt-1 text-xl font-semibold text-foreground">{{ title }}</h1>
      <p class="mt-1 max-w-3xl text-sm text-muted-foreground">{{ summary }}</p>
    </div>
    <div class="flex shrink-0 items-center gap-2">
      <Badge variant="secondary">{{ badge }}</Badge>
      <slot name="actions" />
    </div>
  </div>
</template>
