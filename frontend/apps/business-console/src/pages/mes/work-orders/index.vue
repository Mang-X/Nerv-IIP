<script setup lang="ts">
import BusinessActionSheet from '@/components/business/BusinessActionSheet.vue'
import BusinessContextBar from '@/components/business/BusinessContextBar.vue'
import BusinessEmptyState from '@/components/business/BusinessEmptyState.vue'
import BusinessFormStatus from '@/components/business/BusinessFormStatus.vue'
import BusinessMetricCell from '@/components/business/BusinessMetricCell.vue'
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import BusinessRowActions from '@/components/business/BusinessRowActions.vue'
import BusinessStatusBadge from '@/components/business/BusinessStatusBadge.vue'
import BusinessTablePagination from '@/components/business/BusinessTablePagination.vue'
import { useBusinessMasterDataResources, useBusinessSkus } from '@/composables/useBusinessMasterData'
import { useMesWorkOrders } from '@/composables/useBusinessMes'
import { demoResourcesOf, demoSkus, demoWorkOrders, mergeByKey, readLocalDemoWorkOrders } from '@/data/shockAbsorberDemo'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import type {
  BusinessConsoleCreateRushWorkOrderRequest,
  BusinessConsoleMesWorkOrderItem,
  BusinessConsoleRecordProductionReportRequest,
  BusinessConsoleResourceItem,
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
import {
  ArrowDownIcon,
  ArrowUpDownIcon,
  ArrowUpIcon,
  CalendarCheckIcon,
  ClipboardCheckIcon,
  EyeIcon,
  FactoryIcon,
  PackageCheckIcon,
  RefreshCwIcon,
  RouteIcon,
  WrenchIcon,
} from 'lucide-vue-next'
import { computed, reactive, shallowRef, watch } from 'vue'
import { useRouter } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '工单与派工',
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
const { skus } = useBusinessSkus()
const { resources: siteResources } = useBusinessMasterDataResources('site')
const { resources: lineResources } = useBusinessMasterDataResources('production-line')
const { resources: workCenterResources } = useBusinessMasterDataResources('work-center')
const { resources: shiftResources } = useBusinessMasterDataResources('shift')
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
type SortColumn = 'workOrderId' | 'skuId' | 'status' | 'quantity' | 'dueUtc' | 'operationCount'

const tableState = reactive({
  page: 1,
  pageSize: '10',
  sortBy: 'dueUtc' as SortColumn,
  sortDirection: 'asc' as 'asc' | 'desc',
})
const filterDraft = reactive({
  keyword: '',
  status: 'all',
})
const appliedFilter = reactive({
  keyword: '',
  status: 'all',
})
const appliedScope = reactive({
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
const demandEntries = [
  {
    title: '正常订单',
    description: '销售订单进入计划池',
    action: '去生产计划',
    path: '/mes/plans?source=sales',
  },
  {
    title: '备货生产',
    description: '主生产计划确认后下达',
    action: '查看计划来源',
    path: '/mes/plans?source=stock',
  },
  {
    title: '安全库存补充',
    description: '库存水位触发补货',
    action: '处理补货计划',
    path: '/mes/plans?source=safety',
  },
  {
    title: '急单插单',
    description: '临时插单或返工补单',
    action: '创建急单',
    path: '',
  },
]

const rushForm = reactive({
  organizationId: filters.organizationId,
  environmentId: filters.environmentId,
  skuId: 'FG-SAD-FRT-001',
  productionVersionId: '',
  quantity: '1',
  dueUtc: toLocalDateTimeInput(new Date(Date.now() + 86_400_000)),
  workCenterId: 'WC-001',
  operationTaskId: '',
  operationSequence: '10',
  durationMinutes: '60',
  idempotencyKey: newMesIdempotencyKey('rush-work-order'),
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
  () => visibleWorkOrders.value.filter((order) => (order.status ?? '').toLowerCase() !== 'closed').length,
)
const blockedOrderCount = computed(
  () => visibleWorkOrders.value.filter((order) => ['blocked', 'hold', 'held'].includes((order.status ?? '').toLowerCase())).length,
)
const readyOrderCount = computed(
  () => visibleWorkOrders.value.filter((order) => ['ready', 'released'].includes((order.status ?? '').toLowerCase())).length,
)
const runningOrderCount = computed(
  () => visibleWorkOrders.value.filter((order) => ['running', 'inprogress', 'started'].includes((order.status ?? '').toLowerCase())).length,
)
const operationCount = computed(() =>
  visibleWorkOrders.value.reduce((total, order) => total + (order.operationTasks?.length ?? 0), 0),
)
const dispatchLanes = computed(() => [
  {
    title: '待派工',
    value: readyOrderCount.value,
    description: '已具备下达或开工条件，优先确认工作中心与班次。',
    tone: 'border-primary/20 bg-primary/5',
  },
  {
    title: '执行中',
    value: runningOrderCount.value,
    description: '现场已经开始生产，关注报工、质量和停机异常。',
    tone: 'border-blue-500/20 bg-blue-500/5',
  },
  {
    title: '受阻',
    value: blockedOrderCount.value,
    description: '先处理物料、质量、设备或准备检查问题。',
    tone: blockedOrderCount.value > 0 ? 'border-destructive/30 bg-destructive/5' : 'border-emerald-500/20 bg-emerald-500/5',
  },
])
const canCreateRush = computed(
  () =>
    isNonEmpty(rushForm.organizationId) &&
    isNonEmpty(rushForm.environmentId) &&
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
const siteOptions = computed(() => toResourceOptions(siteResources.value.length ? siteResources.value : demoResourcesOf('site')))
const lineOptions = computed(() => toResourceOptions(lineResources.value.length ? lineResources.value : demoResourcesOf('production-line')))
const workCenterOptions = computed(() => toResourceOptions(workCenterResources.value.length ? workCenterResources.value : demoResourcesOf('work-center')))
const shiftOptions = computed(() => toResourceOptions(shiftResources.value.length ? shiftResources.value : demoResourcesOf('shift')))
const skuOptions = computed(() => toResourceOptions(skus.value.length ? skus.value : demoSkus))
const localWorkOrders = shallowRef<BusinessConsoleMesWorkOrderItem[]>(readLocalDemoWorkOrders())
const sourceWorkOrders = computed(() =>
  mergeByKey([...localWorkOrders.value, ...workOrders.value, ...demoWorkOrders], (order) => order.workOrderId),
)
const visibleWorkOrders = computed(() => {
  const keyword = appliedFilter.keyword.trim().toLowerCase()
  const workCenter = appliedScope.workCenterCode.trim().toLowerCase()

  return sourceWorkOrders.value.filter((order) => {
    const statusMatched = appliedFilter.status === 'all' || order.status === appliedFilter.status
    const keywordMatched =
      !keyword ||
      [order.workOrderId, order.skuId, order.productionVersionId, order.status]
        .some((value) => (value ?? '').toLowerCase().includes(keyword))
    const workCenterMatched =
      !workCenter ||
      (order.operationTasks ?? []).some((task) => (task.workCenterId ?? '').toLowerCase() === workCenter)

    return statusMatched && keywordMatched && workCenterMatched
  })
})
const sortedWorkOrders = computed(() => {
  const direction = tableState.sortDirection === 'asc' ? 1 : -1

  return [...visibleWorkOrders.value].sort((left, right) => {
    const leftValue = sortValue(left, tableState.sortBy)
    const rightValue = sortValue(right, tableState.sortBy)

    if (typeof leftValue === 'number' && typeof rightValue === 'number') {
      return (leftValue - rightValue) * direction
    }

    return String(leftValue).localeCompare(String(rightValue), 'zh-Hans-CN') * direction
  })
})
const pageSizeNumber = computed(() => Number(tableState.pageSize) || 10)
const pagedWorkOrders = computed(() => {
  const start = (tableState.page - 1) * pageSizeNumber.value
  return sortedWorkOrders.value.slice(start, start + pageSizeNumber.value)
})

watch(
  () => [
    appliedFilter.keyword,
    appliedFilter.status,
    tableState.pageSize,
    appliedScope.workCenterCode,
    sourceWorkOrders.value.length,
  ],
  () => {
    tableState.page = 1
  },
)

watch(
  () => rushForm.skuId,
  (skuId) => {
    if (skuId === 'FG-SAD-RR-001') {
      rushForm.productionVersionId = 'PV-RR-2026-B'
      rushForm.workCenterId = rushForm.workCenterId || 'WC-OIL-FILL'
      return
    }
    if (skuId === 'FG-SAD-FRT-001') {
      rushForm.productionVersionId = 'PV-FRT-2026-A'
      rushForm.workCenterId = rushForm.workCenterId || 'WC-TUBE-WELD'
    }
  },
  { immediate: true },
)

function syncContextFromFilters() {
  rushForm.organizationId = filters.organizationId
  rushForm.environmentId = filters.environmentId
  reportForm.organizationId = filters.organizationId
  reportForm.environmentId = filters.environmentId
}

function applyFilters() {
  appliedFilter.keyword = filterDraft.keyword
  appliedFilter.status = filterDraft.status
  appliedScope.siteCode = executionContext.siteCode
  appliedScope.lineCode = executionContext.lineCode
  appliedScope.workCenterCode = executionContext.workCenterCode
  appliedScope.shiftCode = executionContext.shiftCode
  filters.status = appliedFilter.status === 'all' ? undefined : appliedFilter.status
}

function clearFilters() {
  filterDraft.keyword = ''
  filterDraft.status = 'all'
  executionContext.siteCode = ''
  executionContext.lineCode = ''
  executionContext.workCenterCode = ''
  executionContext.shiftCode = ''
  applyFilters()
}

function openDemandEntry(path: string) {
  if (!path) {
    rushSheetOpen.value = true
    return
  }

  void router.push(path)
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

function openRelatedPage(path: string, order: BusinessConsoleMesWorkOrderItem) {
  void router.push({
    path,
    query: {
      workOrderId: order.workOrderId ?? undefined,
      skuId: order.skuId ?? undefined,
    },
  })
}

async function submitRushWorkOrder() {
  if (!canCreateRush.value) return

  const body: BusinessConsoleCreateRushWorkOrderRequest = {
    organizationId: rushForm.organizationId.trim(),
    environmentId: rushForm.environmentId.trim(),
    skuId: rushForm.skuId.trim(),
    productionVersionId: optionalText(rushForm.productionVersionId),
    quantity: toOptionalNumber(rushForm.quantity),
    dueUtc: toIsoFromLocalInput(rushForm.dueUtc),
    workCenterId: rushForm.workCenterId.trim(),
    operationTaskId: optionalText(rushForm.operationTaskId),
    operationSequence: toOptionalInteger(rushForm.operationSequence),
    durationMinutes: toOptionalInteger(rushForm.durationMinutes),
    idempotencyKey: rushForm.idempotencyKey,
  }

  const response = await createRushWorkOrder(body)
  rushSuccess.value = `急单 ${response?.data?.workOrderId ?? '已提交'} 已提交。`
  lastRushAffectedWorkOrders.value = response?.data?.affectedWorkOrderIds ?? []
  lastRushScheduleVersion.value = response?.data?.schedule?.scheduleVersion
  rushForm.idempotencyKey = newMesIdempotencyKey('rush-work-order')
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

function setSort(column: SortColumn) {
  if (tableState.sortBy === column) {
    tableState.sortDirection = tableState.sortDirection === 'asc' ? 'desc' : 'asc'
    return
  }

  tableState.sortBy = column
  tableState.sortDirection = 'asc'
}

function sortIcon(column: SortColumn) {
  if (tableState.sortBy !== column) return ArrowUpDownIcon
  return tableState.sortDirection === 'asc' ? ArrowUpIcon : ArrowDownIcon
}

function sortValue(order: BusinessConsoleMesWorkOrderItem, column: SortColumn) {
  if (column === 'quantity') return order.quantity ?? 0
  if (column === 'dueUtc') return order.dueUtc ? new Date(order.dueUtc).getTime() : 0
  if (column === 'operationCount') return order.operationTasks?.length ?? 0
  return order[column] ?? ''
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

function formatStatus(value?: string | null) {
  const map: Record<string, string> = {
    blocked: '阻塞',
    closed: '已关闭',
    completed: '已完成',
    queued: '排队中',
    ready: '可开工',
    released: '已下达',
    running: '执行中',
  }
  return value ? (map[value.toLowerCase()] ?? value) : '未知'
}

function newMesIdempotencyKey(scope: string) {
  return `${scope}-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`
}

function orderKey(order: BusinessConsoleMesWorkOrderItem, index: number) {
  return `${order.workOrderId ?? 'wo'}:${index}`
}

function toResourceOptions(items: BusinessConsoleResourceItem[]) {
  return items
    .filter((item) => item.active !== false && item.code)
    .map((item) => ({
      label: item.displayName ? `${item.displayName} (${item.code})` : item.code!,
      value: item.code!,
    }))
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
        title="工单与派工"
        kicker="调度员工作台"
        summary="围绕工单安排生产顺序、查看工序任务、发起齐套检查和报工，避免一线人员在多个页面之间手工拼编号。"
      >
        <template #actions>
          <Button size="sm" type="button" variant="outline" @click="router.push('/mes/plans')">
            <CalendarCheckIcon data-icon="inline-start" />
            生产计划
          </Button>
          <Button size="sm" type="button" variant="outline" @click="rushSheetOpen = true">
            <FactoryIcon data-icon="inline-start" />
            创建急单
          </Button>
          <Button size="sm" type="button" variant="outline" :disabled="workOrdersPending" @click="refreshWorkOrders">
            <RefreshCwIcon data-icon="inline-start" />
            刷新
          </Button>
        </template>
      </BusinessPageHeader>

      <div class="flex flex-wrap items-center gap-2 rounded-lg border bg-background px-4 py-3">
        <span class="text-sm font-semibold text-foreground">工单来源</span>
        <button
          v-for="entry in demandEntries"
          :key="entry.title"
          class="inline-flex items-center gap-2 rounded-md border px-3 py-2 text-sm transition-colors hover:border-primary/50 hover:bg-muted/40"
          type="button"
          @click="openDemandEntry(entry.path)"
        >
          <span class="font-medium text-foreground">{{ entry.title }}</span>
          <span class="text-muted-foreground">{{ entry.description }}</span>
          <span class="font-medium text-primary">{{ entry.action }}</span>
        </button>
      </div>

      <BusinessContextBar
        v-model:environment-id="filters.environmentId"
        v-model:line-code="executionContext.lineCode"
        v-model:organization-id="filters.organizationId"
        v-model:shift-code="executionContext.shiftCode"
        v-model:site-code="executionContext.siteCode"
        v-model:work-center-code="executionContext.workCenterCode"
        :line-options="lineOptions"
        :shift-options="shiftOptions"
        :site-options="siteOptions"
        title="生产范围"
        :work-center-options="workCenterOptions"
        @change="syncContextFromFilters"
      >
        <FieldGroup class="grid gap-3 md:grid-cols-[minmax(0,1fr)_220px_auto]">
          <Field>
            <FieldLabel for="work-order-keyword">搜索</FieldLabel>
            <Input id="work-order-keyword" v-model="filterDraft.keyword" placeholder="工单、物料、生产版本" @keydown.enter="applyFilters" />
          </Field>
          <Field>
            <FieldLabel for="work-order-status">状态</FieldLabel>
            <Select v-model="filterDraft.status">
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
          <div class="flex items-end gap-2">
            <Button type="button" @click="applyFilters">查询</Button>
            <Button type="button" variant="outline" @click="clearFilters">清空</Button>
          </div>
        </FieldGroup>
        <BusinessFormStatus :error="listErrorMessage" />
      </BusinessContextBar>

      <div class="grid gap-3 lg:grid-cols-3">
        <div
          v-for="lane in dispatchLanes"
          :key="lane.title"
          class="grid gap-3 rounded-lg border p-4"
          :class="lane.tone"
        >
          <div class="flex items-center justify-between">
            <p class="text-sm font-semibold text-foreground">{{ lane.title }}</p>
            <span class="text-2xl font-semibold tabular-nums">{{ lane.value }}</span>
          </div>
          <p class="text-sm leading-6 text-muted-foreground">{{ lane.description }}</p>
        </div>
      </div>

      <div class="grid gap-3 md:grid-cols-3">
        <BusinessMetricCell label="工单数" :value="visibleWorkOrders.length" detail="当前筛选结果" />
        <BusinessMetricCell label="未关闭工单" :value="openOrderCount" detail="仍需现场跟进" />
        <BusinessMetricCell label="工序任务" :value="operationCount" detail="工单下可见任务" />
      </div>

      <div class="overflow-hidden rounded-lg border bg-background">
        <div class="flex items-center justify-between border-b px-4 py-3">
          <h2 class="text-sm font-semibold text-foreground">工单列表</h2>
          <span class="text-sm text-muted-foreground">工单 / 工序 / 派工</span>
        </div>
        <div class="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>
                  <Button class="-ml-3" size="sm" type="button" variant="ghost" @click="setSort('workOrderId')">
                    工单
                    <component :is="sortIcon('workOrderId')" data-icon="inline-end" />
                  </Button>
                </TableHead>
                <TableHead>
                  <Button class="-ml-3" size="sm" type="button" variant="ghost" @click="setSort('status')">
                    状态
                    <component :is="sortIcon('status')" data-icon="inline-end" />
                  </Button>
                </TableHead>
                <TableHead class="text-right">
                  <Button class="-mr-3" size="sm" type="button" variant="ghost" @click="setSort('quantity')">
                    数量
                    <component :is="sortIcon('quantity')" data-icon="inline-end" />
                  </Button>
                </TableHead>
                <TableHead>
                  <Button class="-ml-3" size="sm" type="button" variant="ghost" @click="setSort('dueUtc')">
                    交期
                    <component :is="sortIcon('dueUtc')" data-icon="inline-end" />
                  </Button>
                </TableHead>
                <TableHead>
                  <Button class="-ml-3" size="sm" type="button" variant="ghost" @click="setSort('operationCount')">
                    工序
                    <component :is="sortIcon('operationCount')" data-icon="inline-end" />
                  </Button>
                </TableHead>
                <TableHead class="text-right">操作</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow v-for="(order, index) in pagedWorkOrders" :key="orderKey(order, index)">
                <TableCell>
                  <div class="flex flex-col gap-0.5">
                    <RouterLink
                      class="font-medium text-primary underline-offset-4 hover:underline"
                      :to="{ path: `/mes/work-orders/${order.workOrderId}` }"
                    >
                      {{ order.workOrderId ?? '无编号' }}
                    </RouterLink>
                    <span class="text-xs text-muted-foreground">{{ order.skuId ?? '无物料' }}</span>
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
                      {{ formatStatus(task.status) }}
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
                    <DropdownMenuItem @click="openRelatedPage('/mes/materials', order)">
                      <PackageCheckIcon data-icon="inline-start" />
                      齐套检查
                    </DropdownMenuItem>
                    <DropdownMenuItem @click="openRelatedPage('/mes/operation-tasks', order)">
                      <RouteIcon data-icon="inline-start" />
                      查看工序
                    </DropdownMenuItem>
                    <DropdownMenuSeparator />
                    <DropdownMenuItem @click="useWorkOrder(order)">
                      <ClipboardCheckIcon data-icon="inline-start" />
                      生产报工
                    </DropdownMenuItem>
                    <DropdownMenuItem @click="openRelatedPage('/mes/capacity', order)">
                      <WrenchIcon data-icon="inline-start" />
                      异常与产能
                    </DropdownMenuItem>
                  </BusinessRowActions>
                </TableCell>
              </TableRow>
              <TableEmpty v-if="!visibleWorkOrders.length && !workOrdersPending" :colspan="6">
                <BusinessEmptyState
                  title="当前筛选下没有工单"
                  description="可以调整状态、工作中心或搜索条件；正常生产请先进入生产计划，急单只处理临时插单。"
                  action="生产计划转工单后会回到这里继续派工、齐套检查和报工。"
                />
              </TableEmpty>
              <TableEmpty v-if="workOrdersPending" :colspan="6">正在加载工单...</TableEmpty>
            </TableBody>
          </Table>
        </div>
        <div class="border-t px-4 py-3">
          <BusinessTablePagination
            v-model:page="tableState.page"
            v-model:page-size="tableState.pageSize"
            :total-items="sortedWorkOrders.length"
          />
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
              <FieldLabel for="rush-sku">物料 <span class="text-destructive">*</span></FieldLabel>
              <Select v-if="skuOptions.length" v-model="rushForm.skuId">
                <SelectTrigger id="rush-sku">
                  <SelectValue placeholder="选择物料" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem v-for="option in skuOptions" :key="option.value" :value="option.value">
                    {{ option.label }}
                  </SelectItem>
                </SelectContent>
              </Select>
              <Input v-else id="rush-sku" v-model="rushForm.skuId" required />
            </Field>
            <Field>
              <FieldLabel for="rush-version">生产版本</FieldLabel>
              <Input id="rush-version" v-model="rushForm.productionVersionId" />
            </Field>
            <Field>
              <FieldLabel for="rush-quantity">数量 <span class="text-destructive">*</span></FieldLabel>
              <Input id="rush-quantity" v-model="rushForm.quantity" inputmode="decimal" required type="number" />
            </Field>
            <Field>
              <FieldLabel for="rush-due">交期 <span class="text-destructive">*</span></FieldLabel>
              <Input id="rush-due" v-model="rushForm.dueUtc" required type="datetime-local" />
            </Field>
            <Field>
              <FieldLabel for="rush-work-center">工作中心 <span class="text-destructive">*</span></FieldLabel>
              <Select v-if="workCenterOptions.length" v-model="rushForm.workCenterId">
                <SelectTrigger id="rush-work-center">
                  <SelectValue placeholder="选择工作中心" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem v-for="option in workCenterOptions" :key="option.value" :value="option.value">
                    {{ option.label }}
                  </SelectItem>
                </SelectContent>
              </Select>
              <Input v-else id="rush-work-center" v-model="rushForm.workCenterId" required />
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
              <FieldLabel for="rush-duration">工时分钟 <span class="text-destructive">*</span></FieldLabel>
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
        description="从工单或工序任务进入报工，系统带出必要字段，一线人员只补充数量和完成状态。"
      >
        <form class="grid content-start gap-4 rounded-lg border bg-background p-4" @submit.prevent="submitProductionReport">
          <div>
            <p class="text-xs font-bold uppercase text-primary">报工</p>
            <h2 class="text-base font-semibold text-foreground">生产报工</h2>
          </div>
          <BusinessFormStatus :error="reportErrorMessage" :success="reportSuccess" />

          <FieldGroup class="grid gap-3 sm:grid-cols-2">
            <Field>
              <FieldLabel for="report-work-order">工单号 <span class="text-destructive">*</span></FieldLabel>
              <Input id="report-work-order" v-model="reportForm.workOrderId" required />
            </Field>
            <Field>
              <FieldLabel for="report-operation-task">工序任务 <span class="text-destructive">*</span></FieldLabel>
              <Input id="report-operation-task" v-model="reportForm.operationTaskId" required />
            </Field>
            <Field>
              <FieldLabel for="report-good">良品数 <span class="text-destructive">*</span></FieldLabel>
              <Input id="report-good" v-model="reportForm.goodQuantity" inputmode="decimal" min="0" required type="number" />
            </Field>
            <Field>
              <FieldLabel for="report-scrap">报废数 <span class="text-destructive">*</span></FieldLabel>
              <Input id="report-scrap" v-model="reportForm.scrapQuantity" inputmode="decimal" min="0" required type="number" />
              <FieldDescription>良品和报废必须为非负数，合计必须大于 0。</FieldDescription>
            </Field>
            <Field>
              <FieldLabel for="report-time">报工时间 <span class="text-destructive">*</span></FieldLabel>
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
