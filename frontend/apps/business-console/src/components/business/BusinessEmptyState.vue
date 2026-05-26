<script setup lang="ts">
defineProps<{
  action?: string
  description: string
  title: string
}>()
</script>

<template>
  <div class="grid gap-2 py-8 text-center">
    <p class="text-sm font-medium text-foreground">{{ title }}</p>
    <p class="mx-auto max-w-md text-sm text-muted-foreground">{{ description }}</p>
    <p v-if="action" class="text-sm text-primary">{{ action }}</p>
  </div>
</template>
