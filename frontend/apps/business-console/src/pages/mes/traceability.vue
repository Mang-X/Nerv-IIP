<script setup lang="ts">
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { useMesTraceability } from '@/composables/useBusinessMes'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvButton,
  NvDataTable,
  NvInput,
  NvPageHeader,
  NvSectionCard,
  NvSectionCards,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvToolbar,
} from '@nerv-iip/ui'
import { RefreshCwIcon } from 'lucide-vue-next'
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
const scanRecordQuery = computed(() => ({
  sourceWorkflow: filters.mode === 'work-order' ? 'production.report' : undefined,
  sourceDocumentId: filters.workOrderId || batchModel.value || undefined,
  scannedValue: batchModel.value || undefined,
}))

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

    <NvSectionCards :columns="2">
      <NvSectionCard description="节点" :value="nodes.length" hint="执行证据对象" />
      <NvSectionCard
        description="关系"
        :value="traceability?.edges?.length ?? 0"
        hint="上下游关联"
      />
    </NvSectionCards>

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
        <NvInput
          v-model="filters.workOrderId"
          class="h-9 w-40"
          placeholder="工单号"
          aria-label="工单号"
        />
        <NvInput
          v-model="batchModel"
          class="h-9 w-44"
          placeholder="批次/序列号/物料批"
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
      empty-message="暂无追溯数据。输入工单、批次/序列号或物料批后查询执行证据链。"
    />
  </BusinessLayout>
</template>
