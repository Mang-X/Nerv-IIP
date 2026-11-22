<script setup lang="ts">
import {
  describeAvailabilityStatus,
  describeEquipmentReason,
  equipmentStatusTone,
} from '@/composables/equipment/useBusinessEquipment'
import EquipmentHealthCard from '@/components/equipment/EquipmentHealthCard.vue'
import { NvBadge, NvButton, NvDataTable } from '@nerv-iip/ui'
import { Settings2Icon } from '@lucide/vue'
import { RouterLink } from 'vue-router'
import { formatDateTime } from '@/utils/format'
import type { ShallowUnwrapRef } from 'vue'
import type { useDeviceDetailPage } from '../useDeviceDetailPage'

type View = ShallowUnwrapRef<ReturnType<typeof useDeviceDetailPage>>

defineProps<{
  activeAlarms: View['activeAlarms']
  availabilityWindows: View['availabilityWindows']
  badgeVariant: View['badgeVariant']
  columns: View['columns']
  currentState: View['currentState']
  deviceCode: View['deviceCode']
  deviceName: View['deviceName']
  devicePending: View['devicePending']
  health: View['health']
  healthErrorMessage: View['healthErrorMessage']
  healthPending: View['healthPending']
  severityLabel: View['severityLabel']
  severityVariant: View['severityVariant']
  sourceText: View['sourceText']
  sourceTone: View['sourceTone']
  stateText: View['stateText']
}>()
</script>

<template>
  <div class="grid gap-4 lg:grid-cols-[360px_minmax(0,1fr)]">
    <div class="grid gap-4">
      <div class="rounded-lg border bg-card p-4">
        <p class="text-xs font-bold uppercase text-primary">当前状态</p>
        <div class="mt-3 flex items-center justify-between gap-3">
          <div class="min-w-0">
            <p class="truncate text-lg font-semibold text-foreground">
              {{ deviceName || deviceCode || '—' }}
            </p>
            <p class="truncate text-xs text-muted-foreground">
              {{ deviceCode || '—' }}
            </p>
            <p class="mt-1 text-sm text-muted-foreground">
              状态时间 {{ formatDateTime(currentState?.stateOccurredAtUtc) }}
            </p>
          </div>
          <NvBadge
            class="rounded-sm"
            :variant="badgeVariant(equipmentStatusTone(currentState?.currentState))"
            >{{ stateText }}</NvBadge
          >
        </div>
        <div class="mt-3">
          <NvBadge class="rounded-sm" :variant="sourceTone">{{ sourceText }}</NvBadge>
        </div>
      </div>

      <EquipmentHealthCard :health="health" :pending="healthPending" :error="healthErrorMessage" />

      <div class="rounded-lg border bg-card">
        <div class="border-b px-4 py-3">
          <h2 class="text-sm font-semibold text-foreground">当前报警</h2>
        </div>
        <div class="grid gap-3 p-4">
          <div
            v-for="alarm in activeAlarms"
            :key="alarm.alarmEventId ?? alarm.alarmCode"
            class="grid gap-2 rounded-lg border p-3"
          >
            <div class="flex items-center justify-between gap-2">
              <p class="truncate text-sm font-semibold text-foreground">
                {{ alarm.alarmCode ?? '未设报警代码' }}
              </p>
              <NvBadge class="rounded-sm" :variant="severityVariant(alarm.severity)">{{
                severityLabel(alarm.severity)
              }}</NvBadge>
            </div>
            <p class="text-xs text-muted-foreground">{{ formatDateTime(alarm.raisedAtUtc) }}</p>
            <NvButton size="sm" type="button" variant="outline" class="justify-self-start" as-child>
              <RouterLink to="/equipment/telemetry/alarm-rules"
                ><Settings2Icon aria-hidden="true" />维护规则</RouterLink
              >
            </NvButton>
          </div>
          <div
            v-if="!activeAlarms.length"
            class="rounded-lg border border-dashed p-4 text-sm text-muted-foreground"
          >
            当前设备没有未解除报警。
          </div>
        </div>
      </div>
    </div>

    <div class="grid gap-2">
      <span class="text-sm font-semibold text-foreground">设备占用时段（排程与维修）</span>
      <NvDataTable
        :columns="columns"
        :rows="availabilityWindows"
        :row-key="(r) => `${r.deviceAssetId}-${r.reasonCode}-${r.startUtc}`"
        :loading="devicePending"
        :searchable="false"
        :column-settings="false"
        empty-message="当前设备没有排程或维修占用时段。"
      >
        <template #cell-availabilityStatus="{ row }">
          <NvBadge
            class="rounded-sm"
            :variant="describeAvailabilityStatus(row.availabilityStatus).tone"
            >{{ describeAvailabilityStatus(row.availabilityStatus).label }}</NvBadge
          >
        </template>
        <template #cell-reason="{ row }">
          <div class="grid gap-1">
            <span class="font-medium text-foreground">{{
              describeEquipmentReason(row.reasonCode ?? '').label
            }}</span>
            <span class="text-xs text-muted-foreground">{{
              describeEquipmentReason(row.reasonCode ?? '').nextStep
            }}</span>
          </div>
        </template>
        <template #cell-startUtc="{ row }">{{ formatDateTime(row.startUtc) }}</template>
        <template #cell-endUtc="{ row }">{{ formatDateTime(row.endUtc) }}</template>
      </NvDataTable>
    </div>
  </div>
</template>
