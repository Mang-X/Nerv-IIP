<script setup lang="ts">
import { useScreenClock } from '@/composables/useScreenClock'
import { computed, watch } from 'vue'
import { useScreenData } from '@nerv-iip/ui'
import { fetchHallMetric, HallMetricUnavailable, type HallMetric } from '@/data/fetchers/launcher'
import type { ScreenSession } from '@/data/session'
import { formatScreenFreshness } from '@/data/freshness'

const props = defineProps<{
  metric: HallMetric
  label: string
  session: ScreenSession
  detail?: boolean
}>()
const { data, error, loading, lastUpdated, refresh, stop, start } = useScreenData(
  () => fetchHallMetric(props.metric, props.session, props.detail),
  { intervalMs: 15000 },
)
const unavailable = computed(() =>
  error.value instanceof HallMetricUnavailable ? error.value.message : undefined,
)
watch(
  error,
  (failure) => {
    if (failure instanceof HallMetricUnavailable) {
      stop()
      data.value = undefined
      lastUpdated.value = undefined
    } else if (!failure) start()
  },
  { flush: 'sync' },
)
const now = useScreenClock()
const value = computed(() => (typeof data.value === 'object' ? data.value.value : data.value))
const exceptions = computed(() => (typeof data.value === 'object' ? data.value.exceptions : []))
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
    <strong>{{ unavailable || value === undefined ? '—' : value }}</strong>
    <small v-for="line in exceptions" :key="line">{{ line }}</small>
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
