<script setup lang="ts">
import {
  resolveScheduleStatus,
  scheduleInvalidationHint,
} from '@/composables/scheduling/useScheduleInvalidation'
import { NvDataTable, NvStatusBadge } from '@nerv-iip/ui'
import { operationTaskLabel } from '@/utils/mesTaskIdentity'
import type { ShallowUnwrapRef } from 'vue'
import type { useWorkOrderDetailPage } from '../useWorkOrderDetailPage'

type View = ShallowUnwrapRef<ReturnType<typeof useWorkOrderDetailPage>>

defineProps<{
  detailError: View['detailError']
  detailErrorMessage: View['detailErrorMessage']
  detailPending: View['detailPending']
  formatDateTime: View['formatDateTime']
  operationTasks: View['operationTasks']
  operationTasksEmptyMessage: View['operationTasksEmptyMessage']
  refreshDetail: View['refreshDetail']
  route: View['route']
  statusLabel: View['statusLabel']
  taskColumns: View['taskColumns']
}>()
</script>

<template>
  <div class="grid min-w-0 gap-2 [&>*]:min-w-0">
    <span class="text-sm font-semibold text-foreground">工序任务</span>
    <NvDataTable
      :columns="taskColumns"
      :rows="operationTasks"
      row-key="operationTaskId"
      :loading="detailPending"
      :error="detailError"
      :error-message="detailErrorMessage"
      :empty-message="operationTasksEmptyMessage"
      :searchable="false"
      :column-settings="false"
      @retry="refreshDetail"
    >
      <template #cell-operationTaskId="{ row }">
        <span
          class="flex flex-col items-start gap-1"
          :data-andon-source="
            Boolean(row.operationTaskId && route.query.operationTaskId === row.operationTaskId)
          "
        >
          <span class="break-all whitespace-normal" :title="row.operationTaskId">{{
            operationTaskLabel(row)
          }}</span>
          <NvStatusBadge
            v-if="row.operationTaskId && route.query.operationTaskId === row.operationTaskId"
            label="来源工序"
            tone="info"
          />
        </span>
      </template>
      <template #cell-status="{ row }">
        <NvStatusBadge :value="row.status" :label="statusLabel(row.status)" />
      </template>
      <template #cell-scheduleStatus="{ row }">
        <span
          v-if="resolveScheduleStatus(row).key === 'invalidated'"
          class="inline-flex"
          :title="scheduleInvalidationHint(row.scheduleInvalidationReasonCode)"
        >
          <NvStatusBadge label="已失效" tone="warning" />
        </span>
        <NvStatusBadge
          v-else
          :label="resolveScheduleStatus(row).label"
          :tone="resolveScheduleStatus(row).tone"
        />
      </template>
      <template #cell-operationSequence="{ row }"
        ><span class="tabular-nums">{{ row.operationSequence ?? 0 }}</span></template
      >
      <template #cell-startedAtUtc="{ row }">{{
        formatDateTime(row.startedAtUtc ?? row.plannedStartUtc)
      }}</template>
    </NvDataTable>
  </div>
</template>
