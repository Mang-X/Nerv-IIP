<script setup lang="ts">
import type {
  BusinessConsoleMesShiftHandoverOpenIssue,
  BusinessConsoleMesShiftHandoverUnfinishedWorkOrder,
  BusinessConsoleMesShiftHandoverWipItem,
} from '@nerv-iip/api-client'
import { NvDataTable, NvStatusBadge } from '@nerv-iip/ui'
import { labelFor, MES_HANDOVER_ISSUE_SEVERITY_LABELS } from '@/data/businessLabels'
import {
  issueSeverityTone,
  wipColumns,
  unfinishedWorkOrderColumns,
  openIssueColumns,
  rowPositionKey,
} from '../handoverDetailPresentation'

defineProps<{
  detailWipItems: BusinessConsoleMesShiftHandoverWipItem[]
  detailUnfinishedWorkOrders: BusinessConsoleMesShiftHandoverUnfinishedWorkOrder[]
  detailOpenIssues: BusinessConsoleMesShiftHandoverOpenIssue[]
  handoverDetailPending: boolean
  statusLabel: (status?: string | null) => string
}>()
</script>

<template>
  <section class="grid grid-cols-1 gap-2 [&>*]:min-w-0">
    <h3 class="text-sm font-semibold text-foreground">在制清点</h3>
    <NvDataTable
      :columns="wipColumns"
      :rows="detailWipItems"
      :row-key="rowPositionKey(detailWipItems)"
      :loading="handoverDetailPending"
      :searchable="false"
      :column-settings="false"
      :pagination="false"
      empty-message="交班时点没有登记在制清点。"
    >
      <template #cell-quantity="{ row }"
        ><span class="tabular-nums">{{ row.quantity ?? 0 }}</span></template
      >
    </NvDataTable>
  </section>

  <section class="grid grid-cols-1 gap-2 [&>*]:min-w-0">
    <h3 class="text-sm font-semibold text-foreground">未完工单</h3>
    <NvDataTable
      :columns="unfinishedWorkOrderColumns"
      :rows="detailUnfinishedWorkOrders"
      :row-key="rowPositionKey(detailUnfinishedWorkOrders)"
      :loading="handoverDetailPending"
      :searchable="false"
      :column-settings="false"
      :pagination="false"
      empty-message="交班时点没有未完工单。"
    >
      <template #cell-plannedQuantity="{ row }"
        ><span class="tabular-nums">{{ row.plannedQuantity ?? 0 }}</span></template
      >
      <template #cell-completedQuantity="{ row }"
        ><span class="tabular-nums">{{ row.completedQuantity ?? 0 }}</span></template
      >
      <template #cell-workOrderStatus="{ row }">
        <NvStatusBadge :value="row.workOrderStatus" :label="statusLabel(row.workOrderStatus)" />
      </template>
    </NvDataTable>
  </section>

  <section class="grid grid-cols-1 gap-2 [&>*]:min-w-0">
    <h3 class="text-sm font-semibold text-foreground">设备与质量遗留问题</h3>
    <NvDataTable
      :columns="openIssueColumns"
      :rows="detailOpenIssues"
      :row-key="rowPositionKey(detailOpenIssues)"
      :loading="handoverDetailPending"
      :searchable="false"
      :column-settings="false"
      :pagination="false"
      empty-message="交班时点没有登记遗留问题。"
    >
      <template #cell-severity="{ row }">
        <NvStatusBadge
          :value="row.severity"
          :label="labelFor(MES_HANDOVER_ISSUE_SEVERITY_LABELS, row.severity, '未定级')"
          :tone="issueSeverityTone(row.severity)"
        />
      </template>
    </NvDataTable>
  </section>
</template>
