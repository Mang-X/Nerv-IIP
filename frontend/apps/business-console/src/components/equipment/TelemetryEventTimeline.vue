<script setup lang="ts">
import type { BusinessConsoleTelemetryHistoryItem } from '@nerv-iip/api-client'
import { projectTelemetryHistory } from '@/pages/equipment/telemetry/telemetryHistoryPresentation'
import { NvTimeline } from '@nerv-iip/ui'
import { computed } from 'vue'

const props = defineProps<{
  items: BusinessConsoleTelemetryHistoryItem[]
}>()

const timelineItems = computed(() => projectTelemetryHistory(props.items).timelineItems)
</script>

<template>
  <section
    class="grid gap-3 rounded-lg border bg-card p-4"
    aria-labelledby="telemetry-events-title"
  >
    <div>
      <h2 id="telemetry-events-title" class="text-base font-semibold text-foreground">
        事件上下文
      </h2>
      <p class="mt-1 text-sm text-muted-foreground">
        同一时间窗口内的报警与状态记录；类型文字与节点形态共同区分事件。
      </p>
    </div>
    <NvTimeline v-if="timelineItems.length" :items="timelineItems" />
    <p v-else class="rounded-lg border border-dashed p-6 text-center text-sm text-muted-foreground">
      当前窗口内没有状态或报警事件。
    </p>
  </section>
</template>
