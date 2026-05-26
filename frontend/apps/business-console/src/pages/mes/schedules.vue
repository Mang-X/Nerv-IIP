<script setup lang="ts">
import BusinessFormStatus from '@/components/business/BusinessFormStatus.vue'
import BusinessMetricCell from '@/components/business/BusinessMetricCell.vue'
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import { useMesSchedules } from '@/composables/useBusinessMes'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import type {
  BusinessConsoleRunScheduleRequest,
  BusinessConsoleScheduledOperation,
} from '@nerv-iip/api-client'
import {
  Badge,
  Button,
  Field,
  FieldGroup,
  FieldLabel,
  Input,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
  Spinner,
  Table,
  TableBody,
  TableCell,
  TableEmpty,
  TableHead,
  TableHeader,
  TableRow,
} from '@nerv-iip/ui'
import { PlayIcon } from 'lucide-vue-next'
import { computed, reactive, shallowRef } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: '规则排程',
  },
})

const { lastSchedule, runSchedule, runScheduleError, runSchedulePending } = useMesSchedules()

const runSuccess = shallowRef('')

const runForm = reactive({
  organizationId: 'org-001',
  environmentId: 'env-dev',
  trigger: 'Manual',
})

const assignments = computed(() => lastSchedule.value?.assignments ?? [])
const affectedWorkOrderIds = computed(() => lastSchedule.value?.affectedWorkOrderIds ?? [])
const errorMessage = computed(() => formatError(runScheduleError.value))
const canRunSchedule = computed(
  () =>
    isNonEmpty(runForm.organizationId) &&
    isNonEmpty(runForm.environmentId) &&
    isNonEmpty(runForm.trigger),
)

function triggerLabel(value: string) {
  if (value === 'Manual') return '手动'
  if (value === 'RushOrder') return '急单'
  if (value === 'AssetUnavailable') return '设备不可用'
  if (value === 'AssetRestored') return '设备恢复'
  return value
}

async function submitScheduleRun() {
  if (!canRunSchedule.value) return

  const body: BusinessConsoleRunScheduleRequest = {
    organizationId: runForm.organizationId.trim(),
    environmentId: runForm.environmentId.trim(),
    trigger: runForm.trigger.trim(),
  }

  const response = await runSchedule(body)
  runSuccess.value = `排程运行 ${response?.data?.scheduleVersion ?? body.trigger} 已完成。`
}

function assignmentKey(item: BusinessConsoleScheduledOperation, index: number) {
  return `${item.workOrderId ?? 'wo'}:${item.operationTaskId ?? index}`
}

function formatDateTime(value?: string | null) {
  if (!value) return '无'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}

function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败。' : ''
}

function isNonEmpty(value: string) {
  return value.trim().length > 0
}
</script>

<template>
  <BusinessLayout>
    <section class="grid gap-4">
      <BusinessPageHeader
        domain="MES"
        title="规则排程"
        summary="运行规则排程，并用列表查看工序任务的工作中心分配结果。"
      />

      <form class="grid gap-4 rounded-lg border bg-background p-4" @submit.prevent="submitScheduleRun">
        <BusinessFormStatus :error="errorMessage" :success="runSuccess" />

        <FieldGroup class="grid gap-3 md:grid-cols-3">
          <Field>
            <FieldLabel for="schedule-org">组织</FieldLabel>
            <Input id="schedule-org" v-model="runForm.organizationId" required />
          </Field>
          <Field>
            <FieldLabel for="schedule-env">环境</FieldLabel>
            <Input id="schedule-env" v-model="runForm.environmentId" required />
          </Field>
          <Field>
            <FieldLabel>触发来源</FieldLabel>
            <Select v-model="runForm.trigger">
              <SelectTrigger aria-label="排程触发来源">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="Manual">手动</SelectItem>
                <SelectItem value="RushOrder">急单</SelectItem>
                <SelectItem value="AssetUnavailable">设备不可用</SelectItem>
                <SelectItem value="AssetRestored">设备恢复</SelectItem>
              </SelectContent>
            </Select>
          </Field>
        </FieldGroup>

        <div class="flex items-center justify-between gap-3 rounded-lg border p-3">
          <span class="min-w-0 truncate text-sm text-muted-foreground">{{ triggerLabel(runForm.trigger) }}</span>
          <Button type="submit" :disabled="runSchedulePending || !canRunSchedule">
            <Spinner v-if="runSchedulePending" data-icon="inline-start" />
            <PlayIcon v-else data-icon="inline-start" />
            运行排程
          </Button>
        </div>
      </form>

      <div class="grid gap-3 md:grid-cols-3">
        <BusinessMetricCell
          label="版本"
          :value="lastSchedule?.scheduleVersion ?? '无'"
          :detail="lastSchedule?.trigger ? triggerLabel(lastSchedule.trigger) : '尚未运行'"
        />
        <BusinessMetricCell label="分配数" :value="assignments.length" detail="返回的工序分配行" />
        <BusinessMetricCell label="影响工单" :value="affectedWorkOrderIds.length" detail="受影响工单号" />
      </div>

      <div class="overflow-hidden rounded-lg border bg-background">
        <div class="flex items-center justify-between border-b px-4 py-3">
          <h2 class="text-sm font-semibold text-foreground">排程结果</h2>
          <span class="text-sm text-muted-foreground">{{ formatDateTime(lastSchedule?.scheduledAtUtc) }}</span>
        </div>
        <div class="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>工单</TableHead>
                <TableHead>工序</TableHead>
                <TableHead>工作中心</TableHead>
                <TableHead>开始</TableHead>
                <TableHead>结束</TableHead>
                <TableHead>原因</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow v-for="(assignment, index) in assignments" :key="assignmentKey(assignment, index)">
                <TableCell class="font-medium">{{ assignment.workOrderId ?? '无' }}</TableCell>
                <TableCell>{{ assignment.operationTaskId ?? '无' }}</TableCell>
                <TableCell>
                  <Badge variant="secondary">{{ assignment.workCenterId ?? '无' }}</Badge>
                </TableCell>
                <TableCell>{{ formatDateTime(assignment.startUtc) }}</TableCell>
                <TableCell>{{ formatDateTime(assignment.endUtc) }}</TableCell>
                <TableCell>{{ assignment.reason ?? '无' }}</TableCell>
              </TableRow>
              <TableEmpty v-if="!assignments.length && !runSchedulePending" :colspan="6">
                暂无排程分配。
              </TableEmpty>
              <TableEmpty v-if="runSchedulePending" :colspan="6">正在运行排程...</TableEmpty>
            </TableBody>
          </Table>
        </div>
      </div>

      <div v-if="affectedWorkOrderIds.length" class="rounded-lg border bg-background p-4">
        <h2 class="text-sm font-semibold text-foreground">受影响工单</h2>
        <div class="mt-3 flex flex-wrap gap-2">
          <Badge v-for="workOrderId in affectedWorkOrderIds" :key="workOrderId" variant="secondary">
            {{ workOrderId }}
          </Badge>
        </div>
      </div>
    </section>
  </BusinessLayout>
</template>
