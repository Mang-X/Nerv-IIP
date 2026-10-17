<script setup lang="ts">
import { computed } from 'vue'
import type { BusinessConsoleSchedulingResourceLoad } from '@nerv-iip/api-client'
import { formatDateTime } from '@/utils/format'

const props = defineProps<{ loads: BusinessConsoleSchedulingResourceLoad[] }>()
const resources = computed(() =>
  Object.entries(Object.groupBy(props.loads, (load) => load.resourceId ?? '未命名资源')).map(
    ([resourceId, loads]) => ({
      resourceId,
      active: loads?.filter((load) => load.assignedMinutes !== 0) ?? [],
      idle: loads?.filter((load) => load.assignedMinutes === 0) ?? [],
    }),
  ),
)
function loadText(load: BusinessConsoleSchedulingResourceLoad) {
  const utilization =
    load.utilization === undefined ? '—' : `${Math.round(load.utilization * 100)}%`
  return `已排 ${load.assignedMinutes ?? '—'} / 可用 ${load.availableMinutes ?? '—'} 分钟 · 利用率 ${utilization}`
}
</script>

<template>
  <section class="grid gap-3" aria-label="资源负荷">
    <header>
      <h3 class="text-sm font-semibold">资源负荷</h3>
      <p class="text-sm text-muted-foreground">
        按资源查看各时间窗口的已排 / 可用分钟；零负荷仅表示该窗口未安排工序。
      </p>
    </header>
    <p v-if="!loads.length" class="text-sm text-muted-foreground">暂无资源负荷。</p>
    <article
      v-for="resource in resources"
      :key="resource.resourceId"
      class="rounded-md border bg-background p-3 text-sm"
    >
      <h4 class="font-medium break-all">{{ resource.resourceId }}</h4>
      <div v-for="(load, index) in resource.active" :key="index" class="mt-2 border-t pt-2">
        <p class="tabular-nums">
          {{ formatDateTime(load.windowStartUtc) }} 至
          {{ formatDateTime(load.windowEndUtc) }}（本地时间）
        </p>
        <p class="text-muted-foreground">{{ loadText(load) }}</p>
      </div>
      <details v-if="resource.idle.length" class="mt-2">
        <summary class="cursor-pointer text-muted-foreground">
          {{ resource.idle.length }} 个零负荷窗口（展开查看）
        </summary>
        <div v-for="(load, index) in resource.idle" :key="index" class="mt-2 border-t pt-2">
          <p class="tabular-nums">
            {{ formatDateTime(load.windowStartUtc) }} 至
            {{ formatDateTime(load.windowEndUtc) }}（本地时间）
          </p>
          <p class="text-muted-foreground">{{ loadText(load) }}</p>
        </div>
      </details>
    </article>
  </section>
</template>
