<script setup lang="ts">
import type { BusinessConsoleSchedulePlan } from '@nerv-iip/api-client'
import { GanttChart, toModel, conflictReasonLabel } from '@nerv-iip/scheduling'
import { NvDataTable, type NvDataTableColumn } from '@nerv-iip/ui'
import { computed } from 'vue'
import SchedulingMaterialShortageSummary from './SchedulingMaterialShortageSummary.vue'
const props = defineProps<{ plan: BusinessConsoleSchedulePlan }>()
const model = computed(() => toModel(props.plan))
const conflictColumns: NvDataTableColumn<(typeof model.value.conflicts)[number]>[] = [
  { key: 'orderId', header: '工单' },
  { key: 'reason', header: '原因', accessor: (row) => conflictReasonLabel[row.reason] },
  { key: 'message', header: '说明' },
]
const unscheduledColumns: NvDataTableColumn<(typeof model.value.unscheduled)[number]>[] = [
  { key: 'orderId', header: '工单' },
  { key: 'operationId', header: '工序' },
  { key: 'reason', header: '原因', accessor: (row) => conflictReasonLabel[row.reason] },
  { key: 'message', header: '说明' },
]
const metrics = computed(() => {
  const m = props.plan.metrics
  return [
    ['已排工序', m?.scheduledOperationCount],
    ['未排工序', m?.unscheduledOperationCount],
    ['准时率', m?.onTimeRate == null ? undefined : `${(m.onTimeRate * 100).toFixed(1)}%`],
    ['延期分钟', m?.totalTardinessMinutes],
    [
      '平均资源利用率',
      m?.averageResourceUtilization == null
        ? undefined
        : `${(m.averageResourceUtilization * 100).toFixed(1)}%`,
    ],
  ]
})
</script>
<template>
  <section class="grid gap-4 border-t pt-4" aria-label="排产结果">
    <header>
      <h3 class="text-lg font-semibold">
        {{ plan.status === 'preview' ? '插单预览结果' : '已生成方案' }}
      </h3>
      <p class="text-sm text-muted-foreground">
        {{
          plan.status === 'preview'
            ? '仅供核算；原方案不变，未保存或发布新方案，不会写回工单。'
            : `方案 ${plan.planId} 已生成，尚未发布。`
        }}
      </p>
    </header>
    <dl class="grid grid-cols-2 gap-3 sm:grid-cols-3">
      <div v-for="[label, value] in metrics" :key="label">
        <dt class="text-xs text-muted-foreground">{{ label }}</dt>
        <dd class="font-medium tabular-nums">{{ value ?? '未提供' }}</dd>
      </div>
    </dl>
    <div class="h-[24rem] min-w-0"><GanttChart :model="model" read-only /></div>
    <section aria-label="排程冲突">
      <h4 class="mb-2 font-semibold">排程冲突 · {{ model.conflicts.length }} 项</h4>
      <NvDataTable
        v-if="model.conflicts.length"
        :columns="conflictColumns"
        :rows="model.conflicts"
        row-key="id"
        :searchable="false"
        :pagination="false"
        :column-settings="false"
        density="compact"
      />
      <p v-else class="text-sm text-muted-foreground">无冲突</p>
    </section>
    <section aria-label="未排工序">
      <h4 class="mb-2 font-semibold">未排工序 · {{ model.unscheduled.length }} 项</h4>
      <NvDataTable
        v-if="model.unscheduled.length"
        :columns="unscheduledColumns"
        :rows="model.unscheduled"
        :row-key="(row) => `${row.orderId}:${row.operationId}`"
        :searchable="false"
        :pagination="false"
        :column-settings="false"
        density="compact"
      />
      <p v-else class="text-sm text-muted-foreground">全部工序已排产</p>
    </section>
    <SchedulingMaterialShortageSummary :shortages="plan.materialShortageSummary ?? []" />
  </section>
</template>
