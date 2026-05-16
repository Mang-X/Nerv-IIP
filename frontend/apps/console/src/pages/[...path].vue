<script setup lang="ts">
import DefaultLayout from '@/layouts/DefaultLayout.vue'
</script>

<template>
  <DefaultLayout>
    <section class="not-found">
      <h1 class="not-found-title">页面不存在</h1>
      <p class="not-found-copy">请返回实例控制台继续操作。</p>
    </section>
  </DefaultLayout>
</template>

<style scoped>
.not-found {
  max-width: 40rem;
}

.not-found-title {
  margin: 0 0 0.5rem;
  font-size: 1.5rem;
}

.not-found-copy {
  margin: 0;
  color: var(--color-text-muted);
}
</style>
