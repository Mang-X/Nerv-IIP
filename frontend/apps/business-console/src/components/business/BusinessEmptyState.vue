<script setup lang="ts">
defineProps<{
  action?: string
  description: string
  title: string
}>()
</script>

<template>
  <div class="grid gap-2 py-10 text-center">
    <p class="text-sm font-medium text-foreground">{{ title }}</p>
    <p class="mx-auto max-w-lg text-sm leading-6 text-muted-foreground">{{ description }}</p>
    <p v-if="action" class="text-sm font-medium text-primary">{{ action }}</p>
  </div>
</template>
