<script setup lang="ts">
import { RouterLink } from 'vue-router'
import AndonScreen from '@/components/andon/AndonScreen.vue'
import ScreenLayout from '@/layouts/ScreenLayout.vue'
import { useBackLink } from '@/composables/useBackLink'
const back = useBackLink(() => ({ to: '/', label: '返回大屏门厅' }))
</script>

<template>
  <ScreenLayout title="Nerv-IIP 安灯呼叫看板" line="真实作业范围" screen="安灯 · 只读">
    <div class="andon-page">
      <AndonScreen />
      <RouterLink :to="back.to" class="andon-back">‹ {{ back.label }}</RouterLink>
    </div>
  </ScreenLayout>
</template>

<style scoped>
@layer app {
  .andon-page {
    height: 100%;
    display: flex;
    flex-direction: column;
    gap: 12px;
  }
  .andon-page > :first-child {
    flex: 1;
    min-height: 0;
  }
  .andon-back {
    color: var(--nv-scr-cyan);
    font-size: 18px;
    text-decoration: none;
  }
}
</style>
