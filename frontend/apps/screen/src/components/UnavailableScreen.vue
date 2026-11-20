<script setup lang="ts">
import ScreenLayout from '@/layouts/ScreenLayout.vue'
defineProps<{ title: string }>()
</script>

<template>
  <ScreenLayout :title="title" line="" screen="">
    <section class="missing">
      <h2>当前范围暂无指标来源</h2>
      <RouterLink to="/">‹ 返回大屏门厅</RouterLink>
    </section>
  </ScreenLayout>
</template>

<style scoped>
.missing { display: grid; align-content: center; justify-items: center; height: 100%; gap: 20px; color: var(--nv-scr-text); }
.missing a { color: var(--nv-scr-cyan); }
</style>
