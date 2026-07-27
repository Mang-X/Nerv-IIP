<script setup lang="ts">
import type { EntityPickerOption, NvDataTableColumn, NvMetricStripCell } from '@nerv-iip/ui'
import {
  useMesTraceability,
  useMesWorkOrders,
  useMesWorkOrderProducedLots,
} from '@/composables/useBusinessMes'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvButton,
  NvDataTable,
  NvEntityPicker,
  NvInput,
  NvMetricStrip,
  NvPageHeader,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvToolbar,
  resolveStatus,
} from '@nerv-iip/ui'
import { RefreshCwIcon } from '@lucide/vue'
import { computed, watch } from 'vue'
import { RouterLink, useRoute } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '追溯查询',
    requiredPermissions: ['business.mes.traceability.read'],
  },
})

const { filters, refreshTraceability, traceability, traceabilityError, traceabilityPending } =
  useMesTraceability()
const route = useRoute()

watch(
  () => route.query,
  (query) => {
    const mode = firstQuery(query.mode)
    const batchOrSerial =
      firstQuery(query.batchOrSerial) || firstQuery(query.serialNo) || firstQuery(query.batchNo)
    const materialLotId = firstQuery(query.materialLotId)
    const workOrderId = firstQuery(query.workOrderId)

    if (mode === 'work-order' || mode === 'batch' || mode === 'material-lot') filters.mode = mode
    if (workOrderId) filters.workOrderId = workOrderId
    if (batchOrSerial) {
      filters.batchOrSerial = batchOrSerial
      filters.materialLotId = materialLotId || batchOrSerial
    } else if (materialLotId) {
      filters.materialLotId = materialLotId
      if (!mode) filters.mode = 'material-lot'
    }
  },
  { immediate: true },
)

const nodes = computed(() => traceability.value?.nodes ?? [])
const errorMessage = computed(() => formatError(traceabilityError.value))
const batchModel = computed({
  get: () => filters.batchOrSerial ?? '',
  set: (value: string) => {
    filters.batchOrSerial = value
    filters.materialLotId = value
  },
})

// 工单目录：追溯按工单标识查询，选项展示真实工单号（WO-…），物料与状态作辅助识别。
const workOrderCatalog = useMesWorkOrders({ initialTake: 200 })
const workOrderOptions = computed<EntityPickerOption[]>(() =>
  workOrderCatalog.workOrders.value.flatMap((order) => {
    const value = order.workOrderId?.trim()
    if (!value) return []
    const hint = [order.skuCode?.trim(), order.status ? resolveStatus(order.status).label : '']
      .filter(Boolean)
      .join(' · ')
    return [{ value, label: order.workOrderNo?.trim() || value, ...(hint ? { hint } : {}) }]
  }),
)
// 换工单等于换了追溯对象：上一张工单的产出批次不再成立，跟着清掉，避免留下陈旧批次。
const workOrderModel = computed({
  get: () => filters.workOrderId,
  set: (value: string) => {
    if (value === filters.workOrderId) return
    filters.workOrderId = value
    batchModel.value = ''
  },
})

// 批次/序列号没有通用目录读面，只有「某工单的产出批次」这一条权威来源；
// 因此选了工单才能挑批次，没选工单时如实保留手工录入。
const { producedLots, producedLotsPending } = useMesWorkOrderProducedLots(() => filters.workOrderId)
const producedLotOptions = computed<EntityPickerOption[]>(() =>
  producedLots.value.map((lot) => {
    const hint = [
      lot.serialNo ? `序列号 ${lot.serialNo}` : '',
      lot.reportNo ? `报工 ${lot.reportNo}` : '',
      `剩余 ${lot.remainingQuantity}`,
    ]
      .filter(Boolean)
      .join(' · ')
    return { value: lot.producedLotNo, label: lot.producedLotNo, hint }
  }),
)
const hasWorkOrderScope = computed(() => filters.workOrderId.trim().length > 0)
const scanRecordQuery = computed(() => ({
  sourceWorkflow: filters.mode === 'work-order' ? 'production.report' : undefined,
  sourceDocumentId: filters.workOrderId || batchModel.value || undefined,
  scannedValue: batchModel.value || undefined,
}))

const traceCells = computed<NvMetricStripCell[]>(() => [
  { key: 'nodes', label: '追溯节点', value: nodes.value.length, unit: '个' },
  { key: 'edges', label: '上下游关联', value: traceability.value?.edges?.length ?? 0, unit: '条' },
])

type NodeRow = (typeof nodes)['value'][number]
const columns: NvDataTableColumn<NodeRow>[] = [
  { key: 'nodeId', header: '节点', cellClass: 'font-medium' },
  { key: 'nodeType', header: '类型', width: 'w-32' },
  { key: 'displayName', header: '名称' },
  { key: 'status', header: '状态', width: 'w-28' },
]

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
    <NvPageHeader title="追溯查询" :breadcrumbs="[{ label: '制造执行' }]">
      <template #actions>
        <NvButton size="sm" type="button" variant="outline" as-child>
          <RouterLink :to="{ path: '/barcode/scans', query: scanRecordQuery }">扫码记录</RouterLink>
        </NvButton>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="traceabilityPending"
          @click="refreshTraceability"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <NvMetricStrip :cells="traceCells" />

    <NvToolbar :show-search="false">
      <template #filters>
        <NvSelect v-model="filters.mode">
          <NvSelectTrigger class="h-9 w-36" aria-label="查询类型"
            ><NvSelectValue
          /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem value="work-order">工单</NvSelectItem>
            <NvSelectItem value="batch">批次/序列号</NvSelectItem>
            <NvSelectItem value="material-lot">物料批</NvSelectItem>
          </NvSelectContent>
        </NvSelect>
        <NvEntityPicker
          v-model="workOrderModel"
          class="w-56"
          :options="workOrderOptions"
          title="选择工单"
          placeholder="选择工单"
          source-text="数据来自制造执行工单列表"
          empty-text="当前范围内没有工单"
          :loading="workOrderCatalog.workOrdersPending.value"
          clearable
          aria-label="工单号"
        />
        <NvEntityPicker
          v-if="hasWorkOrderScope"
          v-model="batchModel"
          class="w-56"
          :options="producedLotOptions"
          title="选择产出批次"
          placeholder="选择产出批次"
          source-text="数据来自该工单的报工产出批次"
          empty-text="该工单还没有产出批次"
          :loading="producedLotsPending"
          clearable
          aria-label="批次或物料批"
        />
        <NvInput
          v-else
          v-model="batchModel"
          class="h-9 w-64"
          placeholder="先选工单可直接挑批次，或直接输入批次/序列号"
          aria-label="批次或物料批"
        />
      </template>
    </NvToolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <NvDataTable
      :columns="columns"
      :rows="nodes"
      row-key="nodeId"
      :loading="traceabilityPending"
      :searchable="false"
      :column-settings="false"
      empty-message="暂无追溯数据。先选择查询类型并填入工单、批次/序列号或物料批，再查询它经过的工序、用料与检验记录。"
    />
  </BusinessLayout>
</template>
