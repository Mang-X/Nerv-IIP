<script setup lang="ts">
import DefaultLayout from '@/layouts/DefaultLayout.vue'

definePage({
  meta: {
    requiresAuth: true,
  },
})
</script>

<template>
  <DefaultLayout>
    <section class="max-w-xl">
      <h1 class="mb-2 text-2xl font-semibold">页面不存在</h1>
      <p class="text-sm text-muted-foreground">请返回实例控制台继续操作。</p>
    </section>
  </DefaultLayout>
</template>
