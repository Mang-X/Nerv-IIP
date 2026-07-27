<script setup lang="ts">
import type { BusinessConsoleWmsWarehouseTaskItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import WmsInventoryContextPanel from '@/components/wms/WmsInventoryContextPanel.vue'
import { useWmsOutboundOrders, useWmsPickingTasks } from '@/composables/useBusinessWms'
import { usePagedList } from '@/composables/usePagedList'
import {
  useWarehouseCodeCatalog,
  WAREHOUSE_CATALOG_SOURCE_TEXT,
  WAREHOUSE_LOCATION_EMPTY_TEXT,
} from '@/composables/useWarehouseCodeCatalog'
import { wmsWarehouseTaskStatusFilterOptions, WMS_STATUS_ANY } from '@/data/wmsReference'
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
} = useWmsPickingTasks()
const route = useRoute()
const { page, pageSize } = usePagedList(filters, {
  resetOn: [() => filters.status, () => filters.locationCode, () => filters.keyword],
})
// 库位后端无主数据读面，从真实的上架/拣货/盘点任务与出库单行里派生可选项。
const { locationOptions, warehouseCatalogPending } = useWarehouseCodeCatalog()
// 出库单是真实读面（只要组织/环境即可列出），拣货任务必须挂在已存在的出库单下。
const { outboundOrders, outboundOrdersPending } = useWmsOutboundOrders({ take: 200 })
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
  get: () => outboundOrderNoById.value.get(createForm.outboundOrderId) ?? createForm.outboundOrderId,
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

const errorMessage = computed(() =>
  formatError(pickingTasksError.value ?? createPickingError.value),
)

type PickingRow = BusinessConsoleWmsWarehouseTaskItem
const columns: NvDataTableColumn<PickingRow>[] = [
  {
    key: 'taskNo',
    header: '任务号',
    cellClass: 'font-medium',
    accessor: (r) => r.taskNo ?? r.warehouseTaskId ?? '无',
  },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'sourceOrderNo', header: '来源单据', accessor: (r) => r.sourceOrderNo ?? '—' },
  { key: 'skuCode', header: '物料', accessor: (r) => r.skuCode ?? '—' },
  { key: 'inventoryContext', header: '库存上下文', width: 'w-72' },
  {
    key: 'location',
    header: '起讫库位',
    accessor: (r) => `${r.fromLocationCode ?? '—'} → ${r.toLocationCode ?? '—'}`,
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
function firstQuery(value: unknown) {
  if (Array.isArray(value)) return typeof value[0] === 'string' ? value[0] : ''
  return typeof value === 'string' ? value : ''
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="拣货任务"
      :breadcrumbs="[{ label: '仓储作业' }]"
      :count="`${pickingTasksTotal} 个拣货任务`"
    >
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="pickingTasksPending"
          @click="refreshPickingTasks"
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

    <NvToolbar :show-search="false">
      <template #filters>
        <NvInput
          v-model="filters.keyword"
          class="h-9 w-40"
          placeholder="任务号/物料"
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
          aria-label="拣货任务状态"
        />
      </template>
    </NvToolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

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
      :searchable="false"
      :column-settings="false"
      empty-message="暂无拣货任务。领料齐套或出库拣货时由系统派生，或在此手工登记。"
    >
      <template #cell-status="{ row }"><NvStatusBadge :value="row.status" /></template>
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
