<script setup lang="ts">
defineProps<{
  label: string
  value: string | number
  detail?: string
}>()
</script>

<template>
  <div class="rounded-lg border bg-background px-4 py-3">
    <p class="text-xs font-medium uppercase text-muted-foreground">{{ label }}</p>
    <p class="mt-1 text-2xl font-semibold tabular-nums text-foreground">{{ value }}</p>
    <p v-if="detail" class="mt-1 text-xs text-muted-foreground">{{ detail }}</p>
  </div>
</template>
