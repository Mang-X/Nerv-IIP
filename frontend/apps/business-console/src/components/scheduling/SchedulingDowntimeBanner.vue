<script setup lang="ts">
import type { SchedulingDowntimeImpactResponse } from '@nerv-iip/api-client'
import type { ScheduleModel } from '@nerv-iip/scheduling'
import { NvButton } from '@nerv-iip/ui'
import { computed } from 'vue'
import { downtimePresentation } from '@/composables/schedulingDowntime'
import { formatDateTime } from '@/utils/format'
const props = defineProps<{
  impact?: SchedulingDowntimeImpactResponse
  model?: ScheduleModel
  now: Date
  blockedReason?: string
  pending?: boolean
  hasCandidates?: boolean
}>()
const emit = defineEmits<{ preview: [] }>()
const items = computed(() =>
  (props.impact?.items ?? []).map((item) => {
    const fact = item.fact!
    const resource = props.model?.resources.find((r) => r.id === fact.deviceAssetId)
    return {
      item,
      fact,
      display: downtimePresentation(item, props.now),
      device: resource?.text || fact.deviceAssetId || fact.workCenterId,
    }
  }),
)
</script>
<template>
  <section
    v-if="items.length"
    class="grid gap-3 rounded-lg border border-destructive/40 bg-destructive/5 p-4"
    aria-label="设备停机影响"
    data-testid="scheduling-downtime"
  >
    <div class="flex flex-wrap items-center justify-between gap-3">
      <h2 class="font-semibold text-destructive">设备停机影响</h2>
      <NvButton
        variant="outline"
        :disabled="Boolean(blockedReason) || pending"
        :title="blockedReason"
        @click="emit('preview')"
      >
        {{ pending ? '正在核实候选…' : hasCandidates ? '重预览并比较候选' : '生成并比较重排候选' }}
      </NvButton>
    </div>
    <p
      v-for="{ item, fact, display, device } in items"
      :key="`${fact.source}:${fact.sourceReferenceId}`"
      class="text-sm"
      role="status"
    >
      <strong>{{ device }}</strong> · 停机 {{ display.duration }} · {{ display.status }}
      <span v-if="fact.expectedRestoreAtUtc && !fact.recoveredAtUtc"
        >（ETR {{ formatDateTime(fact.expectedRestoreAtUtc) }}）</span
      >
      <span v-if="fact.recoveredAtUtc">（实际恢复 {{ formatDateTime(fact.recoveredAtUtc) }}）</span>
      · {{ item.operationsWithAlternativesCount ?? 0 }} 道受影响工序有合格可用备选
    </p>
    <p class="text-xs text-muted-foreground">
      停机风险按已保存基线核对，工序保持原位置；选定候选后在草稿与修订对比中核对，再确认发布。
    </p>
    <p v-if="blockedReason" class="text-xs text-muted-foreground">{{ blockedReason }}</p>
  </section>
</template>
