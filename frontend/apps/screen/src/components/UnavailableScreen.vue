<script setup lang="ts">
import InspectionDetail from './InspectionDetail.vue'
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
        <HallMetric detail metric="ncr" label="NCR 未处置 / 处置中（单）" :session="session" />
        <InspectionDetail :session="session" />
        <p>每种状态最多展示 5 条。批次合格率与缺陷分布未接入</p>
      </template>
      <h2 v-else>当前范围暂无指标来源</h2>
      <RouterLink v-if="screen.key !== 'quality'" to="/andon">进入真实安灯队列</RouterLink>
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
