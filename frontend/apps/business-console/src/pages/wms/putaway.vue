<script setup lang="ts">
import type { BusinessConsoleWmsWarehouseTaskItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import CarriedContextSummary from '@/components/business/CarriedContextSummary.vue'
import CodeWithNameCell from '@/components/business/CodeWithNameCell.vue'
import WmsInventoryContextPanel from '@/components/wms/WmsInventoryContextPanel.vue'
import { useWmsInboundOrders, useWmsPutawayTasks } from '@/composables/useBusinessWms'
import { useMasterDataDisplayNames } from '@/composables/useMasterDataDisplayNames'
import { usePagedList } from '@/composables/usePagedList'
import { useSkuNames } from '@/composables/useSkuNames'
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
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { useAuthStore } from '@/stores/auth'
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
    title: '上架任务',
    requiredPermissions: ['business.wms.receipts.read'],
  },
})

const route = useRoute()
const auth = useAuthStore()
const {
  filters,
  putawayTasks,
  putawayTasksError,
  putawayTasksPending,
  putawayTasksTotal,
  refreshPutawayTasks,
  createPutaway,
  createPutawayPending,
  createPutawayError,
} = useWmsPutawayTasks()
const permissionCodes = computed(() => auth.principal?.permissionCodes ?? [])
const canManageReceipts = computed(() => permissionCodes.value.includes(P.wmsReceiptsManage))
const inboundOrderNo = computed(() => firstQuery(route.query.inboundOrderNo))
const inboundOrderId = computed(() => firstQuery(route.query.inboundOrderId))
watch(
  inboundOrderNo,
  (value) => {
    filters.keyword = value || undefined
  },
  { immediate: true },
)
const { page, pageSize } = usePagedList(filters, {
  resetOn: [() => filters.status, () => filters.locationCode, () => filters.keyword],
})
// 库位后端无主数据读面，从真实的上架/拣货/盘点任务与出库单行里派生可选项。
const { locationOptions, warehouseCatalogPending } = useWarehouseCodeCatalog()
// 入库单是真实读面（只要组织/环境即可列出），上架任务必须挂在已存在的入库单下。
const { inboundOrders, inboundOrdersPending } = useWmsInboundOrders({ take: 200 })
/**
 * 选择器以**人读单号**为选中值，而不是入库单的内部 id——选择器会把 value 当编码显示出来，
 * 直接绑 id 会把 GUID 露到界面上（UI 不暴露工程语言）。提交时再映射回 id，提交体不变。
 */
const inboundOrderOptions = computed(() =>
  inboundOrders.value.flatMap((order) => {
    const id = order.inboundOrderId?.trim()
    const no = order.inboundOrderNo?.trim() || id
    if (!id || !no) return []
    return [{ value: no, label: no, hint: order.status }]
  }),
)
const inboundOrderIdByNo = computed(() => {
  const map = new Map<string, string>()
  for (const order of inboundOrders.value) {
    const id = order.inboundOrderId?.trim()
    const no = order.inboundOrderNo?.trim() || id
    if (id && no) map.set(no, id)
  }
  return map
})
const inboundOrderNoById = computed(() => {
  const map = new Map<string, string>()
  for (const [no, id] of inboundOrderIdByNo.value) map.set(id, no)
  return map
})
const inboundOrderSelection = computed({
  // 目录还没到位时如实回落显示已有值，不让选择框看起来是空的。
  get: () => inboundOrderNoById.value.get(createForm.inboundOrderId) ?? createForm.inboundOrderId,
  set: (no: string) => {
    createForm.inboundOrderId = inboundOrderIdByNo.value.get(no) ?? no
  },
})
// 状态是后端枚举而不是目录，用哨兵值表达「全部」，避免空字符串和真实码值混淆。
const statusFilter = computed({
  get: () => filters.status || WMS_STATUS_ANY,
  set: (value: string) => {
    filters.status = value === WMS_STATUS_ANY ? undefined : value
  },
})

// 上架任务挂在收货入库单下（完工入库 → 上架增量）。创建需绑定入库单与单行任务。
const createOpen = shallowRef(false)
const createError = shallowRef('')
const createForm = reactive({
  inboundOrderId: '',
  taskNo: '',
  lineNo: '',
  fromLocationCode: '',
  toLocationCode: '',
  quantity: '',
})

function openCreate() {
  if (!canManageReceipts.value) return

  createForm.inboundOrderId = inboundOrderId.value
  createForm.taskNo = ''
  createForm.lineNo = '1'
  createForm.fromLocationCode = ''
  createForm.toLocationCode = ''
  createForm.quantity = ''
  createError.value = ''
  createOpen.value = true
}
watch(
  () => [inboundOrderId.value, firstQuery(route.query.create)] as const,
  ([id, create]) => {
    if (canManageReceipts.value && id && create === '1') openCreate()
  },
  { immediate: true },
)
async function submitCreate() {
  if (!canManageReceipts.value) {
    createError.value = '缺少收货管理权限，无法创建上架任务。'
    return
  }

  if (
    !createForm.inboundOrderId.trim() ||
    !createForm.taskNo.trim() ||
    !createForm.lineNo.trim() ||
    !createForm.fromLocationCode.trim() ||
    !createForm.toLocationCode.trim()
  ) {
    createError.value = '请填写入库单、任务号、行号与起讫库位。'
    return
  }
  if (!(Number(createForm.quantity) > 0)) {
    createError.value = '上架数量需为正数。'
    return
  }
  try {
    await createPutaway(createForm.inboundOrderId.trim(), {
      taskNo: createForm.taskNo.trim(),
      lineNo: createForm.lineNo.trim(),
      fromLocationCode: createForm.fromLocationCode.trim(),
      toLocationCode: createForm.toLocationCode.trim(),
      quantity: Number(createForm.quantity),
    })
    createOpen.value = false
    notifySuccess('上架任务已创建')
  } catch (error) {
    notifyError(error, '创建上架任务失败，请稍后重试。')
  }
}

// 从收货入库行带出的上下文：有入库单就只读展示（人读单号优先），没有才让用户自己填。
const carriedFromInbound = computed(() => Boolean(inboundOrderId.value))
const carriedContextItems = computed(() => [
  { label: '入库单', value: inboundOrderNo.value || inboundOrderId.value },
])

const errorMessage = computed(() =>
  formatError(putawayTasksError.value ?? createPutawayError.value),
)

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

type PutawayRow = BusinessConsoleWmsWarehouseTaskItem
const columns: NvDataTableColumn<PutawayRow>[] = [
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

function rowKey(row: PutawayRow) {
  return row.warehouseTaskId ?? row.taskNo ?? '上架任务'
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
function firstQuery(value: unknown) {
  return Array.isArray(value) ? String(value[0] ?? '') : typeof value === 'string' ? value : ''
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="上架任务"
      :breadcrumbs="[{ label: '仓储作业' }]"
      :count="`${putawayTasksTotal} 个上架任务`"
    >
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="putawayTasksPending"
          @click="refreshPutawayTasks"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
        <NvButton v-if="canManageReceipts" size="sm" type="button" @click="openCreate">
          <PlusIcon aria-hidden="true" />
          新建上架任务
        </NvButton>
      </template>
    </NvPageHeader>

    <NvToolbar :show-search="false">
      <template #filters>
        <NvInput
          v-model="filters.keyword"
          class="h-9 w-40"
          placeholder="任务号/来源单/物料"
          aria-label="关键字"
        />
        <NvEntityPicker
          v-model="filters.locationCode"
          class="w-36"
          :options="locationOptions"
          title="选择库位"
          placeholder="库位"
          :source-text="WAREHOUSE_CATALOG_SOURCE_TEXT"
          :empty-text="WAREHOUSE_LOCATION_EMPTY_TEXT"
          :loading="warehouseCatalogPending"
          clearable
          aria-label="库位"
        />
        <NvSearchSelect
          v-model="statusFilter"
          class="w-32"
          :options="wmsWarehouseTaskStatusFilterOptions"
          placeholder="全部状态"
          aria-label="上架任务状态"
        />
      </template>
    </NvToolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="putawayTasksTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :columns="columns"
      :rows="putawayTasks"
      :row-key="rowKey"
      :loading="putawayTasksPending"
      :searchable="false"
      :column-settings="false"
      empty-message="暂无上架任务。完工入库后由系统派生，或在此手工登记。"
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

    <NvDialog v-if="canManageReceipts" v-model:open="createOpen">
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>新建上架任务</NvDialogTitle>
          <!-- 上架来源已在下方只读区呈现；此处仅供读屏播报。 -->
          <NvDialogDescription class="sr-only">
            入库单 {{ inboundOrderNo || inboundOrderId || '未指定' }} 的上架任务。
          </NvDialogDescription>
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submitCreate">
          <CarriedContextSummary
            v-if="carriedFromInbound"
            label="上架来源"
            :items="carriedContextItems"
          />
          <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
            <NvField v-if="!carriedFromInbound" class="sm:col-span-2">
              <NvFieldLabel for="wms-putaway-inbound">入库单</NvFieldLabel>
              <NvEntityPicker
                id="wms-putaway-inbound"
                v-model="inboundOrderSelection"
                :options="inboundOrderOptions"
                title="选择入库单"
                placeholder="选择入库单"
                source-text="数据来自仓储入库单"
                empty-text="暂无入库单，请先登记收货入库"
                :loading="inboundOrdersPending"
                clearable
                aria-label="入库单"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="wms-putaway-no">任务号</NvFieldLabel>
              <NvInput id="wms-putaway-no" v-model="createForm.taskNo" autocomplete="off" />
            </NvField>
            <NvField>
              <NvFieldLabel for="wms-putaway-line">行号</NvFieldLabel>
              <NvInput id="wms-putaway-line" v-model="createForm.lineNo" autocomplete="off" />
            </NvField>
            <NvField>
              <NvFieldLabel for="wms-putaway-from">来源库位</NvFieldLabel>
              <NvEntityPicker
                id="wms-putaway-from"
                v-model="createForm.fromLocationCode"
                :options="locationOptions"
                title="选择暂存库位"
                placeholder="暂存库位"
                :source-text="WAREHOUSE_CATALOG_SOURCE_TEXT"
                :empty-text="WAREHOUSE_LOCATION_EMPTY_TEXT"
                :loading="warehouseCatalogPending"
                clearable
                aria-label="来源库位"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="wms-putaway-to">目标库位</NvFieldLabel>
              <NvEntityPicker
                id="wms-putaway-to"
                v-model="createForm.toLocationCode"
                :options="locationOptions"
                title="选择货架库位"
                placeholder="货架库位"
                :source-text="WAREHOUSE_CATALOG_SOURCE_TEXT"
                :empty-text="WAREHOUSE_LOCATION_EMPTY_TEXT"
                :loading="warehouseCatalogPending"
                clearable
                aria-label="目标库位"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="wms-putaway-qty">上架数量</NvFieldLabel>
              <NvInput
                id="wms-putaway-qty"
                v-model="createForm.quantity"
                type="number"
                min="0.000001"
                step="any"
                autocomplete="off"
                required
              />
            </NvField>
          </NvFieldGroup>

          <NvFieldError v-if="createError" :errors="[createError]" />

          <NvDialogFooter>
            <NvDialogClose as-child>
              <NvButton type="button" variant="outline">取消</NvButton>
            </NvDialogClose>
            <NvButton type="submit" :disabled="createPutawayPending">创建上架任务</NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
