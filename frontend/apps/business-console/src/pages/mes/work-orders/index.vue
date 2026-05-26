<script setup lang="ts">
import BusinessActionSheet from '@/components/business/BusinessActionSheet.vue'
import BusinessContextBar from '@/components/business/BusinessContextBar.vue'
import BusinessEmptyState from '@/components/business/BusinessEmptyState.vue'
import BusinessFormStatus from '@/components/business/BusinessFormStatus.vue'
import BusinessMetricCell from '@/components/business/BusinessMetricCell.vue'
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import BusinessRowActions from '@/components/business/BusinessRowActions.vue'
import BusinessStatusBadge from '@/components/business/BusinessStatusBadge.vue'
import { useMesWorkOrders } from '@/composables/useBusinessMes'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import type {
  BusinessConsoleCreateRushWorkOrderRequest,
  BusinessConsoleMesWorkOrderItem,
  BusinessConsoleRecordProductionReportRequest,
} from '@nerv-iip/api-client'
import {
  Button,
  Checkbox,
  DropdownMenuItem,
  DropdownMenuSeparator,
  Field,
  FieldDescription,
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
import { ClipboardCheckIcon, EyeIcon, FactoryIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, shallowRef } from 'vue'
import { useRouter } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '计划与工单',
  },
})

const {
  createRushWorkOrder,
  createRushWorkOrderError,
  createRushWorkOrderPending,
  filters,
  recordProductionReport,
  recordProductionReportError,
  recordProductionReportPending,
  refreshWorkOrders,
  workOrders,
  workOrdersError,
  workOrdersPending,
} = useMesWorkOrders()

const router = useRouter()
const rushSuccess = shallowRef('')
const reportSuccess = shallowRef('')
const rushSheetOpen = shallowRef(false)
const reportSheetOpen = shallowRef(false)
const lastRushAffectedWorkOrders = shallowRef<string[]>([])
const lastRushScheduleVersion = shallowRef<number>()
const executionContext = reactive({
  siteCode: '',
  lineCode: '',
  workCenterCode: '',
  shiftCode: '',
})
const statusOptions = [
  { label: '全部状态', value: 'all' },
  { label: '已下达', value: 'Released' },
  { label: '可开工', value: 'Ready' },
  { label: '执行中', value: 'Running' },
  { label: '已完成', value: 'Completed' },
  { label: '已关闭', value: 'Closed' },
  { label: '阻塞', value: 'Blocked' },
]

const rushForm = reactive({
  organizationId: filters.organizationId,
  environmentId: filters.environmentId,
  workOrderId: '',
  skuId: 'SKU-001',
  productionVersionId: '',
  quantity: '1',
  dueUtc: toLocalDateTimeInput(new Date(Date.now() + 86_400_000)),
  workCenterId: 'WC-001',
  operationTaskId: '',
  operationSequence: '10',
  durationMinutes: '60',
})

const reportForm = reactive({
  organizationId: filters.organizationId,
  environmentId: filters.environmentId,
  workOrderId: '',
  operationTaskId: '',
  goodQuantity: '1',
  scrapQuantity: '0',
  completesOperation: true,
  reportedAtUtc: toLocalDateTimeInput(new Date()),
})

const listErrorMessage = computed(() => formatError(workOrdersError.value))
const rushErrorMessage = computed(() => formatError(createRushWorkOrderError.value))
const reportErrorMessage = computed(() => formatError(recordProductionReportError.value))
const reportGoodQuantity = computed(() => toOptionalNumber(reportForm.goodQuantity))
const reportScrapQuantity = computed(() => toOptionalNumber(reportForm.scrapQuantity))
const reportQuantitiesAreValid = computed(
  () =>
    reportGoodQuantity.value !== undefined &&
    reportScrapQuantity.value !== undefined &&
    reportGoodQuantity.value >= 0 &&
    reportScrapQuantity.value >= 0 &&
    reportGoodQuantity.value + reportScrapQuantity.value > 0,
)
const openOrderCount = computed(
  () => workOrders.value.filter((order) => (order.status ?? '').toLowerCase() !== 'closed').length,
)
const operationCount = computed(() =>
  workOrders.value.reduce((total, order) => total + (order.operationTasks?.length ?? 0), 0),
)
const canCreateRush = computed(
  () =>
    isNonEmpty(rushForm.organizationId) &&
    isNonEmpty(rushForm.environmentId) &&
    isNonEmpty(rushForm.workOrderId) &&
    isNonEmpty(rushForm.skuId) &&
    toOptionalNumber(rushForm.quantity) !== undefined &&
    isNonEmpty(rushForm.dueUtc) &&
    isNonEmpty(rushForm.workCenterId) &&
    toOptionalNumber(rushForm.durationMinutes) !== undefined,
)
const canRecordReport = computed(
  () =>
    isNonEmpty(reportForm.organizationId) &&
    isNonEmpty(reportForm.environmentId) &&
    isNonEmpty(reportForm.workOrderId) &&
    isNonEmpty(reportForm.operationTaskId) &&
    reportQuantitiesAreValid.value &&
    isNonEmpty(reportForm.reportedAtUtc),
)
const statusFilter = computed({
  get: () => filters.status || 'all',
  set: (value: string) => {
    filters.status = value === 'all' ? undefined : value
  },
})

function syncContextFromFilters() {
  rushForm.organizationId = filters.organizationId
  rushForm.environmentId = filters.environmentId
  reportForm.organizationId = filters.organizationId
  reportForm.environmentId = filters.environmentId
}

function useWorkOrder(order: BusinessConsoleMesWorkOrderItem) {
  reportForm.workOrderId = order.workOrderId ?? ''
  const firstTask = order.operationTasks?.[0]
  if (firstTask?.operationTaskId) {
    reportForm.operationTaskId = firstTask.operationTaskId
  }
  reportSheetOpen.value = true
}

function openOrderDetail(order: BusinessConsoleMesWorkOrderItem) {
  if (!order.workOrderId) return
  void router.push({ path: `/mes/work-orders/${encodeURIComponent(order.workOrderId)}` })
}

async function submitRushWorkOrder() {
  if (!canCreateRush.value) return

  const body: BusinessConsoleCreateRushWorkOrderRequest = {
    organizationId: rushForm.organizationId.trim(),
    environmentId: rushForm.environmentId.trim(),
    workOrderId: rushForm.workOrderId.trim(),
    skuId: rushForm.skuId.trim(),
    productionVersionId: optionalText(rushForm.productionVersionId),
    quantity: toOptionalNumber(rushForm.quantity),
    dueUtc: toIsoFromLocalInput(rushForm.dueUtc),
    workCenterId: rushForm.workCenterId.trim(),
    operationTaskId: optionalText(rushForm.operationTaskId),
    operationSequence: toOptionalInteger(rushForm.operationSequence),
    durationMinutes: toOptionalInteger(rushForm.durationMinutes),
  }

  const response = await createRushWorkOrder(body)
  rushSuccess.value = `急单 ${response?.data?.workOrderId ?? body.workOrderId} 已提交。`
  lastRushAffectedWorkOrders.value = response?.data?.affectedWorkOrderIds ?? []
  lastRushScheduleVersion.value = response?.data?.schedule?.scheduleVersion
}

async function submitProductionReport() {
  if (!canRecordReport.value) return

  const body: BusinessConsoleRecordProductionReportRequest = {
    organizationId: reportForm.organizationId.trim(),
    environmentId: reportForm.environmentId.trim(),
    workOrderId: reportForm.workOrderId.trim(),
    operationTaskId: reportForm.operationTaskId.trim(),
    goodQuantity: reportGoodQuantity.value,
    scrapQuantity: reportScrapQuantity.value,
    completesOperation: reportForm.completesOperation,
    reportedAtUtc: toIsoFromLocalInput(reportForm.reportedAtUtc),
  }

  const response = await recordProductionReport(body)
  reportSuccess.value = `生产报工 ${response?.data?.productionReportId ?? body.workOrderId} 已提交。`
}

function optionalText(value: string) {
  const trimmed = value.trim()
  return trimmed ? trimmed : undefined
}

function toOptionalNumber(value: string) {
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : undefined
}

function toOptionalInteger(value: string) {
  const parsed = Number.parseInt(value, 10)
  return Number.isFinite(parsed) ? parsed : undefined
}

function toIsoFromLocalInput(value: string) {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toISOString()
}

function toLocalDateTimeInput(date: Date) {
  const offset = date.getTimezoneOffset() * 60_000
  return new Date(date.getTime() - offset).toISOString().slice(0, 16)
}

function formatDateTime(value?: string | null) {
  if (!value) return '无'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}

function formatQuantity(value?: number) {
  return new Intl.NumberFormat(undefined, {
    maximumFractionDigits: 3,
  }).format(value ?? 0)
}

function orderKey(order: BusinessConsoleMesWorkOrderItem, index: number) {
  return `${order.workOrderId ?? 'wo'}:${index}`
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
        title="计划与工单"
        summary="以工单列表和工序上下文为中心处理急单、详情查看和报工动作。"
      >
        <template #actions>
          <Button size="sm" type="button" @click="rushSheetOpen = true">
            <FactoryIcon data-icon="inline-start" />
            创建急单
          </Button>
          <Button size="sm" type="button" variant="outline" :disabled="workOrdersPending" @click="refreshWorkOrders">
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
        title="派工上下文"
        @change="syncContextFromFilters"
      >
        <FieldGroup class="grid gap-3 md:grid-cols-2">
          <Field>
            <FieldLabel for="work-order-status">状态</FieldLabel>
            <Select v-model="statusFilter">
              <SelectTrigger id="work-order-status" aria-label="工单状态">
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
            <FieldLabel for="work-order-take">数量</FieldLabel>
            <Input id="work-order-take" v-model.number="filters.take" inputmode="numeric" type="number" />
          </Field>
        </FieldGroup>
        <BusinessFormStatus :error="listErrorMessage" />
      </BusinessContextBar>

      <div class="grid gap-3 md:grid-cols-3">
        <BusinessMetricCell label="工单数" :value="workOrders.length" detail="当前筛选结果" />
        <BusinessMetricCell label="未关闭工单" :value="openOrderCount" detail="非 Closed 状态" />
        <BusinessMetricCell label="工序任务" :value="operationCount" detail="工单下可见任务" />
      </div>

      <div class="overflow-hidden rounded-lg border bg-background">
        <div class="flex items-center justify-between border-b px-4 py-3">
          <h2 class="text-sm font-semibold text-foreground">工单列表</h2>
          <span class="text-sm text-muted-foreground">返回 {{ workOrders.length }} 条</span>
        </div>
        <div class="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>工单</TableHead>
                <TableHead>状态</TableHead>
                <TableHead class="text-right">数量</TableHead>
                <TableHead>交期</TableHead>
                <TableHead>工序</TableHead>
                <TableHead class="text-right">操作</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow v-for="(order, index) in workOrders" :key="orderKey(order, index)">
                <TableCell>
                  <div class="flex flex-col gap-0.5">
                    <RouterLink
                      class="font-medium text-primary underline-offset-4 hover:underline"
                      :to="{ path: `/mes/work-orders/${order.workOrderId}` }"
                    >
                      {{ order.workOrderId ?? '无编号' }}
                    </RouterLink>
                    <span class="text-xs text-muted-foreground">{{ order.skuId ?? '无 SKU' }}</span>
                  </div>
                </TableCell>
                <TableCell>
                  <BusinessStatusBadge :value="order.status" />
                </TableCell>
                <TableCell class="text-right tabular-nums">{{ formatQuantity(order.quantity) }}</TableCell>
                <TableCell>{{ formatDateTime(order.dueUtc) }}</TableCell>
                <TableCell>
                  <div class="grid gap-1">
                    <span
                      v-for="task in order.operationTasks ?? []"
                      :key="task.operationTaskId ?? `${order.workOrderId}-${task.operationSequence}`"
                      class="text-xs text-muted-foreground"
                    >
                      {{ task.operationSequence ?? '无' }} /
                      {{ task.workCenterId ?? '无' }} /
                      {{ task.status ?? '未知' }}
                    </span>
                    <span v-if="!(order.operationTasks?.length)" class="text-xs text-muted-foreground">
                      暂无工序任务
                    </span>
                  </div>
                </TableCell>
                <TableCell class="text-right">
                  <BusinessRowActions :label="`工单操作 ${order.workOrderId ?? ''}`">
                    <DropdownMenuItem @click="openOrderDetail(order)">
                      <EyeIcon data-icon="inline-start" />
                      查看详情
                    </DropdownMenuItem>
                    <DropdownMenuSeparator />
                    <DropdownMenuItem @click="useWorkOrder(order)">
                      <ClipboardCheckIcon data-icon="inline-start" />
                      生产报工
                    </DropdownMenuItem>
                  </BusinessRowActions>
                </TableCell>
              </TableRow>
              <TableEmpty v-if="!workOrders.length && !workOrdersPending" :colspan="6">
                <BusinessEmptyState
                  title="当前筛选下没有工单"
                  description="可以调整状态筛选，或从右上角创建急单进入插单流程。"
                  action="急单创建完成后会回到工单列表继续派工和报工。"
                />
              </TableEmpty>
              <TableEmpty v-if="workOrdersPending" :colspan="6">正在加载工单...</TableEmpty>
            </TableBody>
          </Table>
        </div>
      </div>

      <BusinessActionSheet
        v-model:open="rushSheetOpen"
        title="创建急单"
        description="急单用于生产插单和临时补单；提交后系统返回受影响工单和排程版本。"
      >
        <form class="grid gap-4 rounded-lg border bg-background p-4" @submit.prevent="submitRushWorkOrder">
          <div>
            <p class="text-xs font-bold uppercase text-primary">急单</p>
            <h2 class="text-base font-semibold text-foreground">创建急单</h2>
          </div>
          <BusinessFormStatus :error="rushErrorMessage" :success="rushSuccess" />

          <FieldGroup class="grid gap-3 sm:grid-cols-2">
            <Field>
              <FieldLabel for="rush-org">组织</FieldLabel>
              <Input id="rush-org" v-model="rushForm.organizationId" required />
            </Field>
            <Field>
              <FieldLabel for="rush-env">环境</FieldLabel>
              <Input id="rush-env" v-model="rushForm.environmentId" required />
            </Field>
            <Field>
              <FieldLabel for="rush-work-order">工单号</FieldLabel>
              <Input id="rush-work-order" v-model="rushForm.workOrderId" required />
            </Field>
            <Field>
              <FieldLabel for="rush-sku">SKU</FieldLabel>
              <Input id="rush-sku" v-model="rushForm.skuId" required />
            </Field>
            <Field>
              <FieldLabel for="rush-version">生产版本</FieldLabel>
              <Input id="rush-version" v-model="rushForm.productionVersionId" />
            </Field>
            <Field>
              <FieldLabel for="rush-quantity">数量</FieldLabel>
              <Input id="rush-quantity" v-model="rushForm.quantity" inputmode="decimal" required type="number" />
            </Field>
            <Field>
              <FieldLabel for="rush-due">交期</FieldLabel>
              <Input id="rush-due" v-model="rushForm.dueUtc" required type="datetime-local" />
            </Field>
            <Field>
              <FieldLabel for="rush-work-center">工作中心</FieldLabel>
              <Input id="rush-work-center" v-model="rushForm.workCenterId" required />
            </Field>
            <Field>
              <FieldLabel for="rush-operation-task">工序任务</FieldLabel>
              <Input id="rush-operation-task" v-model="rushForm.operationTaskId" />
            </Field>
            <Field>
              <FieldLabel for="rush-operation-sequence">工序序号</FieldLabel>
              <Input id="rush-operation-sequence" v-model="rushForm.operationSequence" inputmode="numeric" type="number" />
            </Field>
            <Field>
              <FieldLabel for="rush-duration">工时分钟</FieldLabel>
              <Input id="rush-duration" v-model="rushForm.durationMinutes" inputmode="numeric" required type="number" />
            </Field>
          </FieldGroup>

          <div v-if="lastRushScheduleVersion || lastRushAffectedWorkOrders.length" class="grid gap-2 rounded-lg border p-3">
            <p class="text-sm font-semibold text-foreground">排程结果</p>
            <p v-if="lastRushScheduleVersion" class="text-sm text-muted-foreground">
              排程版本 {{ lastRushScheduleVersion }}
            </p>
            <p v-if="lastRushAffectedWorkOrders.length" class="text-sm text-muted-foreground">
              受影响工单：{{ lastRushAffectedWorkOrders.join(', ') }}
            </p>
          </div>

          <div class="flex justify-end">
            <Button type="submit" :disabled="createRushWorkOrderPending || !canCreateRush">
              <Spinner v-if="createRushWorkOrderPending" data-icon="inline-start" />
              <FactoryIcon v-else data-icon="inline-start" />
              创建急单
            </Button>
          </div>
        </form>
      </BusinessActionSheet>

      <BusinessActionSheet
        v-model:open="reportSheetOpen"
        title="生产报工"
        description="从工单或工序任务进入报工，避免一线人员手动拼接上下文。"
      >
        <form class="grid content-start gap-4 rounded-lg border bg-background p-4" @submit.prevent="submitProductionReport">
          <div>
            <p class="text-xs font-bold uppercase text-primary">报工</p>
            <h2 class="text-base font-semibold text-foreground">生产报工</h2>
          </div>
          <BusinessFormStatus :error="reportErrorMessage" :success="reportSuccess" />

          <FieldGroup class="grid gap-3 sm:grid-cols-2">
            <Field>
              <FieldLabel for="report-org">组织</FieldLabel>
              <Input id="report-org" v-model="reportForm.organizationId" required />
            </Field>
            <Field>
              <FieldLabel for="report-env">环境</FieldLabel>
              <Input id="report-env" v-model="reportForm.environmentId" required />
            </Field>
            <Field>
              <FieldLabel for="report-work-order">工单号</FieldLabel>
              <Input id="report-work-order" v-model="reportForm.workOrderId" required />
            </Field>
            <Field>
              <FieldLabel for="report-operation-task">工序任务</FieldLabel>
              <Input id="report-operation-task" v-model="reportForm.operationTaskId" required />
            </Field>
            <Field>
              <FieldLabel for="report-good">良品数</FieldLabel>
              <Input id="report-good" v-model="reportForm.goodQuantity" inputmode="decimal" min="0" required type="number" />
            </Field>
            <Field>
              <FieldLabel for="report-scrap">报废数</FieldLabel>
              <Input id="report-scrap" v-model="reportForm.scrapQuantity" inputmode="decimal" min="0" required type="number" />
              <FieldDescription>良品和报废必须为非负数，合计必须大于 0。</FieldDescription>
            </Field>
            <Field>
              <FieldLabel for="report-time">报工时间</FieldLabel>
              <Input id="report-time" v-model="reportForm.reportedAtUtc" required type="datetime-local" />
            </Field>
            <Field orientation="horizontal" class="items-center justify-between rounded-lg border p-3">
              <FieldLabel for="report-complete">完成当前工序</FieldLabel>
              <Checkbox id="report-complete" v-model:checked="reportForm.completesOperation" />
            </Field>
          </FieldGroup>

          <div class="flex justify-end">
            <Button type="submit" :disabled="recordProductionReportPending || !canRecordReport">
              <Spinner v-if="recordProductionReportPending" data-icon="inline-start" />
              <ClipboardCheckIcon v-else data-icon="inline-start" />
              提交报工
            </Button>
          </div>
        </form>
      </BusinessActionSheet>
    </section>
  </BusinessLayout>
</template>
