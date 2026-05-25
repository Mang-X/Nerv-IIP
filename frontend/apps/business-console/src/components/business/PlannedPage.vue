<script setup lang="ts">
import { Badge } from '@nerv-iip/ui'

defineProps<{
  domain: string
  routeName: string
  summary: string
}>()
</script>

<template>
  <section class="grid gap-3">
    <div class="flex flex-wrap items-end justify-between gap-3">
      <div>
        <p class="text-xs font-bold uppercase text-primary">{{ domain }}</p>
        <h1 class="text-xl font-semibold text-foreground">{{ routeName }}</h1>
        <p class="mt-1 max-w-3xl text-sm text-muted-foreground">{{ summary }}</p>
      </div>
      <Badge variant="secondary">MVP route</Badge>
    </div>

    <div class="rounded-lg border bg-background p-4 text-sm text-muted-foreground">
      This route is wired into the authenticated Business Console shell. Data tables and forms land
      here in the next vertical-slice tasks.
    </div>
  </section>
</template>
