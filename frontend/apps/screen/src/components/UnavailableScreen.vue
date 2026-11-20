<script setup lang="ts">
import ScreenLayout from '@/layouts/ScreenLayout.vue'
import HallMetric from '@/components/launcher/HallMetric.vue'
import type { ScreenDef } from '@/data/screens'
import { useBackLink } from '@/composables/useBackLink'
import { useRealAuthStore } from '@/stores/realAuth'
import { computed } from 'vue'

defineProps<{ screen: ScreenDef }>()
const back = useBackLink(() => ({ to: '/', label: '返回大屏门厅' }))
const auth = useRealAuthStore()
const session = computed(() => ({
  organizationId: auth.principal?.organizationId ?? '',
  environmentId: auth.principal?.environmentId ?? '',
}))
</script>

<template>
  <ScreenLayout :title="screen.title" :line="session.organizationId" screen="">
    <section class="missing">
      <template v-if="screen.key === 'quality'">
        <HallMetric metric="ncr" label="待处置 NCR（单）" :session="session" />
        <HallMetric metric="inspection" label="待检任务（项）" :session="session" />
        <p>批次合格率与缺陷分布暂无来源</p>
      </template>
      <h2 v-else>当前范围暂无指标来源</h2>
      <RouterLink v-if="screen.key === 'line'" to="/andon">进入真实安灯队列</RouterLink>
      <RouterLink v-if="screen.key === 'factory'" to="/workshop">车间总览</RouterLink>
      <RouterLink v-if="screen.key === 'workshop'" to="/line">产线监控</RouterLink>
      <RouterLink :to="back.to">‹ {{ back.label }}</RouterLink>
    </section>
  </ScreenLayout>
</template>

<style scoped>
.missing {
  display: grid;
  align-content: center;
  justify-items: center;
  height: 100%;
  gap: 20px;
  color: var(--nv-scr-text);
}
.missing a {
  color: var(--nv-scr-cyan);
}
</style>
