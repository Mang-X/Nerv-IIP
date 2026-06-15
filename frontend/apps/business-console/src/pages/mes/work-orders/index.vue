<script setup lang="ts">
import type {
  BusinessConsoleCreateRushWorkOrderRequest,
  BusinessConsoleMesWorkOrderItem,
  BusinessConsoleRecordProductionReportRequest,
  BusinessConsoleResourceItem,
} from '@nerv-iip/api-client'
import type { DataTableColumn, DataTableSort } from '@nerv-iip/ui'
import { useBusinessMasterDataResources, useBusinessSkus } from '@/composables/useBusinessMasterData'
import { useMesWorkOrders } from '@/composables/useBusinessMes'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Button,
  Checkbox,
  DataTable,
  DataTablePagination,
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DropdownMenuItem,
  DropdownMenuSeparator,
  Field,
  FieldDescription,
  FieldGroup,
  FieldLabel,
  Input,
  PageHeader,
  RowActions,
  SectionCard,
  SectionCards,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
  Spinner,
  StatusBadge,
  Toolbar,
} from '@nerv-iip/ui'
import { watchDebounced } from '@vueuse/core'
import {
  CalendarCheckIcon,
  ClipboardCheckIcon,
  EyeIcon,
  FactoryIcon,
  PackageCheckIcon,
  RefreshCwIcon,
  RouteIcon,
  WrenchIcon,
} from 'lucide-vue-next'
import { computed, reactive, ref, shallowRef, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'

definePage({ meta: { requiresAuth: true, title: '工单与派工' } })

type Row = BusinessConsoleMesWorkOrderItem

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
  workOrdersTotal,
} = useMesWorkOrders()

const route = useRoute()
const router = useRouter()
const { skus } = useBusinessSkus()
const { resources: workCenterResources } = useBusinessMasterDataResources('work-center')

const rushSuccess = shallowRef('')
const reportSuccess = shallowRef('')
const rushSheetOpen = shallowRef(false)
const reportSheetOpen = shallowRef(false)
const lastRushAffectedWorkOrders = shallowRef<string[]>([])
const lastRushScheduleVersion = shallowRef<number>()

// --- Filters (live) ---
const keyword = ref('')
const statusFilter = ref('all')
const workCenterFilter = ref('all')

watch(statusFilter, (value) => {
  filters.status = value === 'all' ? undefined : value
})
watchDebounced(keyword, (value) => {
  filters.keyword = value.trim() || undefined
}, { debounce: 300, maxWait: 1000 })
watch(workCenterFilter, (value) => {
  filters.workCenterId = value === 'all' ? undefined : value
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
  { title: '正常订单', description: '销售订单进入计划池', action: '去生产计划', path: '/mes/plans?source=sales' },
  { title: '备货生产', description: '主生产计划确认后下达', action: '查看计划来源', path: '/mes/plans?source=stock' },
  { title: '安全库存补充', description: '库存水位触发补货', action: '处理补货计划', path: '/mes/plans?source=safety' },
  { title: '急单插单', description: '临时插单或返工补单', action: '创建急单', path: '' },
]

const rushForm = reactive({
  organizationId: filters.organizationId,
  environmentId: filters.environmentId,
  skuId: '',
  productionVersionId: '',
  quantity: '1',
  dueUtc: toLocalDateTimeInput(new Date(Date.now() + 86_400_000)),
  workCenterId: '',
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
  idempotencyKey: newMesIdempotencyKey('production-report'),
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

const workCenterOptions = computed(() => toResourceOptions(workCenterResources.value))
const skuOptions = computed(() => toResourceOptions(skus.value))

const visibleWorkOrders = computed(() => workOrders.value)

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
  { title: '待派工', value: readyOrderCount.value, description: '已具备下达或开工条件，优先确认工作中心与班次。', tone: 'border-primary/20 bg-primary/5' },
  { title: '执行中', value: runningOrderCount.value, description: '现场已经开始生产，关注报工、质量和停机异常。', tone: 'border-brand/30 bg-brand/5' },
  { title: '受阻', value: blockedOrderCount.value, description: '先处理物料、质量、设备或准备检查问题。', tone: blockedOrderCount.value > 0 ? 'border-destructive/30 bg-destructive/5' : 'border-success/30 bg-success/5' },
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

// --- Sort (page-owned, before pagination) ---
const sort = ref<DataTableSort | null>({ key: 'dueUtc', direction: 'asc' })
function sortValue(order: Row, key: string): string | number {
  if (key === 'quantity') return order.quantity ?? 0
  if (key === 'dueUtc') return order.dueUtc ? new Date(order.dueUtc).getTime() : 0
  if (key === 'operationCount') return order.operationTasks?.length ?? 0
  return (order[key as keyof Row] as string | null) ?? ''
}
const sortedWorkOrders = computed(() => {
  if (!sort.value) return visibleWorkOrders.value
  const { key, direction } = sort.value
  const factor = direction === 'asc' ? 1 : -1
  return [...visibleWorkOrders.value].sort((a, b) => {
    const av = sortValue(a, key)
    const bv = sortValue(b, key)
    if (typeof av === 'number' && typeof bv === 'number') return (av - bv) * factor
    return String(av).localeCompare(String(bv), 'zh-Hans-CN') * factor
  })
})

// --- Pagination (server-driven: filters.skip/take, total from backend) ---
const page = ref(1)
const pageSize = ref('10')
const pageSizeNumber = computed(() => Number(pageSize.value) || 10)
// 后端已分页和过滤，当前页内仅做展示排序，不再切片。
const pagedWorkOrders = computed(() => sortedWorkOrders.value)
watch([keyword, statusFilter, workCenterFilter, pageSize], () => {
  page.value = 1
})
watch([page, pageSize], () => {
  filters.skip = (page.value - 1) * pageSizeNumber.value
  filters.take = pageSizeNumber.value
}, { immediate: true })

watch(
  () => route.query,
  (query) => {
    const workOrderId = firstQueryValue(query.workOrderId)
    const operationTaskId = firstQueryValue(query.operationTaskId)
    if (!workOrderId || !operationTaskId) return
    reportForm.workOrderId = workOrderId
    reportForm.operationTaskId = operationTaskId
    reportSheetOpen.value = true
  },
  { immediate: true },
)

const columns: DataTableColumn<Row>[] = [
  { key: 'workOrderId', header: '工单', sortable: true, cellClass: 'font-medium' },
  { key: 'status', header: '状态', sortable: true, width: 'w-24' },
  { key: 'quantity', header: '数量', align: 'end', sortable: true, width: 'w-24', accessor: (r) => r.quantity ?? 0 },
  { key: 'dueUtc', header: '交期', sortable: true, width: 'w-44', accessor: (r) => (r.dueUtc ? new Date(r.dueUtc).getTime() : 0) },
  { key: 'operationCount', header: '工序', sortable: true, accessor: (r) => r.operationTasks?.length ?? 0 },
  { key: 'actions', header: '操作', align: 'end', width: 'w-12' },
]

function rowKey(order: Row) {
  return order.workOrderId ?? `${order.skuId ?? 'wo'}-${order.dueUtc ?? ''}`
}

function openDemandEntry(path: string) {
  if (!path) {
    rushSheetOpen.value = true
    return
  }
  void router.push(path)
}

function useWorkOrder(order: Row) {
  reportForm.workOrderId = order.workOrderId ?? ''
  reportForm.operationTaskId = order.operationTasks?.[0]?.operationTaskId ?? ''
  reportSuccess.value = ''
  reportSheetOpen.value = true
}
function canReportOrder(order: Row) {
  return Boolean(order.workOrderId && order.operationTasks?.some((task) => task.operationTaskId))
}
function openOrderDetail(order: Row) {
  if (!order.workOrderId) return
  void router.push({ path: `/mes/work-orders/${encodeURIComponent(order.workOrderId)}` })
}
function openRelatedPage(path: string, order: Row) {
  void router.push({ path, query: { workOrderId: order.workOrderId ?? undefined, skuId: order.skuId ?? undefined } })
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
    idempotencyKey: reportForm.idempotencyKey,
  }
  const response = await recordProductionReport(body)
  reportSuccess.value = `生产报工 ${response?.data?.reportNo ?? response?.data?.productionReportId ?? body.workOrderId} 已提交。`
  reportForm.idempotencyKey = newMesIdempotencyKey('production-report')
}

function resetFilters() {
  keyword.value = ''
  statusFilter.value = 'all'
  workCenterFilter.value = 'all'
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
  return new Intl.NumberFormat(undefined, { maximumFractionDigits: 3 }).format(value ?? 0)
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
function toResourceOptions(items: BusinessConsoleResourceItem[]) {
  return items
    .filter((item) => item.active !== false && item.code)
    .map((item) => ({ label: item.displayName ? `${item.displayName} (${item.code})` : item.code!, value: item.code! }))
}
function firstQueryValue(value: unknown) {
  if (Array.isArray(value)) return typeof value[0] === 'string' ? value[0] : ''
  return typeof value === 'string' ? value : ''
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
function isNonEmpty(value: string) {
  return value.trim().length > 0
}
</script>

<template>
  <BusinessLayout>
    <PageHeader
      title="工单与派工"
      :breadcrumbs="[{ label: '制造执行' }]"
      :count="`${workOrdersTotal} 个工单`"
    >
      <template #actions>
        <Button size="sm" type="button" variant="outline" @click="router.push('/mes/plans')">
          <CalendarCheckIcon aria-hidden="true" />
          生产计划
        </Button>
        <Button size="sm" type="button" variant="outline" @click="rushSheetOpen = true">
          <FactoryIcon aria-hidden="true" />
          创建急单
        </Button>
        <Button size="sm" type="button" variant="outline" :disabled="workOrdersPending" @click="refreshWorkOrders">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
      </template>
    </PageHeader>

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

    <div class="grid gap-3 lg:grid-cols-3">
      <div v-for="lane in dispatchLanes" :key="lane.title" class="grid gap-3 rounded-lg border p-4" :class="lane.tone">
        <div class="flex items-center justify-between">
          <p class="text-sm font-semibold text-foreground">{{ lane.title }}</p>
          <span class="text-2xl font-semibold tabular-nums">{{ lane.value }}</span>
        </div>
        <p class="text-sm leading-6 text-muted-foreground">{{ lane.description }}</p>
      </div>
    </div>

    <SectionCards :columns="3">
      <SectionCard description="工单数" :value="workOrdersTotal" hint="后端分页总数" />
      <SectionCard description="未关闭工单" :value="openOrderCount" hint="仍需现场跟进" />
      <SectionCard description="工序任务" :value="operationCount" hint="工单下可见任务" />
    </SectionCards>

    <Toolbar v-model:search="keyword" search-placeholder="搜索工单、物料、生产版本">
      <template #filters>
        <Select v-model="statusFilter">
          <SelectTrigger class="h-9 w-32" aria-label="工单状态"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem v-for="option in statusOptions" :key="option.value" :value="option.value">{{ option.label }}</SelectItem>
          </SelectContent>
        </Select>
        <Select v-model="workCenterFilter">
          <SelectTrigger class="h-9 w-40" aria-label="工作中心"><SelectValue placeholder="全部工作中心" /></SelectTrigger>
          <SelectContent>
            <SelectItem value="all">全部工作中心</SelectItem>
            <SelectItem v-for="option in workCenterOptions" :key="option.value" :value="option.value">{{ option.label }}</SelectItem>
          </SelectContent>
        </Select>
      </template>
      <template #actions>
        <Button type="button" variant="ghost" size="sm" @click="resetFilters">重置</Button>
      </template>
    </Toolbar>

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">{{ listErrorMessage }}</p>

    <DataTable
      v-model:sort="sort"
      :columns="columns"
      :rows="pagedWorkOrders"
      :row-key="rowKey"
      :client-sort="false"
      :loading="workOrdersPending"
      empty-message="当前筛选下没有工单。正常生产请先进入生产计划转工单，急单只处理临时插单。"
    >
      <template #cell-workOrderId="{ row }">
        <RouterLink
          v-if="row.workOrderId"
          :to="`/mes/work-orders/${encodeURIComponent(row.workOrderId)}`"
          class="flex flex-col gap-0.5 text-left"
        >
          <span class="font-medium text-brand underline-offset-4 hover:underline">{{ row.workOrderId }}</span>
          <span class="text-xs text-muted-foreground">{{ row.skuId ?? '无物料' }}</span>
        </RouterLink>
        <div v-else class="flex flex-col gap-0.5">
          <span class="font-medium text-muted-foreground">无编号</span>
          <span class="text-xs text-muted-foreground">{{ row.skuId ?? '无物料' }}</span>
        </div>
      </template>
      <template #cell-status="{ row }"><StatusBadge :value="row.status" /></template>
      <template #cell-quantity="{ row }"><span class="tabular-nums">{{ formatQuantity(row.quantity) }}</span></template>
      <template #cell-dueUtc="{ row }">{{ formatDateTime(row.dueUtc) }}</template>
      <template #cell-operationCount="{ row }">
        <div class="grid gap-1">
          <span
            v-for="task in row.operationTasks ?? []"
            :key="task.operationTaskId ?? `${row.workOrderId}-${task.operationSequence}`"
            class="text-xs text-muted-foreground"
          >
            {{ task.operationSequence ?? '无' }} / {{ task.workCenterId ?? '无' }} / {{ formatStatus(task.status) }}
          </span>
          <span v-if="!(row.operationTasks?.length)" class="text-xs text-muted-foreground">暂无工序任务</span>
        </div>
      </template>
      <template #cell-actions="{ row }">
        <RowActions :label="`工单操作 ${row.workOrderId ?? ''}`">
          <DropdownMenuItem @click="openOrderDetail(row)">
            <EyeIcon aria-hidden="true" />
            查看详情
          </DropdownMenuItem>
          <DropdownMenuItem @click="openRelatedPage('/mes/materials', row)">
            <PackageCheckIcon aria-hidden="true" />
            齐套检查
          </DropdownMenuItem>
          <DropdownMenuItem @click="openRelatedPage('/mes/operation-tasks', row)">
            <RouteIcon aria-hidden="true" />
            查看工序
          </DropdownMenuItem>
          <DropdownMenuSeparator />
          <DropdownMenuItem :disabled="!canReportOrder(row)" @click="useWorkOrder(row)">
            <ClipboardCheckIcon aria-hidden="true" />
            {{ canReportOrder(row) ? '生产报工' : '暂无工序，不能报工' }}
          </DropdownMenuItem>
          <DropdownMenuItem @click="openRelatedPage('/mes/capacity', row)">
            <WrenchIcon aria-hidden="true" />
            异常与产能
          </DropdownMenuItem>
        </RowActions>
      </template>
    </DataTable>

    <DataTablePagination v-model:page="page" v-model:page-size="pageSize" :total-items="workOrdersTotal" />

    <Dialog v-model:open="rushSheetOpen">
      <DialogContent class="sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>创建急单</DialogTitle>
          <DialogDescription>急单用于生产插单和临时补单；提交后系统返回受影响工单和排程版本。</DialogDescription>
        </DialogHeader>
        <form class="grid gap-4" @submit.prevent="submitRushWorkOrder">
          <p v-if="rushErrorMessage" class="text-sm text-destructive" role="alert">{{ rushErrorMessage }}</p>
          <p v-if="rushSuccess" class="text-sm text-success" role="status">{{ rushSuccess }}</p>

          <FieldGroup class="grid gap-3 sm:grid-cols-2">
            <Field>
              <FieldLabel for="rush-sku">物料 <span class="text-destructive">*</span></FieldLabel>
              <Select v-if="skuOptions.length" v-model="rushForm.skuId">
                <SelectTrigger id="rush-sku"><SelectValue placeholder="选择物料" /></SelectTrigger>
                <SelectContent>
                  <SelectItem v-for="option in skuOptions" :key="option.value" :value="option.value">{{ option.label }}</SelectItem>
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
                <SelectTrigger id="rush-work-center"><SelectValue placeholder="选择工作中心" /></SelectTrigger>
                <SelectContent>
                  <SelectItem v-for="option in workCenterOptions" :key="option.value" :value="option.value">{{ option.label }}</SelectItem>
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
            <p v-if="lastRushScheduleVersion" class="text-sm text-muted-foreground">排程版本 {{ lastRushScheduleVersion }}</p>
            <p v-if="lastRushAffectedWorkOrders.length" class="text-sm text-muted-foreground">受影响工单：{{ lastRushAffectedWorkOrders.join(', ') }}</p>
          </div>

          <DialogFooter>
            <Button type="button" variant="outline" @click="rushSheetOpen = false">取消</Button>
            <Button type="submit" :disabled="createRushWorkOrderPending || !canCreateRush">
              <Spinner v-if="createRushWorkOrderPending" aria-hidden="true" />
              <FactoryIcon v-else aria-hidden="true" />
              创建急单
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>

    <Dialog v-model:open="reportSheetOpen">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>生产报工</DialogTitle>
          <DialogDescription>从工单或工序任务进入报工，系统带出必要字段，一线人员只补充数量和完成状态。</DialogDescription>
        </DialogHeader>
        <form class="grid content-start gap-4" @submit.prevent="submitProductionReport">
          <p v-if="reportErrorMessage" class="text-sm text-destructive" role="alert">{{ reportErrorMessage }}</p>
          <p v-if="reportSuccess" class="text-sm text-success" role="status">{{ reportSuccess }}</p>

          <FieldGroup class="grid gap-3 sm:grid-cols-2">
            <Field class="sm:col-span-2">
              <FieldLabel>报工对象</FieldLabel>
              <div class="rounded-lg border bg-muted/30 px-3 py-2 text-sm text-muted-foreground">
                工单与工序来自所选行，只能从工单列表或工序任务带入。
              </div>
            </Field>
            <Field>
              <FieldLabel for="report-work-order">工单号 <span class="text-destructive">*</span></FieldLabel>
              <Input id="report-work-order" v-model="reportForm.workOrderId" readonly required />
            </Field>
            <Field>
              <FieldLabel for="report-operation-task">工序任务 <span class="text-destructive">*</span></FieldLabel>
              <Input id="report-operation-task" v-model="reportForm.operationTaskId" readonly required />
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

          <DialogFooter>
            <Button type="button" variant="outline" @click="reportSheetOpen = false">取消</Button>
            <Button type="submit" :disabled="recordProductionReportPending || !canRecordReport">
              <Spinner v-if="recordProductionReportPending" aria-hidden="true" />
              <ClipboardCheckIcon v-else aria-hidden="true" />
              提交报工
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  </BusinessLayout>
</template>
