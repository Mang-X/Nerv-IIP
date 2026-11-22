<script setup lang="ts">
import { equipmentStatusTone } from '@/composables/equipment/useBusinessEquipment'
import { NvMetricCard, NvSectionCards } from '@nerv-iip/ui'
import { RadioIcon } from '@lucide/vue'
import { formatDateTime } from '@/utils/format'
import type { ShallowUnwrapRef } from 'vue'
import type { useDeviceDetailPage } from '../useDeviceDetailPage'

type View = ShallowUnwrapRef<ReturnType<typeof useDeviceDetailPage>>

defineProps<{
  activeAlarms: View['activeAlarms']
  blockCount: View['blockCount']
  currentState: View['currentState']
  sourceText: View['sourceText']
  sourceTone: View['sourceTone']
  stateText: View['stateText']
}>()
</script>

<template>
  <NvSectionCards :columns="4">
    <NvMetricCard
      variant="alert"
      label="当前状态"
      :value="stateText"
      :tone="equipmentStatusTone(currentState?.currentState) === 'danger' ? 'danger' : 'neutral'"
      :status="
        equipmentStatusTone(currentState?.currentState) === 'danger'
          ? { label: '需关注', tone: 'danger' }
          : undefined
      "
      :foot-start="`状态时间 ${formatDateTime(currentState?.stateOccurredAtUtc)}`"
    />
    <NvMetricCard
      variant="icon"
      label="数据状态"
      :value="sourceText"
      :tone="sourceTone"
      :icon="RadioIcon"
    />
    <NvMetricCard
      variant="alert"
      label="未解除报警"
      :value="activeAlarms.length"
      :tone="activeAlarms.length > 0 ? 'danger' : 'neutral'"
      :status="activeAlarms.length > 0 ? { label: '需处理', tone: 'danger' } : undefined"
    />
    <NvMetricCard
      variant="alert"
      label="占用时段"
      :value="blockCount"
      :tone="blockCount > 0 ? 'warning' : 'neutral'"
      :status="blockCount > 0 ? { label: '影响排程', tone: 'warning' } : undefined"
    />
  </NvSectionCards>
</template>
