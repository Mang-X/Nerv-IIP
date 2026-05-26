<script setup lang="ts">
import BusinessFormStatus from '@/components/business/BusinessFormStatus.vue'
import BusinessMetricCell from '@/components/business/BusinessMetricCell.vue'
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import { useMesOperationTasks } from '@/composables/useBusinessMes'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Badge,
  Button,
  Field,
  FieldGroup,
  FieldLabel,
  Input,
  Table,
  TableBody,
  TableCell,
  TableEmpty,
  TableHead,
  TableHeader,
  TableRow,
} from '@nerv-iip/ui'
import { RefreshCwIcon } from 'lucide-vue-next'
import { computed } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: '工序执行',
  },
})

const {
  filters,
  operationTasks,
  operationTasksError,
  operationTasksPending,
  refreshOperationTasks,
} = useMesOperationTasks()

const errorMessage = computed(() => formatError(operationTasksError.value))
const readyCount = computed(() => operationTasks.value.filter((item) => item.status === 'Ready').length)

function formatDateTime(value?: string | null) {
  if (!value) return '无'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}

function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败。' : ''
}
</script>

<template>
  <BusinessLayout>
    <section class="grid gap-4">
      <BusinessPageHeader
        domain="MES"
        title="工序执行"
        summary="面向班组长和一线员工查看工序任务、工作中心、设备、班次和质量状态。"
      >
        <template #actions>
          <Button size="sm" type="button" variant="outline" :disabled="operationTasksPending" @click="refreshOperationTasks">
            <RefreshCwIcon data-icon="inline-start" />
            刷新
          </Button>
        </template>
      </BusinessPageHeader>

      <div class="grid gap-3 rounded-lg border bg-background p-4">
        <FieldGroup class="grid gap-3 md:grid-cols-4">
          <Field>
            <FieldLabel for="operation-org">组织</FieldLabel>
            <Input id="operation-org" v-model="filters.organizationId" />
          </Field>
          <Field>
            <FieldLabel for="operation-env">环境</FieldLabel>
            <Input id="operation-env" v-model="filters.environmentId" />
          </Field>
          <Field>
            <FieldLabel for="operation-status">状态</FieldLabel>
            <Input id="operation-status" v-model="filters.status" placeholder="可选" />
          </Field>
          <Field>
            <FieldLabel for="operation-take">数量</FieldLabel>
            <Input id="operation-take" v-model.number="filters.take" inputmode="numeric" type="number" />
          </Field>
        </FieldGroup>
        <BusinessFormStatus :error="errorMessage" />
      </div>

      <div class="grid gap-3 md:grid-cols-3">
        <BusinessMetricCell label="任务数" :value="operationTasks.length" detail="当前筛选结果" />
        <BusinessMetricCell label="可开工" :value="readyCount" detail="Ready 状态任务" />
        <BusinessMetricCell label="非可开工" :value="operationTasks.length - readyCount" detail="需关注任务" />
      </div>

      <div class="overflow-hidden rounded-lg border bg-background">
        <div class="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>工序任务</TableHead>
                <TableHead>工单</TableHead>
                <TableHead>状态</TableHead>
                <TableHead>序号</TableHead>
                <TableHead>工作中心</TableHead>
                <TableHead>设备</TableHead>
                <TableHead>班次</TableHead>
                <TableHead>计划开始</TableHead>
                <TableHead>质量状态</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow v-for="task in operationTasks" :key="task.operationTaskId">
                <TableCell class="font-medium">{{ task.operationTaskId ?? '无编号' }}</TableCell>
                <TableCell>{{ task.workOrderId ?? '无' }}</TableCell>
                <TableCell>
                  <Badge variant="secondary">{{ task.status ?? '未知' }}</Badge>
                </TableCell>
                <TableCell class="tabular-nums">{{ task.operationSequence ?? 0 }}</TableCell>
                <TableCell>{{ task.workCenterId ?? '无' }}</TableCell>
                <TableCell>{{ task.deviceAssetId ?? '未指定' }}</TableCell>
                <TableCell>{{ task.shiftId ?? '未指定' }}</TableCell>
                <TableCell>{{ formatDateTime(task.plannedStartUtc) }}</TableCell>
                <TableCell>{{ task.qualityStatus ?? '未检' }}</TableCell>
              </TableRow>
              <TableEmpty v-if="!operationTasks.length && !operationTasksPending" :colspan="9">
                暂无工序任务。
              </TableEmpty>
              <TableEmpty v-if="operationTasksPending" :colspan="9">正在加载工序任务...</TableEmpty>
            </TableBody>
          </Table>
        </div>
      </div>
    </section>
  </BusinessLayout>
</template>
