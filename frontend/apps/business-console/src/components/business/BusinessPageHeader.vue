<script setup lang="ts">
import { Badge } from '@nerv-iip/ui'

withDefaults(
  defineProps<{
    domain: string
    title: string
    summary: string
    badge?: string
    kicker?: string
  }>(),
  {
    badge: 'PC 工作台',
    kicker: '',
  },
)
</script>

<template>
  <div class="flex flex-col gap-4 border-b pb-5 sm:flex-row sm:items-end sm:justify-between">
    <div class="min-w-0">
      <div class="flex flex-wrap items-center gap-2">
        <p class="text-xs font-bold uppercase text-primary">{{ domain }}</p>
        <span v-if="kicker" class="rounded-sm bg-muted px-2 py-0.5 text-xs text-muted-foreground">
          {{ kicker }}
        </span>
      </div>
      <h1 class="mt-2 text-2xl font-semibold tracking-normal text-foreground">{{ title }}</h1>
      <p class="mt-2 max-w-3xl text-sm leading-6 text-muted-foreground">{{ summary }}</p>
    </div>
    <div class="flex shrink-0 items-center gap-2">
      <Badge variant="secondary">{{ badge }}</Badge>
      <slot name="actions" />
    </div>
  </div>
</template>
