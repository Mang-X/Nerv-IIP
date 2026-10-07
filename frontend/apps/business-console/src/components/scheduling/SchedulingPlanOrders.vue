<script setup lang="ts">
import type { SchedulingWorkOrderFacts } from '@/composables/useBusinessMes'
import type { ScheduleModel, ScheduleTask } from '@nerv-iip/scheduling'
import { operationTaskStatusLabel, workOrderStatusLabel } from '@nerv-iip/business-core'
import { NvDataTable, NvStatusBadge, type NvDataTableColumn } from '@nerv-iip/ui'
import { computed } from 'vue'

const props = defineProps<{
  model: ScheduleModel
  workOrders: SchedulingWorkOrderFacts[]
  selectedTaskId: string
  factsUnavailable?: boolean
}>()
const emit = defineEmits<{ select: [taskId: string] }>()
const columns: NvDataTableColumn<ScheduleTask>[] = [
  { key: 'operationId', header: '工序' },
  { key: 'status', header: '状态' },
]
const groups = computed(() => {
  const orders = new Map(props.workOrders.map((order) => [order.workOrderId, order]))
  const byWorkOrder = new Map<string, ScheduleTask[]>()
  for (const task of props.model.tasks) {
    if (task.type !== 'operation' || task.blockKind) continue
    const tasks = byWorkOrder.get(task.orderId) ?? []
    tasks.push(task)
    byWorkOrder.set(task.orderId, tasks)
  }
  const result = new Map<
    string,
    { id: string; order?: SchedulingWorkOrderFacts; tasks: ScheduleTask[] }[]
  >()
  for (const [id, tasks] of byWorkOrder) {
    const order = orders.get(id)
    const labels = props.factsUnavailable
      ? ['销售订单暂不可读取']
      : order?.commercialSourceFacts?.status === 'forbidden'
        ? ['无权读取销售订单']
        : [
            ...new Set(
              (order?.commercialSourceFacts?.salesOrders ?? []).flatMap((link) =>
                link.salesOrderNo ? [link.salesOrderNo] : [],
              ),
            ),
          ]
    if (!labels.length)
      labels.push(
        order?.commercialSourceFacts === undefined ? '销售订单关联未知' : '未关联销售订单',
      )
    for (const label of labels) {
      const list = result.get(label) ?? []
      list.push({
        id,
        order,
        tasks: [...tasks].sort((a, b) => a.operationSequence - b.operationSequence),
      })
      result.set(label, list)
    }
  }
  return [...result].map(([label, orders]) => ({ label, orders }))
})
function operationStatus(task: ScheduleTask, order?: SchedulingWorkOrderFacts) {
  const operation = order?.operationTasks?.find(
    (item) =>
      item.operationTaskId === task.operationId || item.operationTaskNo === task.operationId,
  )
  return operationTaskStatusLabel(operation?.status)
}
function rowClass(task: ScheduleTask) {
  return task.id === props.selectedTaskId
    ? 'bg-primary/10 ring-1 ring-inset ring-primary/40'
    : undefined
}
function progress(order: SchedulingWorkOrderFacts | undefined, tasks: ScheduleTask[]) {
  const current = tasks.find((task) => task.currentExecution?.workOrderProgress)?.currentExecution
    ?.workOrderProgress
  const completed = current?.completedQuantity ?? order?.completedQuantity
  const planned = current?.plannedQuantity ?? order?.quantity
  return completed != null && planned != null ? `${completed} / ${planned}` : '未知'
}
</script>

<template>
  <aside
    class="max-h-[34rem] overflow-y-auto rounded-lg border bg-card xl:w-[20rem] xl:flex-none"
    aria-label="销售订单计划视角"
  >
    <h3 class="sticky top-0 z-10 border-b bg-card px-4 py-3 text-base font-semibold">
      销售订单 · 工单 · 工序
    </h3>
    <section v-for="group in groups" :key="group.label" class="border-b p-3 last:border-b-0">
      <h4 class="mb-3 text-sm font-semibold">{{ group.label }}</h4>
      <div v-for="order in group.orders" :key="order.id" class="mb-3 last:mb-0">
        <div class="mb-2 flex flex-wrap items-center justify-between gap-1 text-xs">
          <span class="font-medium"
            >{{ order.order?.workOrderNo || order.id }} ·
            {{ workOrderStatusLabel(order.order?.executionStatus ?? order.order?.status) }}</span
          >
          <span class="text-muted-foreground"
            >工单进度 {{ progress(order.order, order.tasks) }}</span
          >
        </div>
        <NvDataTable
          :columns="columns"
          :rows="order.tasks"
          row-key="id"
          :searchable="false"
          :pagination="false"
          :column-settings="false"
          :row-class="rowClass"
          density="compact"
          @row-click="emit('select', $event.id)"
        >
          <template #cell-operationId="{ row }">
            <button
              type="button"
              class="text-left text-primary hover:underline"
              :data-operation="row.id"
              :aria-pressed="row.id === selectedTaskId"
              @click.stop="emit('select', row.id)"
            >
              {{ row.operationId }}
            </button>
            <p v-if="row.predecessors?.length" class="mt-1 text-xs text-muted-foreground">
              前序 {{ row.predecessors.join('、') }}
            </p>
          </template>
          <template #cell-status="{ row }">
            <NvStatusBadge :label="operationStatus(row, order.order)" tone="neutral" />
          </template>
        </NvDataTable>
      </div>
    </section>
  </aside>
</template>
