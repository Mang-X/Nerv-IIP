<script setup lang="ts">
import BusinessLayout from '@/layouts/BusinessLayout.vue'

definePage({
  meta: {
    requiresAuth: true,
    title: '业务路由不存在',
  },
})
</script>

<template>
  <BusinessLayout>
    <section class="rounded-lg border bg-background p-4">
      <h1 class="text-lg font-semibold text-foreground">业务路由不存在</h1>
      <p class="mt-1 text-sm text-muted-foreground">
        当前业务控制台 MVP 尚未包含该路由。
      </p>
    </section>
  </BusinessLayout>
</template>
