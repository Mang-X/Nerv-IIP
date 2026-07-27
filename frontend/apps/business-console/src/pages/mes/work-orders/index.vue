<script setup lang="ts">
import type {
  BusinessConsoleCreateRushWorkOrderRequest,
  BusinessConsoleMesWorkOrderItem,
  BusinessConsoleResourceItem,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn, NvDataTableSort } from '@nerv-iip/ui'
import { mesWorkOrderStatusOptions } from '@/composables/mes/useMesReferenceLabels'
import { useMesDisplayNames } from '@/composables/mes/useMesDisplayNames'
import {
  useBusinessMasterDataResources,
  useBusinessSkus,
} from '@/composables/useBusinessMasterData'
import { useMesOperationTasks, useMesWorkOrders } from '@/composables/useBusinessMes'
import { useMesMaterialVersionCatalog } from '@/composables/useMesPickerCatalog'
import { useOrderUrgencies } from '@/composables/useOrderUrgency'
import {
  DEFAULT_URGENCY_DISPLAY_MODE,
  orderRowsByUrgency,
  type UrgencyDisplayMode,
} from '@/composables/useUrgencyDisplayMode'
import ProductionReportDialog from '@/components/mes/ProductionReportDialog.vue'
import WorkOrderDetailSheet from '@/components/mes/WorkOrderDetailSheet.vue'
import type { ProductionReportContext } from '@/composables/mes/useProductionReportForm'
import OrderUrgencyBadge from '@/components/urgency/OrderUrgencyBadge.vue'
import UrgencyDisplayModeSelect from '@/components/urgency/UrgencyDisplayModeSelect.vue'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { notifyError, notifySuccess } from '@/utils/notify'
import {
  NvButton,
  NvDataTable,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvDropdownMenuItem,
  NvDropdownMenuSeparator,
  NvEntityPicker,
  NvField,
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
  ExternalLinkIcon,
  EyeIcon,
  FactoryIcon,
  LockIcon,
  RefreshCwIcon,
} from '@lucide/vue'
import { computed, reactive, ref, shallowRef, watch } from 'vue'
import { useRouter } from 'vue-router'

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
  refreshWorkOrders,
  workOrders,
  workOrdersError,
  workOrdersPending,
  workOrdersTotal,
} = useMesWorkOrders()
const orderUrgencies = useOrderUrgencies(
  computed(() => workOrders.value.map((order) => order.workOrderId)),
)
const displayMode = shallowRef<UrgencyDisplayMode>(DEFAULT_URGENCY_DISPLAY_MODE)
function refreshUrgency() {
  void orderUrgencies.refresh()
  refreshWorkOrders()
}

const router = useRouter()
const { skus } = useBusinessSkus()
const { resolveSku, resolveWorkCenter } = useMesDisplayNames()
const { resources: workCenterResources } = useBusinessMasterDataResources('work-center')

const rushSheetOpen = shallowRef(false)
const reportSheetOpen = shallowRef(false)
const sheetWorkOrderId = ref<string | null>(null)

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

// 报工对象由所选工单行带出（工单 + 该工单的首道可报工序），弹窗不提供任何挑选入口。
const reportContext = shallowRef<ProductionReportContext | null>(null)

const listErrorMessage = computed(() => formatError(workOrdersError.value))

const workCenterOptions = computed(() => toResourceOptions(workCenterResources.value))
const skuOptions = computed(() => toResourceOptions(skus.value))

// ── 急单表单的四个选择器 ────────────────────────────────────────
// 物料 ▸ 生产版本 从属，工作中心 ▸ 工序任务 从属：上游变了清空下游。
const { productionVersionOptions, productionVersionsPending } = useMesMaterialVersionCatalog()
const { operationTasks, operationTasksPending } = useMesOperationTasks()

const rushVersionOptions = computed(() => productionVersionOptions(rushForm.skuId))
const rushOperationTaskOptions = computed(() => {
  const workCenter = rushForm.workCenterId.trim()
  return operationTasks.value
    .filter((task) => !!task.operationTaskId)
    .filter((task) => !workCenter || task.workCenterCode === workCenter)
    .map((task) => ({
      value: task.operationTaskId as string,
      label: task.operationTaskNo || task.operationCode || '未编号工序任务',
      hint: [task.workOrderNo, task.workCenterName ?? task.workCenterCode]
        .filter(Boolean)
        .join(' · '),
    }))
})

watch(
  () => rushForm.skuId,
  () => {
    rushForm.productionVersionId = ''
  },
)
watch(
  () => rushForm.workCenterId,
  () => {
    rushForm.operationTaskId = ''
  },
)

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

// --- Sort (page-owned, before pagination) ---
// 默认无列排序：按统一紧急度（等级→CR→预计延迟→due→等待）排序；用户点列头后按列排序。
// 后端分页，紧急度排序仅对当前页行生效；跨页排序需后端支持（已知契约限制）。
const sort = ref<NvDataTableSort | null>(null)
function sortValue(order: Row, key: string): string | number {
  if (key === 'quantity') return order.quantity ?? 0
  if (key === 'dueUtc') return order.dueUtc ? new Date(order.dueUtc).getTime() : 0
  if (key === 'operationCount') return order.operationTasks?.length ?? 0
  return (order[key as keyof Row] as string | null) ?? ''
}
const sortedWorkOrders = computed(() => {
  if (!sort.value) {
    return orderRowsByUrgency(
      visibleWorkOrders.value,
      (order) => order.workOrderId,
      orderUrgencies.byReference.value,
    )
  }
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

// 报工不再靠 URL query 跨页唤起：工序执行页与报工记录页都在本地行上直接打开同一个报工弹窗，
// 上下文随行带出，避免跳到本页后只剩两个 ID、工作中心/物料/计划数量全丢。

const columns: NvDataTableColumn<Row>[] = [
  { key: 'workOrderId', header: '工单', sortable: true, cellClass: 'font-medium' },
  { key: 'status', header: '状态', sortable: true, width: 'w-24' },
  { key: 'urgency', header: '紧急度', width: 'w-28' },
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

// 一个工单可能有多道工序：优先带出「还没做完」的第一道（executing/ready/queued 之类），
// 都做完了才退回第一道，避免默认把报工记到已完成工序上。
function reportableTask(order: Row) {
  const tasks = (order.operationTasks ?? []).filter((task) => task.operationTaskId)
  const done = new Set(['completed', 'closed', 'cancelled'])
  return tasks.find((task) => !done.has((task.status ?? '').toLowerCase())) ?? tasks[0]
}
function openReport(order: Row) {
  const task = reportableTask(order)
  if (!order.workOrderId || !task?.operationTaskId) return
  reportContext.value = {
    workOrderId: order.workOrderId,
    workOrderNo: order.workOrderNo,
    operationTaskId: task.operationTaskId,
    operationTaskNo: task.operationTaskNo,
    operationSequence: task.operationSequence,
    workCenterLabel:
      task.workCenterName ?? resolveWorkCenter(task.workCenterCode ?? task.workCenterId),
    skuLabel: resolveSku(order.skuCode ?? order.skuId),
    plannedQuantity: order.quantity,
  }
  reportSheetOpen.value = true
}
function canReportOrder(order: Row) {
  return Boolean(order.workOrderId && reportableTask(order)?.operationTaskId)
}
function openOrderDetail(order: Row) {
  if (!order.workOrderId) return
  void router.push({ path: `/mes/work-orders/${encodeURIComponent(order.workOrderId)}` })
}
// 行内抽屉：工单详情 / 工序 / 齐套 / 派工 / 状态流转一次看全，不跳页。
function openOrderSheet(order: Row) {
  if (!order.workOrderId) return
  sheetWorkOrderId.value = order.workOrderId
}
// 抽屉里点「报工」把工序 id 抛回来，由本页共用的报工弹窗承接（抽屉不自己再开一个）。
function openReportFromSheet(operationTaskId: string) {
  const order = workOrders.value.find((item) => item.workOrderId === sheetWorkOrderId.value)
  const task = (order?.operationTasks ?? []).find((t) => t.operationTaskId === operationTaskId)
  if (!order?.workOrderId || !task?.operationTaskId) return
  reportContext.value = {
    workOrderId: order.workOrderId,
    workOrderNo: order.workOrderNo,
    operationTaskId: task.operationTaskId,
    operationTaskNo: task.operationTaskNo,
    operationSequence: task.operationSequence,
    workCenterLabel:
      task.workCenterName ?? resolveWorkCenter(task.workCenterCode ?? task.workCenterId),
    skuLabel: resolveSku(order.skuCode ?? order.skuId),
    plannedQuantity: order.quantity,
  }
  sheetWorkOrderId.value = null
  reportSheetOpen.value = true
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
  try {
    const response = await createRushWorkOrder(body)
    const affected = response?.data?.affectedWorkOrderIds ?? []
    // 排程反馈是结果、不是常驻说明：随成功 toast 一次说清，弹窗即关，不在表单里堆一块「反馈区」。
    notifySuccess(
      `已创建急单 ${response?.data?.workOrderId ?? ''}` +
        (affected.length ? ` · 重排影响 ${affected.length} 个在制工单` : ''),
    )
    rushForm.idempotencyKey = newMesIdempotencyKey('rush-work-order')
    rushSheetOpen.value = false
  } catch (error) {
    notifyError(createRushWorkOrderError.value ?? error, '创建急单失败，请稍后重试。')
  }
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
        <UrgencyDisplayModeSelect v-model="displayMode" />
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
      <template #cell-status="{ row }">
        <div class="flex items-center gap-1.5">
          <NvStatusBadge :value="row.status" />
          <!-- 质量保留锁定标记：与工单生命周期状态无关，来源为活跃 quality hold（#886）。 -->
          <LockIcon
            v-if="row.hasActiveQualityHold"
            class="size-3.5 text-destructive"
            aria-label="存在有效质量保留"
            title="存在有效质量保留，需处理后才能放行或开工"
          />
        </div>
      </template>
      <template #cell-urgency="{ row }">
        <OrderUrgencyBadge
          :order-reference="row.workOrderId ?? ''"
          :mode="displayMode"
          :urgency="
            row.workOrderId ? orderUrgencies.byReference.value.get(row.workOrderId) : undefined
          "
          @refresh="refreshUrgency"
        />
      </template>
      <template #cell-quantity="{ row }"
        ><span class="tabular-nums">{{ formatQuantity(row.quantity) }}</span></template
      >
      <template #cell-dueUtc="{ row }">{{ formatDateTime(row.dueUtc) }}</template>
      <template #cell-operationCount="{ row }">
        <div class="grid gap-1">
          <!-- 一行四段斜杠拼接读不出主次：第一行给「第几道 · 在哪做」，第二行给单号与状态。 -->
          <div
            v-for="task in row.operationTasks ?? []"
            :key="task.operationTaskId ?? `${row.workOrderId}-${task.operationSequence}`"
            class="grid gap-0.5"
          >
            <span class="text-xs font-medium text-foreground">
              第 {{ task.operationSequence ?? '—' }} 道 ·
              {{
                task.workCenterName ??
                resolveWorkCenter(task.workCenterCode ?? task.workCenterId) ??
                '未指定工作中心'
              }}
            </span>
            <span class="text-xs text-muted-foreground">
              {{ task.operationTaskNo ?? task.operationTaskId ?? '未生成任务' }} ·
              {{ formatStatus(task.status) }}
            </span>
          </div>
          <span v-if="!row.operationTasks?.length" class="text-xs text-muted-foreground"
            >暂无工序任务</span
          >
        </div>
      </template>
      <template #cell-actions="{ row }">
        <NvRowActions :label="`工单操作 ${row.workOrderId ?? ''}`">
          <!-- 详情 / 工序 / 齐套 / 派工 / 状态流转全部收进行内抽屉，不再逐个跳页丢上下文。 -->
          <NvDropdownMenuItem :disabled="!row.workOrderId" @click="openOrderSheet(row)">
            <EyeIcon aria-hidden="true" />
            工单详情与工序
          </NvDropdownMenuItem>
          <NvDropdownMenuItem :disabled="!canReportOrder(row)" @click="openReport(row)">
            <ClipboardCheckIcon aria-hidden="true" />
            {{ canReportOrder(row) ? '生产报工' : '暂无工序，不能报工' }}
          </NvDropdownMenuItem>
          <NvDropdownMenuSeparator />
          <NvDropdownMenuItem :disabled="!row.workOrderId" @click="openOrderDetail(row)">
            <ExternalLinkIcon aria-hidden="true" />
            打开完整详情页
          </NvDropdownMenuItem>
        </NvRowActions>
      </template>
    </NvDataTable>

    <NvDialog v-model:open="rushSheetOpen">
      <NvDialogContent class="sm:max-w-2xl">
        <NvDialogHeader>
          <NvDialogTitle>创建急单</NvDialogTitle>
          <NvDialogDescription class="sr-only"
            >临时插单，填写物料、数量、交期与工作中心后提交。</NvDialogDescription
          >
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submitRushWorkOrder">
          <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
            <NvField>
              <NvFieldLabel for="rush-sku"
                >物料 <span class="text-destructive">*</span></NvFieldLabel
              >
              <NvEntityPicker
                id="rush-sku"
                v-model="rushForm.skuId"
                :options="skuOptions"
                title="选择物料"
                placeholder="选择物料"
                source-text="数据来自基础数据物料主数据"
                empty-text="暂无物料，请先在基础数据维护"
                aria-label="物料"
                clearable
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="rush-version">生产版本</NvFieldLabel>
              <NvEntityPicker
                id="rush-version"
                v-model="rushForm.productionVersionId"
                :options="rushVersionOptions"
                title="选择生产版本"
                :placeholder="rushForm.skuId ? '可留空，按生效日自动解析' : '先选物料'"
                :disabled="!rushForm.skuId"
                source-text="仅列所选物料的生产版本"
                empty-text="该物料暂无生产版本，请先在工程数据维护"
                :loading="productionVersionsPending"
                aria-label="生产版本"
                clearable
              />
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
              <NvEntityPicker
                id="rush-work-center"
                v-model="rushForm.workCenterId"
                :options="workCenterOptions"
                title="选择工作中心"
                placeholder="选择工作中心"
                source-text="数据来自基础数据工作中心主数据"
                empty-text="暂无工作中心，请先在基础数据维护"
                aria-label="工作中心"
                clearable
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="rush-operation-task">工序任务</NvFieldLabel>
              <NvEntityPicker
                id="rush-operation-task"
                v-model="rushForm.operationTaskId"
                :options="rushOperationTaskOptions"
                title="选择工序任务"
                :placeholder="rushForm.workCenterId ? '可留空' : '先选工作中心'"
                :disabled="!rushForm.workCenterId"
                source-text="仅列所选工作中心的在办工序任务"
                empty-text="该工作中心暂无在办工序任务"
                :loading="operationTasksPending"
                aria-label="工序任务"
                clearable
              />
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

          <NvDialogFooter class="sm:justify-between">
            <NvButton size="sm" type="button" variant="ghost" as-child>
              <RouterLink to="/scheduling"
                ><CalendarCogIcon aria-hidden="true" />排产工作台</RouterLink
              >
            </NvButton>
            <div class="flex gap-2">
              <NvButton type="button" variant="outline" @click="rushSheetOpen = false"
                >取消</NvButton
              >
              <NvButton type="submit" :disabled="createRushWorkOrderPending || !canCreateRush">
                <Spinner v-if="createRushWorkOrderPending" aria-hidden="true" />
                <FactoryIcon v-else aria-hidden="true" />
                创建急单
              </NvButton>
            </div>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>

    <!-- 报工「带出式录入」样板：上下文只能由所选工单行带出，弹窗自身不提供工单/工序挑选入口。 -->
    <ProductionReportDialog
      v-model:open="reportSheetOpen"
      :context="reportContext"
      @reported="refreshWorkOrders"
    />

    <WorkOrderDetailSheet v-model:work-order-id="sheetWorkOrderId" @report="openReportFromSheet" />
  </BusinessLayout>
</template>
