<script setup lang="ts">
import ScreenLayout from '@/layouts/ScreenLayout.vue'
</script>

<template>
  <ScreenLayout title="设备监控大屏" screen="设备 · 建设中">
    <div class="ph">
      <p class="ph__t">设备监控大屏</p>
      <p class="ph__s">建设中（M1 Phase 2）</p>
    </div>
  </ScreenLayout>
</template>

<style scoped>
.ph {
  height: 100%;
  display: flex;
  flex-direction: column;
  gap: 10px;
  align-items: center;
  justify-content: center;
  color: var(--sb-muted);
}
.ph__t {
  font-size: 28px;
  color: var(--sb-text-2);
}
.ph__s {
  color: var(--sb-faint);
}
</style>
