<script setup lang="ts">
import { useNow } from '@vueuse/core'
import { computed, watch } from 'vue'
import { useScreenData } from '@nerv-iip/ui'
import { fetchHallMetric, HallMetricUnavailable, type HallMetric } from '@/data/fetchers/launcher'
import type { ScreenSession } from '@/data/session'
import { formatScreenFreshness } from '@/data/freshness'

const props = defineProps<{ metric: HallMetric; label: string; session: ScreenSession }>()
const { data, error, loading, lastUpdated, refresh } = useScreenData(
  () => fetchHallMetric(props.metric, props.session),
  { intervalMs: 15000 },
)
const unavailable = computed(() =>
  error.value instanceof HallMetricUnavailable ? error.value.message : undefined,
)
watch(
  unavailable,
  (value) => {
    if (value) {
      data.value = undefined
      lastUpdated.value = undefined
    }
  },
  { flush: 'sync' },
)
const now = useNow({ interval: 1000 })
const status = computed(() => {
  if (unavailable.value) return unavailable.value
  if (error.value && data.value === undefined) return '读取失败'
  if (!lastUpdated.value) return '读取中'
  return formatScreenFreshness(
    Boolean(error.value) || now.value.getTime() - lastUpdated.value > 45000,
    lastUpdated.value,
  ).text
})
</script>

<template>
  <div class="metric">
    <span>{{ label }}</span>
    <strong>{{ unavailable || data === undefined ? '—' : data }}</strong>
    <small role="status"
      >{{ status }} <button v-if="error" :disabled="loading" @click="refresh">重试</button></small
    >
  </div>
</template>

<style scoped>
.metric {
  display: grid;
  gap: 6px;
  color: var(--nv-scr-muted);
}
strong {
  font-size: 30px;
  color: var(--nv-scr-text);
}
button {
  cursor: pointer;
  color: var(--nv-scr-cyan);
}
</style>
