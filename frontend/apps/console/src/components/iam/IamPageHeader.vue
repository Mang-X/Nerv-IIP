<script setup lang="ts">
defineProps<{
  title: string
  description: string
}>()
</script>

<template>
  <header class="flex flex-col gap-1">
    <h1 class="text-2xl font-semibold tracking-normal text-foreground">
      {{ title }}
    </h1>
    <p class="max-w-3xl text-sm leading-6 text-muted-foreground">
      {{ description }}
    </p>
  </header>
</template>
