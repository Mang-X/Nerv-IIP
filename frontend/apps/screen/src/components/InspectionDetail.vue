<script setup lang="ts">
import ScopePicker from './ScopePicker.vue'
import ReadError from './ReadError.vue'
import { useScopedReading } from '@/composables/useScopedReading'
import { inspectionOptions } from '@/data/real/workScope'
import { fetchHallMetric } from '@/data/fetchers/launcher'
import type { ScreenSession } from '@/data/session'
import { formatScreenFreshness } from '@/data/freshness'
const props = defineProps<{ session: ScreenSession }>()
const { options, selected, select, data, error, loading, lastUpdated, refresh } = useScopedReading(
  () => inspectionOptions(props.session),
  (scope) => fetchHallMetric('inspection', props.session, true, scope),
  15000,
)
</script>
<template>
  <section>
    <ScopePicker label="检验范围" :options="options" :selected="selected" @select="select" />
    <p>检验待认领 / 进行中（项）</p>
    <template v-if="typeof data === 'object'">
      <strong>{{ data.value }}</strong>
      <p v-for="line in data.exceptions.slice(1)" :key="line">{{ line }}</p>
      <p v-if="data.value === '0 / 0'">当前范围暂无检验任务</p>
    </template>
    <ReadError
      v-if="error"
      :error="error"
      :loading="loading"
      :last-updated="lastUpdated"
      @retry="refresh"
    />
    <small v-else>{{
      lastUpdated ? formatScreenFreshness(false, lastUpdated).text : '读取中'
    }}</small>
  </section>
</template>
