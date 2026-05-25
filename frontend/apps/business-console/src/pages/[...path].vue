<script setup lang="ts">
import BusinessLayout from '@/layouts/BusinessLayout.vue'

definePage({
  meta: {
    requiresAuth: true,
    title: 'Business route not found',
  },
})
</script>

<template>
  <BusinessLayout>
    <section class="rounded-lg border bg-background p-4">
      <h1 class="text-lg font-semibold text-foreground">Business route not found</h1>
      <p class="mt-1 text-sm text-muted-foreground">
        The requested business console route is not part of the current MVP shell.
      </p>
    </section>
  </BusinessLayout>
</template>
