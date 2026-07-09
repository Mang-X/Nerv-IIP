<script setup lang="ts">
import type {
  BusinessConsoleCreateRushWorkOrderRequest,
  BusinessConsoleMesWorkOrderItem,
  BusinessConsoleRecordProductionReportRequest,
  BusinessConsoleResourceItem,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn, DataTableSort } from '@nerv-iip/ui'
import { mesWorkOrderStatusOptions } from '@/composables/mes/useMesReferenceLabels'
import { useMesDisplayNames } from '@/composables/mes/useMesDisplayNames'
import {
  useBusinessMasterDataResources,
  useBusinessSkus,
} from '@/composables/useBusinessMasterData'
import { useMesWorkOrders } from '@/composables/useBusinessMes'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvButton,
  NvCheckbox,
  NvDataTable,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvDropdownMenuItem,
  NvDropdownMenuSeparator,
  NvField,
  NvFieldDescription,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvPageHeader,
  NvRowActions,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  Spinner,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import { watchDebounced } from '@vueuse/core'
import {
  CalendarCheckIcon,
  CalendarCogIcon,
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

definePage({
  meta: {
    requiresAuth: true,
    title: '工单与派工',
    requiredPermissions: ['business.mes.work-orders.read'],
  },
})

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
const { resolveSku, resolveWorkCenter } = useMesDisplayNames()
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
watchDebounced(
  keyword,
  (value) => {
    filters.keyword = value.trim() || undefined
  },
  { debounce: 300, maxWait: 1000 },
)
watch(workCenterFilter, (value) => {
  filters.workCenterId = value === 'all' ? undefined : value
})

const statusOptions = mesWorkOrderStatusOptions
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
watch(
  [page, pageSize],
  () => {
    filters.skip = (page.value - 1) * pageSizeNumber.value
    filters.take = pageSizeNumber.value
  },
  { immediate: true },
)

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

const columns: NvDataTableColumn<Row>[] = [
  { key: 'workOrderId', header: '工单', sortable: true, cellClass: 'font-medium' },
  { key: 'status', header: '状态', sortable: true, width: 'w-24' },
  {
    key: 'quantity',
    header: '数量',
    align: 'end',
    sortable: true,
    width: 'w-24',
    accessor: (r) => r.quantity ?? 0,
  },
  {
    key: 'dueUtc',
    header: '交期',
    sortable: true,
    width: 'w-44',
    accessor: (r) => (r.dueUtc ? new Date(r.dueUtc).getTime() : 0),
  },
  {
    key: 'operationCount',
    header: '工序',
    sortable: true,
    accessor: (r) => r.operationTasks?.length ?? 0,
  },
  { key: 'actions', header: '操作', align: 'end', width: 'w-12' },
]

function rowKey(order: Row) {
  return order.workOrderId ?? `${order.skuId ?? 'wo'}-${order.dueUtc ?? ''}`
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
  void router.push({
    path,
    query: { workOrderId: order.workOrderId ?? undefined, skuId: order.skuId ?? undefined },
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
    inprogress: '执行中',
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
    .map((item) => ({
      label: item.displayName ? `${item.displayName} (${item.code})` : item.code!,
      value: item.code!,
    }))
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
    <NvPageHeader
      title="工单与派工"
      :breadcrumbs="[{ label: '制造执行' }]"
      :count="`${workOrdersTotal} 个工单`"
    >
      <template #actions>
        <NvButton size="sm" type="button" variant="outline" @click="router.push('/mes/plans')">
          <CalendarCheckIcon aria-hidden="true" />
          生产计划
        </NvButton>
        <NvButton size="sm" type="button" variant="outline" @click="rushSheetOpen = true">
          <FactoryIcon aria-hidden="true" />
          创建急单
        </NvButton>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="workOrdersPending"
          @click="refreshWorkOrders"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <NvToolbar v-model:search="keyword" search-placeholder="搜索工单、物料、生产版本">
      <template #filters>
        <NvSelect v-model="statusFilter">
          <NvSelectTrigger class="h-9 w-32" aria-label="工单状态"
            ><NvSelectValue
          /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem
              v-for="option in statusOptions"
              :key="option.value"
              :value="option.value"
              >{{ option.label }}</NvSelectItem
            >
          </NvSelectContent>
        </NvSelect>
        <NvSelect v-model="workCenterFilter">
          <NvSelectTrigger class="h-9 w-40" aria-label="工作中心"
            ><NvSelectValue placeholder="全部工作中心"
          /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem value="all">全部工作中心</NvSelectItem>
            <NvSelectItem
              v-for="option in workCenterOptions"
              :key="option.value"
              :value="option.value"
              >{{ option.label }}</NvSelectItem
            >
          </NvSelectContent>
        </NvSelect>
      </template>
      <template #actions>
        <NvButton type="button" variant="ghost" size="sm" @click="resetFilters">重置</NvButton>
      </template>
    </NvToolbar>

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">
      {{ listErrorMessage }}
    </p>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="workOrdersTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      v-model:sort="sort"
      :columns="columns"
      :rows="pagedWorkOrders"
      :row-key="rowKey"
      :client-sort="false"
      :loading="workOrdersPending"
      empty-message="当前筛选下没有工单。正常生产请先进入生产计划转工单，急单只处理临时插单。"
      :searchable="false"
      :column-settings="false"
    >
      <template #cell-workOrderId="{ row }">
        <RouterLink
          v-if="row.workOrderId"
          :to="`/mes/work-orders/${encodeURIComponent(row.workOrderId)}`"
          class="flex flex-col gap-0.5 text-left"
        >
          <span class="font-medium text-brand underline-offset-4 hover:underline">{{
            row.workOrderNo ?? row.workOrderId
          }}</span>
          <span class="text-xs text-muted-foreground">{{
            resolveSku(row.skuCode ?? row.skuId) ?? '无'
          }}</span>
        </RouterLink>
        <div v-else class="flex flex-col gap-0.5">
          <span class="font-medium text-muted-foreground">无编号</span>
          <span class="text-xs text-muted-foreground">{{
            resolveSku(row.skuCode ?? row.skuId) ?? '无'
          }}</span>
        </div>
      </template>
      <template #cell-status="{ row }"><NvStatusBadge :value="row.status" /></template>
      <template #cell-quantity="{ row }"
        ><span class="tabular-nums">{{ formatQuantity(row.quantity) }}</span></template
      >
      <template #cell-dueUtc="{ row }">{{ formatDateTime(row.dueUtc) }}</template>
      <template #cell-operationCount="{ row }">
        <div class="grid gap-1">
          <span
            v-for="task in row.operationTasks ?? []"
            :key="task.operationTaskId ?? `${row.workOrderId}-${task.operationSequence}`"
            class="text-xs text-muted-foreground"
          >
            {{ task.operationSequence ?? '无' }} /
            {{
              task.workCenterName ??
              resolveWorkCenter(task.workCenterCode ?? task.workCenterId) ??
              '无'
            }}
            / {{ task.operationTaskNo ?? task.operationTaskId ?? '无任务' }} /
            {{ formatStatus(task.status) }}
          </span>
          <span v-if="!row.operationTasks?.length" class="text-xs text-muted-foreground"
            >暂无工序任务</span
          >
        </div>
      </template>
      <template #cell-actions="{ row }">
        <NvRowActions :label="`工单操作 ${row.workOrderId ?? ''}`">
          <NvDropdownMenuItem @click="openOrderDetail(row)">
            <EyeIcon aria-hidden="true" />
            查看详情
          </NvDropdownMenuItem>
          <NvDropdownMenuItem @click="openRelatedPage('/mes/materials', row)">
            <PackageCheckIcon aria-hidden="true" />
            齐套检查
          </NvDropdownMenuItem>
          <NvDropdownMenuItem @click="openRelatedPage('/mes/operation-tasks', row)">
            <RouteIcon aria-hidden="true" />
            查看工序
          </NvDropdownMenuItem>
          <NvDropdownMenuSeparator />
          <NvDropdownMenuItem :disabled="!canReportOrder(row)" @click="useWorkOrder(row)">
            <ClipboardCheckIcon aria-hidden="true" />
            {{ canReportOrder(row) ? '生产报工' : '暂无工序，不能报工' }}
          </NvDropdownMenuItem>
          <NvDropdownMenuItem @click="openRelatedPage('/mes/capacity', row)">
            <WrenchIcon aria-hidden="true" />
            异常与产能
          </NvDropdownMenuItem>
        </NvRowActions>
      </template>
    </NvDataTable>

    <NvDialog v-model:open="rushSheetOpen">
      <NvDialogContent class="sm:max-w-2xl">
        <NvDialogHeader>
          <NvDialogTitle>创建急单</NvDialogTitle>
          <NvDialogDescription
            >急单用于生产插单和临时补单；提交后系统返回 MES
            规则排程反馈，正式排产输出请进入排产工作台。</NvDialogDescription
          >
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submitRushWorkOrder">
          <p v-if="rushErrorMessage" class="text-sm text-destructive" role="alert">
            {{ rushErrorMessage }}
          </p>
          <p v-if="rushSuccess" class="text-sm text-success" role="status">{{ rushSuccess }}</p>

          <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
            <NvField>
              <NvFieldLabel for="rush-sku"
                >物料 <span class="text-destructive">*</span></NvFieldLabel
              >
              <NvSelect v-if="skuOptions.length" v-model="rushForm.skuId">
                <NvSelectTrigger id="rush-sku"
                  ><NvSelectValue placeholder="选择物料"
                /></NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem
                    v-for="option in skuOptions"
                    :key="option.value"
                    :value="option.value"
                    >{{ option.label }}</NvSelectItem
                  >
                </NvSelectContent>
              </NvSelect>
              <NvInput v-else id="rush-sku" v-model="rushForm.skuId" required />
            </NvField>
            <NvField>
              <NvFieldLabel for="rush-version">生产版本</NvFieldLabel>
              <NvInput id="rush-version" v-model="rushForm.productionVersionId" />
            </NvField>
            <NvField>
              <NvFieldLabel for="rush-quantity"
                >数量 <span class="text-destructive">*</span></NvFieldLabel
              >
              <NvInput
                id="rush-quantity"
                v-model="rushForm.quantity"
                inputmode="decimal"
                required
                type="number"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="rush-due"
                >交期 <span class="text-destructive">*</span></NvFieldLabel
              >
              <NvInput id="rush-due" v-model="rushForm.dueUtc" required type="datetime-local" />
            </NvField>
            <NvField>
              <NvFieldLabel for="rush-work-center"
                >工作中心 <span class="text-destructive">*</span></NvFieldLabel
              >
              <NvSelect v-if="workCenterOptions.length" v-model="rushForm.workCenterId">
                <NvSelectTrigger id="rush-work-center"
                  ><NvSelectValue placeholder="选择工作中心"
                /></NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem
                    v-for="option in workCenterOptions"
                    :key="option.value"
                    :value="option.value"
                    >{{ option.label }}</NvSelectItem
                  >
                </NvSelectContent>
              </NvSelect>
              <NvInput v-else id="rush-work-center" v-model="rushForm.workCenterId" required />
            </NvField>
            <NvField>
              <NvFieldLabel for="rush-operation-task">工序任务</NvFieldLabel>
              <NvInput id="rush-operation-task" v-model="rushForm.operationTaskId" />
            </NvField>
            <NvField>
              <NvFieldLabel for="rush-operation-sequence">工序序号</NvFieldLabel>
              <NvInput
                id="rush-operation-sequence"
                v-model="rushForm.operationSequence"
                inputmode="numeric"
                type="number"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="rush-duration"
                >工时分钟 <span class="text-destructive">*</span></NvFieldLabel
              >
              <NvInput
                id="rush-duration"
                v-model="rushForm.durationMinutes"
                inputmode="numeric"
                required
                type="number"
              />
            </NvField>
          </NvFieldGroup>

          <div class="flex flex-wrap items-center justify-between gap-2 rounded-lg border p-3">
            <p class="text-sm text-muted-foreground">
              正式排产输出、冲突治理和甘特请进入排产工作台。
            </p>
            <NvButton size="sm" type="button" variant="outline" as-child>
              <RouterLink to="/scheduling"
                ><CalendarCogIcon aria-hidden="true" />排产工作台</RouterLink
              >
            </NvButton>
          </div>

          <div
            v-if="lastRushScheduleVersion || lastRushAffectedWorkOrders.length"
            class="grid gap-2 rounded-lg border p-3"
          >
            <div class="flex flex-wrap items-center justify-between gap-2">
              <p class="text-sm font-semibold text-foreground">规则排程反馈</p>
            </div>
            <p v-if="lastRushScheduleVersion" class="text-sm text-muted-foreground">
              规则版本 {{ lastRushScheduleVersion }}；正式排产输出以排产工作台为准。
            </p>
            <p v-if="lastRushAffectedWorkOrders.length" class="text-sm text-muted-foreground">
              受影响工单：{{ lastRushAffectedWorkOrders.join(', ') }}
            </p>
          </div>

          <NvDialogFooter>
            <NvButton type="button" variant="outline" @click="rushSheetOpen = false">取消</NvButton>
            <NvButton type="submit" :disabled="createRushWorkOrderPending || !canCreateRush">
              <Spinner v-if="createRushWorkOrderPending" aria-hidden="true" />
              <FactoryIcon v-else aria-hidden="true" />
              创建急单
            </NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>

    <NvDialog v-model:open="reportSheetOpen">
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>生产报工</NvDialogTitle>
          <NvDialogDescription
            >从工单或工序任务进入报工，系统带出必要字段，一线人员只补充数量和完成状态。</NvDialogDescription
          >
        </NvDialogHeader>
        <form class="grid content-start gap-4" @submit.prevent="submitProductionReport">
          <p v-if="reportErrorMessage" class="text-sm text-destructive" role="alert">
            {{ reportErrorMessage }}
          </p>
          <p v-if="reportSuccess" class="text-sm text-success" role="status">{{ reportSuccess }}</p>

          <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
            <NvField class="sm:col-span-2">
              <NvFieldLabel>报工对象</NvFieldLabel>
              <div class="rounded-lg border bg-muted/30 px-3 py-2 text-sm text-muted-foreground">
                工单与工序来自所选行，只能从工单列表或工序任务带入。
              </div>
            </NvField>
            <NvField>
              <NvFieldLabel for="report-work-order"
                >工单号 <span class="text-destructive">*</span></NvFieldLabel
              >
              <NvInput id="report-work-order" v-model="reportForm.workOrderId" readonly required />
            </NvField>
            <NvField>
              <NvFieldLabel for="report-operation-task"
                >工序任务 <span class="text-destructive">*</span></NvFieldLabel
              >
              <NvInput
                id="report-operation-task"
                v-model="reportForm.operationTaskId"
                readonly
                required
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="report-good"
                >良品数 <span class="text-destructive">*</span></NvFieldLabel
              >
              <NvInput
                id="report-good"
                v-model="reportForm.goodQuantity"
                inputmode="decimal"
                min="0"
                required
                type="number"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="report-scrap"
                >报废数 <span class="text-destructive">*</span></NvFieldLabel
              >
              <NvInput
                id="report-scrap"
                v-model="reportForm.scrapQuantity"
                inputmode="decimal"
                min="0"
                required
                type="number"
              />
              <NvFieldDescription>良品和报废必须为非负数，合计必须大于 0。</NvFieldDescription>
            </NvField>
            <NvField>
              <NvFieldLabel for="report-time"
                >报工时间 <span class="text-destructive">*</span></NvFieldLabel
              >
              <NvInput
                id="report-time"
                v-model="reportForm.reportedAtUtc"
                required
                type="datetime-local"
              />
            </NvField>
            <NvField
              orientation="horizontal"
              class="items-center justify-between rounded-lg border p-3"
            >
              <NvFieldLabel for="report-complete">完成当前工序</NvFieldLabel>
              <NvCheckbox id="report-complete" v-model:checked="reportForm.completesOperation" />
            </NvField>
          </NvFieldGroup>

          <NvDialogFooter>
            <NvButton type="button" variant="outline" @click="reportSheetOpen = false"
              >取消</NvButton
            >
            <NvButton type="submit" :disabled="recordProductionReportPending || !canRecordReport">
              <Spinner v-if="recordProductionReportPending" aria-hidden="true" />
              <ClipboardCheckIcon v-else aria-hidden="true" />
              提交报工
            </NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
