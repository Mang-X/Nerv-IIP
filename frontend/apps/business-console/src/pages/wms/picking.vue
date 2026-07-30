<script setup lang="ts">
import type { BusinessConsoleWmsWarehouseTaskItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import CodeWithNameCell from '@/components/business/CodeWithNameCell.vue'
import WmsInventoryContextPanel from '@/components/wms/WmsInventoryContextPanel.vue'
import WmsOperationalCandidateFilters from '@/components/wms/WmsOperationalCandidateFilters.vue'
import { wmsStatusTone } from '@/data/businessLabels'
import { hasBusinessContext } from '@/composables/businessContextBinding'
import ListScopeMeta from '@/components/business/ListScopeMeta.vue'
import { useWmsOutboundOrders, useWmsPickingTasks } from '@/composables/useBusinessWms'
import { useMasterDataDisplayNames } from '@/composables/useMasterDataDisplayNames'
import { usePagedList } from '@/composables/usePagedList'
import { useWmsOperationalCandidates } from '@/composables/useWmsOperationalCandidates'
import { useSkuNames } from '@/composables/useSkuNames'
import { bindWmsWorkScopeFilters } from '@/composables/useWmsWorkScope'
import {
  useWarehouseCodeCatalog,
  WAREHOUSE_CATALOG_SOURCE_TEXT,
  WAREHOUSE_LOCATION_EMPTY_TEXT,
} from '@/composables/useWarehouseCodeCatalog'
import {
  wmsWarehouseTaskStatusFilterOptions,
  wmsWarehouseTaskStatusLabel,
  WMS_STATUS_ANY,
} from '@/data/wmsReference'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { notifyError, notifySuccess } from '@/utils/notify'
import {
  NvButton,
  NvDataTable,
  NvDialog,
  NvDialogClose,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvField,
  NvFieldError,
  NvFieldGroup,
  NvFieldLabel,
  NvEntityPicker,
  NvInput,
  NvPageHeader,
  NvSearchSelect,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon } from '@lucide/vue'
import { computed, reactive, shallowRef, watch } from 'vue'
import { useRoute } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '拣货任务',
    requiredPermissions: ['business.wms.shipments.read'],
  },
})

const {
  filters,
  pickingTasks,
  pickingTasksError,
  pickingTasksPending,
  pickingTasksTotal,
  refreshPickingTasks,
  createPicking,
  createPickingPending,
  createPickingError,
  pickingTasksLastUpdatedAt,
  pickingTasksHasSuccessfulResponse,
  pickingTasksHasFailedResponse,
} = useWmsPickingTasks({ workScopeRequired: true })
const {
  scopeKey,
  scopeOptions,
  selectedScopeLabel,
  hasSelection: pickingScopeReady,
  pending: workScopePending,
  error: workScopeError,
  refresh: refreshWorkScopes,
} = bindWmsWorkScopeFilters(filters, 'shipments')
const operationalCandidates = useWmsOperationalCandidates('shipment', filters)
const route = useRoute()
const { page, pageSize } = usePagedList(filters, {
  resetOn: [
    () => filters.status,
    () => filters.locationCode,
    () => filters.lotNo,
    () => filters.keyword,
    () => filters.scopeKind,
    () => filters.scopeId,
  ],
})
// 库位后端无主数据读面，从真实的上架/拣货/盘点任务与出库单行里派生可选项。
const { locationOptions, warehouseCatalogPending } = useWarehouseCodeCatalog()
// 出库单是真实读面（只要组织/环境即可列出），拣货任务必须挂在已存在的出库单下。
const {
  filters: outboundOrderFilters,
  outboundOrders,
  outboundOrdersPending,
} = useWmsOutboundOrders({ take: 200, workScopeRequired: true })
watch(
  () => [filters.scopeKind, filters.scopeId] as const,
  ([scopeKind, scopeId]) => {
    outboundOrderFilters.scopeKind = scopeKind
    outboundOrderFilters.scopeId = scopeId
    outboundOrderFilters.skip = 0
  },
  { immediate: true, flush: 'sync' },
)
/**
 * 选择器以**人读单号**为选中值，而不是出库单的内部 id——选择器会把 value 当编码显示出来，
 * 直接绑 id 会把 GUID 露到界面上（UI 不暴露工程语言）。提交时再映射回 id，提交体不变。
 */
const outboundOrderOptions = computed(() =>
  outboundOrders.value.flatMap((order) => {
    const id = order.outboundOrderId?.trim()
    const no = order.outboundOrderNo?.trim() || id
    if (!id || !no) return []
    return [{ value: no, label: no, hint: order.siteCode }]
  }),
)
const outboundOrderIdByNo = computed(() => {
  const map = new Map<string, string>()
  for (const order of outboundOrders.value) {
    const id = order.outboundOrderId?.trim()
    const no = order.outboundOrderNo?.trim() || id
    if (id && no) map.set(no, id)
  }
  return map
})
const outboundOrderNoById = computed(() => {
  const map = new Map<string, string>()
  for (const [no, id] of outboundOrderIdByNo.value) map.set(id, no)
  return map
})
const outboundOrderSelection = computed({
  // 目录还没到位时如实回落显示已有值，不让选择框看起来是空的。
  get: () =>
    outboundOrderNoById.value.get(createForm.outboundOrderId) ?? createForm.outboundOrderId,
  set: (no: string) => {
    createForm.outboundOrderId = outboundOrderIdByNo.value.get(no) ?? no
  },
})
// 状态是后端枚举而不是目录，用哨兵值表达「全部」，避免空字符串和真实码值混淆。
const statusFilter = computed({
  get: () => filters.status || WMS_STATUS_ANY,
  set: (value: string) => {
    filters.status = value === WMS_STATUS_ANY ? undefined : value
  },
})

watch(
  () => route.query,
  (query) => {
    const location = firstQuery(query.locationCode)
    const sku = firstQuery(query.skuCode)

    if (location) filters.locationCode = location
    if (sku) filters.keyword = sku
  },
  { immediate: true },
)

// 拣货任务挂在出库单下（领料齐套 → 出库拣货扣减）。创建需绑定出库单与单行任务。
const createOpen = shallowRef(false)
const createError = shallowRef('')
const createForm = reactive({
  outboundOrderId: '',
  taskNo: '',
  lineNo: '',
  fromLocationCode: '',
  toLocationCode: '',
  quantity: '',
})

function openCreate() {
  createForm.outboundOrderId = ''
  createForm.taskNo = ''
  createForm.lineNo = '1'
  createForm.fromLocationCode = ''
  createForm.toLocationCode = ''
  createForm.quantity = ''
  createError.value = ''
  createOpen.value = true
}
async function submitCreate() {
  if (
    !createForm.outboundOrderId.trim() ||
    !createForm.taskNo.trim() ||
    !createForm.lineNo.trim() ||
    !createForm.fromLocationCode.trim() ||
    !createForm.toLocationCode.trim()
  ) {
    createError.value = '请填写出库单、任务号、行号与起讫库位。'
    return
  }
  if (createForm.quantity !== '' && !(Number(createForm.quantity) > 0)) {
    createError.value = '拣货数量需为正数。'
    return
  }
  try {
    await createPicking(createForm.outboundOrderId.trim(), {
      taskNo: createForm.taskNo.trim(),
      lineNo: createForm.lineNo.trim(),
      fromLocationCode: createForm.fromLocationCode.trim(),
      toLocationCode: createForm.toLocationCode.trim(),
      quantity: createForm.quantity === '' ? undefined : Number(createForm.quantity),
    })
    createOpen.value = false
    notifySuccess('拣货任务已创建')
  } catch (error) {
    notifyError(error, '创建拣货任务失败，请稍后重试。')
  }
}

/**
 * 读错误只归列表区域。创建拣货任务的失败一律走 toast，不并进这一条：
 * 两者共用一个变量时，「创建失败」会伪装成「列表加载失败」。
 */
const listErrorMessage = computed(() =>
  pickingTasksError.value
    ? `取不到拣货任务列表，当前拣货进度无法判断：${formatError(pickingTasksError.value)}`
    : '',
)
/**
 * 页头计数一律用服务端总数；上下文未就绪 / 读取中 / 读失败时显文字而不是 0——
 * 骨架还在转就断言「0 个拣货任务」，等于把加载中说成没有任务。
 */
// 业务范围是否选定走全站唯一判定，不在页面里另写一份——判定分叉了，
// 「还没查」和「真的 0 条」很快又会混回同一个渲染。
const contextReady = computed(() => hasBusinessContext(filters) && pickingScopeReady.value)
const headerCount = computed(() => {
  if (!contextReady.value) return '未选择业务范围'
  if (pickingTasksError.value) return '任务数取不到'
  if (pickingTasksPending.value) return '加载中'
  return `${pickingTasksTotal.value} 个拣货任务`
})

// 任务读面只回编码（SKU-… / WH-…），名称在主数据里，按编码 join 出中文名。
const { resolveSkuName } = useSkuNames()
const { resolveLocation } = useMasterDataDisplayNames({ locations: true })

/** 库位展示串：优先中文名，名录查不到就只显编码。 */
function locationLabel(code?: string | null) {
  if (!code) return '—'
  return resolveLocation(code) ?? code
}
interface TaskLocations {
  fromLocationCode?: string | null
  toLocationCode?: string | null
}
function hasLocationName(row: TaskLocations) {
  return Boolean(resolveLocation(row.fromLocationCode) || resolveLocation(row.toLocationCode))
}
/** 「名称 编码」串，供排序与导出用；名录查不到就只有编码，不编名字。 */
function skuText(code?: string | null, fallback = '—') {
  const name = resolveSkuName(code)
  return name ? `${name} ${code}` : (code ?? fallback)
}
function statusLabel(value?: string | null) {
  return wmsWarehouseTaskStatusLabel(value)
}

type PickingRow = BusinessConsoleWmsWarehouseTaskItem
const columns: NvDataTableColumn<PickingRow>[] = [
  {
    key: 'taskNo',
    header: '任务号',
    cellClass: 'font-medium',
    // warehouseTaskId 是系统 GUID，不上屏；没有人读任务号就如实留空。
    accessor: (r) => r.taskNo ?? '无任务号',
  },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'sourceOrderNo', header: '来源单据', accessor: (r) => r.sourceOrderNo ?? '—' },
  {
    key: 'skuCode',
    header: '物料',
    accessor: (r) => skuText(r.skuCode),
  },
  { key: 'inventoryContext', header: '库存上下文', width: 'w-72' },
  {
    key: 'location',
    header: '起讫库位',
    accessor: (r) => `${locationLabel(r.fromLocationCode)} → ${locationLabel(r.toLocationCode)}`,
  },
  {
    key: 'quantity',
    header: '数量',
    align: 'end',
    accessor: (r) => formatQuantity(r.executedQuantity ?? r.plannedQuantity),
  },
  { key: 'createdAtUtc', header: '创建时间', accessor: (r) => formatDateTime(r.createdAtUtc) },
]

function rowKey(row: PickingRow) {
  return row.warehouseTaskId ?? row.taskNo ?? '拣货任务'
}
function formatQuantity(value?: number | null) {
  return new Intl.NumberFormat(undefined, { maximumFractionDigits: 3 }).format(value ?? 0)
}
function formatDateTime(value?: string | null) {
  if (!value) return '—'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}

function refreshAll() {
  void refreshWorkScopes()
  void refreshPickingTasks()
}
function firstQuery(value: unknown) {
  if (Array.isArray(value)) return typeof value[0] === 'string' ? value[0] : ''
  return typeof value === 'string' ? value : ''
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader title="拣货任务" :breadcrumbs="[{ label: '仓储作业' }]" :count="headerCount">
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="pickingTasksPending"
          @click="refreshAll"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
        <NvButton size="sm" type="button" @click="openCreate">
          <PlusIcon aria-hidden="true" />
          新建拣货任务
        </NvButton>
      </template>
    </NvPageHeader>

    <ListScopeMeta
      :scope="selectedScopeLabel || 'WMS 作业范围未就绪'"
      source="WMS 发货作业范围目录"
      :loaded="pickingTasks.length"
      :total="pickingTasksTotal"
      :updated-at="pickingTasksLastUpdatedAt"
      :empty="pickingTasksHasSuccessfulResponse && !pickingTasksError && pickingTasks.length === 0"
      :failed="
        pickingTasksHasFailedResponse || Boolean(pickingTasksError) || Boolean(workScopeError)
      "
      failure-explanation="WMS 发货作业范围或拣货任务未成功返回，请重试。"
      :empty-explanation="
        pickingScopeReady ? '当前作业范围没有拣货任务。' : '作业范围目录未就绪，未发起查询。'
      "
    />

    <NvToolbar :show-search="false">
      <template #filters>
        <NvSearchSelect
          v-model="scopeKey"
          class="w-56"
          :options="scopeOptions"
          :loading="workScopePending"
          placeholder="选择作业范围"
          aria-label="作业范围"
        />
        <NvInput
          v-model="filters.keyword"
          class="h-9 w-40"
          placeholder="任务号/物料"
          aria-label="关键字"
        />
        <WmsOperationalCandidateFilters
          v-model:location-code="filters.locationCode"
          v-model:lot-no="filters.lotNo"
          :location-options="operationalCandidates.locationOptions.value"
          :lot-options="operationalCandidates.lotOptions.value"
          :pending="operationalCandidates.pending.value"
          :source-label="operationalCandidates.sourceLabel.value"
          :source-kind="operationalCandidates.sourceKind.value"
          :as-of-utc="operationalCandidates.asOfUtc.value"
          :freshness-utc="operationalCandidates.freshnessUtc.value"
          :truncated="operationalCandidates.truncated.value"
        />
        <NvSearchSelect
          v-model="statusFilter"
          class="w-32"
          :options="wmsWarehouseTaskStatusFilterOptions"
          placeholder="全部状态"
          aria-label="拣货任务状态"
        />
      </template>
    </NvToolbar>

    <!-- 读失败 / 未选组织环境都由表格自己的三态呈现，绝不退化成「暂无拣货任务」。 -->
    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="pickingTasksTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :columns="columns"
      :rows="pickingTasks"
      :row-key="rowKey"
      :loading="pickingTasksPending"
      :error="pickingTasksError"
      :error-message="listErrorMessage"
      :awaiting-scope="!contextReady"
      awaiting-scope-message="请先在顶部选择业务范围，再查看拣货任务。"
      :searchable="false"
      :column-settings="false"
      empty-message="暂无拣货任务。领料齐套或出库拣货时由系统派生，或在此手工登记。"
      @retry="refreshPickingTasks"
    >
      <template #cell-status="{ row }"
        ><NvStatusBadge
          :value="row.status"
          :label="statusLabel(row.status)"
          :tone="wmsStatusTone(row.status)"
      /></template>
      <template #cell-skuCode="{ row }">
        <CodeWithNameCell :code="row.skuCode" :name="resolveSkuName(row.skuCode)" fallback="—" />
      </template>
      <template #cell-location="{ row }">
        <span class="grid leading-tight">
          <span>
            {{ locationLabel(row.fromLocationCode) }} → {{ locationLabel(row.toLocationCode) }}
          </span>
          <!-- 名称解析出来了才补编码副行，否则两行都是编码，纯属噪音。 -->
          <span v-if="hasLocationName(row)" class="text-xs text-muted-foreground"
            >{{ row.fromLocationCode ?? '—' }} → {{ row.toLocationCode ?? '—' }}</span
          >
        </span>
      </template>
      <template #cell-inventoryContext="{ row }">
        <WmsInventoryContextPanel
          compact
          :sku-code="row.skuCode"
          :uom-code="row.uomCode"
          :site-code="row.siteCode"
          :location-code="row.fromLocationCode"
          gap-message="本页暂不显示逐行可用量与批次、序列号、冻结预留明细，请到库存可用量或批次与预留页查看。"
        />
      </template>
    </NvDataTable>

    <NvDialog v-model:open="createOpen">
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>新建拣货任务</NvDialogTitle>
          <!-- 界面上不再写说明书；仅供读屏播报对象范围。 -->
          <NvDialogDescription class="sr-only">出库单下的单行拣货任务。</NvDialogDescription>
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submitCreate">
          <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
            <NvField class="sm:col-span-2">
              <NvFieldLabel for="wms-picking-outbound">出库单</NvFieldLabel>
              <NvEntityPicker
                id="wms-picking-outbound"
                v-model="outboundOrderSelection"
                :options="outboundOrderOptions"
                title="选择出库单"
                placeholder="选择出库单"
                source-text="数据来自仓储出库单"
                empty-text="暂无出库单，请先创建出库单"
                :loading="outboundOrdersPending"
                clearable
                aria-label="出库单"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="wms-picking-no">任务号</NvFieldLabel>
              <NvInput id="wms-picking-no" v-model="createForm.taskNo" autocomplete="off" />
            </NvField>
            <NvField>
              <NvFieldLabel for="wms-picking-line">行号</NvFieldLabel>
              <NvInput id="wms-picking-line" v-model="createForm.lineNo" autocomplete="off" />
            </NvField>
            <NvField>
              <NvFieldLabel for="wms-picking-from">拣货库位</NvFieldLabel>
              <NvEntityPicker
                id="wms-picking-from"
                v-model="createForm.fromLocationCode"
                :options="locationOptions"
                title="选择货架库位"
                placeholder="货架库位"
                :source-text="WAREHOUSE_CATALOG_SOURCE_TEXT"
                :empty-text="WAREHOUSE_LOCATION_EMPTY_TEXT"
                :loading="warehouseCatalogPending"
                clearable
                aria-label="拣货库位"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="wms-picking-to">目标库位</NvFieldLabel>
              <NvEntityPicker
                id="wms-picking-to"
                v-model="createForm.toLocationCode"
                :options="locationOptions"
                title="选择集货库位"
                placeholder="集货/暂存库位"
                :source-text="WAREHOUSE_CATALOG_SOURCE_TEXT"
                :empty-text="WAREHOUSE_LOCATION_EMPTY_TEXT"
                :loading="warehouseCatalogPending"
                clearable
                aria-label="目标库位"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="wms-picking-qty">拣货数量（可选）</NvFieldLabel>
              <NvInput
                id="wms-picking-qty"
                v-model="createForm.quantity"
                type="number"
                min="0"
                step="any"
                autocomplete="off"
              />
            </NvField>
          </NvFieldGroup>

          <NvFieldError v-if="createError" :errors="[createError]" />

          <NvDialogFooter>
            <NvDialogClose as-child>
              <NvButton type="button" variant="outline">取消</NvButton>
            </NvDialogClose>
            <NvButton type="submit" :disabled="createPickingPending">创建拣货任务</NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
