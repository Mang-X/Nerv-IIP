<script setup lang="ts">
import BusinessContextBar from '@/components/business/BusinessContextBar.vue'
import BusinessEmptyState from '@/components/business/BusinessEmptyState.vue'
import BusinessFormStatus from '@/components/business/BusinessFormStatus.vue'
import BusinessMetricCell from '@/components/business/BusinessMetricCell.vue'
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import BusinessRowActions from '@/components/business/BusinessRowActions.vue'
import BusinessStatusBadge from '@/components/business/BusinessStatusBadge.vue'
import { useMesOperationTasks } from '@/composables/useBusinessMes'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Button,
  DropdownMenuItem,
  DropdownMenuSeparator,
  Field,
  FieldGroup,
  FieldLabel,
  Input,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
  Table,
  TableBody,
  TableCell,
  TableEmpty,
  TableHead,
  TableHeader,
  TableRow,
} from '@nerv-iip/ui'
import { ClipboardCheckIcon, EyeIcon, RefreshCwIcon, ShieldCheckIcon, WrenchIcon } from 'lucide-vue-next'
import { computed, reactive } from 'vue'
import { useRouter } from 'vue-router'

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

const router = useRouter()
const errorMessage = computed(() => formatError(operationTasksError.value))
const readyCount = computed(() => operationTasks.value.filter((item) => item.status === 'Ready').length)
const executionContext = reactive({
  siteCode: '',
  lineCode: '',
  workCenterCode: '',
  shiftCode: '',
})
const statusOptions = [
  { label: '全部状态', value: 'all' },
  { label: '可开工', value: 'Ready' },
  { label: '执行中', value: 'Running' },
  { label: '暂停', value: 'Paused' },
  { label: '已完成', value: 'Completed' },
  { label: '阻塞', value: 'Blocked' },
]
const statusFilter = computed({
  get: () => filters.status || 'all',
  set: (value: string) => {
    filters.status = value === 'all' ? undefined : value
  },
})

function openWorkOrder(workOrderId?: string | null) {
  if (!workOrderId) return
  void router.push({ path: `/mes/work-orders/${encodeURIComponent(workOrderId)}` })
}

function openRoute(path: string) {
  void router.push({ path })
}

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

      <BusinessContextBar
        v-model:environment-id="filters.environmentId"
        v-model:line-code="executionContext.lineCode"
        v-model:organization-id="filters.organizationId"
        v-model:shift-code="executionContext.shiftCode"
        v-model:site-code="executionContext.siteCode"
        v-model:work-center-code="executionContext.workCenterCode"
        title="执行上下文"
      >
        <FieldGroup class="grid gap-3 md:grid-cols-2">
          <Field>
            <FieldLabel for="operation-status">状态</FieldLabel>
            <Select v-model="statusFilter">
              <SelectTrigger id="operation-status" aria-label="工序状态">
                <SelectValue placeholder="全部状态" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem v-for="option in statusOptions" :key="option.value" :value="option.value">
                  {{ option.label }}
                </SelectItem>
              </SelectContent>
            </Select>
          </Field>
          <Field>
            <FieldLabel for="operation-take">数量</FieldLabel>
            <Input id="operation-take" v-model.number="filters.take" inputmode="numeric" type="number" />
          </Field>
        </FieldGroup>
        <BusinessFormStatus :error="errorMessage" />
      </BusinessContextBar>

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
                <TableHead class="text-right">操作</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow v-for="task in operationTasks" :key="task.operationTaskId">
                <TableCell class="font-medium">{{ task.operationTaskId ?? '无编号' }}</TableCell>
                <TableCell>
                  <button
                    v-if="task.workOrderId"
                    class="font-medium text-primary underline-offset-4 hover:underline"
                    type="button"
                    @click="openWorkOrder(task.workOrderId)"
                  >
                    {{ task.workOrderId }}
                  </button>
                  <span v-else>无</span>
                </TableCell>
                <TableCell>
                  <BusinessStatusBadge :value="task.status" />
                </TableCell>
                <TableCell class="tabular-nums">{{ task.operationSequence ?? 0 }}</TableCell>
                <TableCell>{{ task.workCenterId ?? '无' }}</TableCell>
                <TableCell>{{ task.deviceAssetId ?? '未指定' }}</TableCell>
                <TableCell>{{ task.shiftId ?? '未指定' }}</TableCell>
                <TableCell>{{ formatDateTime(task.plannedStartUtc) }}</TableCell>
                <TableCell>{{ task.qualityStatus ?? '未检' }}</TableCell>
                <TableCell class="text-right">
                  <BusinessRowActions :label="`工序任务操作 ${task.operationTaskId ?? ''}`">
                    <DropdownMenuItem :disabled="!task.workOrderId" @click="openWorkOrder(task.workOrderId)">
                      <EyeIcon data-icon="inline-start" />
                      查看工单
                    </DropdownMenuItem>
                    <DropdownMenuItem @click="openRoute('/mes/production-reports')">
                      <ClipboardCheckIcon data-icon="inline-start" />
                      查看报工
                    </DropdownMenuItem>
                    <DropdownMenuSeparator />
                    <DropdownMenuItem @click="openRoute('/quality/inspections')">
                      <ShieldCheckIcon data-icon="inline-start" />
                      呼叫质检
                    </DropdownMenuItem>
                    <DropdownMenuItem @click="openRoute('/mes/capacity')">
                      <WrenchIcon data-icon="inline-start" />
                      记录异常
                    </DropdownMenuItem>
                  </BusinessRowActions>
                </TableCell>
              </TableRow>
              <TableEmpty v-if="!operationTasks.length && !operationTasksPending" :colspan="10">
                <BusinessEmptyState
                  title="当前没有工序任务"
                  description="请检查工单释放、排程结果和工作中心筛选；可开工任务会出现在这里。"
                  action="一线人员通常从此列表进入工单、报工、质检或异常处理。"
                />
              </TableEmpty>
              <TableEmpty v-if="operationTasksPending" :colspan="10">正在加载工序任务...</TableEmpty>
            </TableBody>
          </Table>
        </div>
      </div>
    </section>
  </BusinessLayout>
</template>
