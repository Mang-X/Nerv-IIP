<script setup lang="ts">
import type { BusinessConsoleMesReceiptRequestRow } from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { useMesReceiptInventorySource } from '@/composables/useBusinessMes'
import { receiptStatusLabel } from '@/composables/mes/useMesReferenceLabels'
import { inlineErrorMessage } from '@/utils/notify'
import {
  NvButton,
  NvDataTable,
  NvSpinner,
  NvStatusBadge,
  NvDialog,
  NvDialogContent,
  NvDialogHeader,
  NvDialogTitle,
  NvDialogDescription,
} from '@nerv-iip/ui'
import { computed } from 'vue'
import { labelFor, QUALITY_STATUS_LABELS } from '@/data/businessLabels'

const props = defineProps<{ receipt: BusinessConsoleMesReceiptRequestRow }>()
const emit = defineEmits<{ close: [] }>()
const { source, sourcePending, sourceError } = useMesReceiptInventorySource(() => props.receipt)
const identityMatches = computed(
  () =>
    source.value?.requestNo === props.receipt.requestNo &&
    source.value?.workOrderId === props.receipt.workOrderId &&
    (source.value?.producedLotNo ?? '') === (props.receipt.producedLotNo ?? ''),
)
const linkLabels: Record<string, string> = {
  notPosted: '未过账',
  postingFailed: '过账失败',
  partiallyPosted: '部分过账',
  qualityRestricted: '质量受限',
  posted: '已过账',
}
const movements = computed(() => source.value?.movements ?? [])
const balances = computed(() => source.value?.balances ?? [])
type Movement = (typeof movements)['value'][number]
type Balance = (typeof balances)['value'][number]
const movementColumns: NvDataTableColumn<Movement>[] = [
  {
    key: 'sourceDocumentId',
    width: 'w-48',
    cellClass: 'whitespace-normal break-all',
    header: '来源入库单',
  },
  { key: 'skuCode', width: 'w-48', cellClass: 'whitespace-normal break-all', header: '成品' },
  { key: 'lotNo', width: 'w-48', cellClass: 'whitespace-normal break-all', header: '库存批次' },
  { key: 'serialNo', width: 'w-48', cellClass: 'whitespace-normal break-all', header: '序列号' },
  { key: 'locationCode', width: 'w-48', cellClass: 'whitespace-normal break-all', header: '库位' },
  { key: 'quantity', width: 'w-28', header: '移动数量' },
  { key: 'uomCode', width: 'w-28', header: '单位' },
  { key: 'qualityStatus', width: 'w-28', header: '质量状态' },
  {
    key: 'postedAtUtc',
    width: 'w-48',
    cellClass: 'whitespace-normal break-all',
    header: '过账时间',
  },
]
const balanceColumns: NvDataTableColumn<Balance>[] = [
  { key: 'skuCode', width: 'w-48', cellClass: 'whitespace-normal break-all', header: '成品' },
  { key: 'lotNo', width: 'w-48', cellClass: 'whitespace-normal break-all', header: '库存批次' },
  { key: 'serialNo', width: 'w-48', cellClass: 'whitespace-normal break-all', header: '序列号' },
  { key: 'locationCode', width: 'w-48', cellClass: 'whitespace-normal break-all', header: '库位' },
  { key: 'onHandQuantity', width: 'w-28', header: '在库数量' },
  { key: 'reservedQuantity', width: 'w-28', header: '预留数量' },
  { key: 'availableQuantity', width: 'w-28', header: '可用数量' },
  { key: 'uomCode', width: 'w-28', header: '单位' },
  { key: 'qualityStatus', width: 'w-28', header: '质量状态' },
]
</script>

<template>
  <NvDialog
    :open="true"
    @update:open="
      (value) => {
        if (!value) emit('close')
      }
    "
  >
    <NvDialogContent class="max-h-[85vh] overflow-y-auto sm:max-w-6xl">
      <NvDialogHeader>
        <NvDialogTitle>入库库存来源</NvDialogTitle>
        <NvDialogDescription>核对所选入库单的过账与库存事实</NvDialogDescription>
      </NvDialogHeader>
      <section class="my-4 grid min-w-0 gap-4 rounded-md border p-4" aria-label="入库库存来源">
        <div class="flex items-center justify-between gap-2">
          <h2 class="text-lg font-semibold">库存来源 · {{ receipt.requestNo }}</h2>
          <NvButton size="sm" variant="outline" @click="$emit('close')">关闭库存来源</NvButton>
        </div>
        <p v-if="sourcePending"><NvSpinner aria-hidden="true" />加载库存来源…</p>
        <p v-else-if="sourceError" role="alert">
          {{ inlineErrorMessage(sourceError, '库存来源读取失败。') }}
        </p>
        <p v-else-if="source && !identityMatches" role="alert">
          入库来源与所选工单、入库单或产出批次不一致，请刷新登记后核对。
        </p>
        <template v-else-if="source && identityMatches">
          <p>
            工单 {{ source.workOrderNo || source.workOrderId }} · 产出批次
            {{ source.producedLotNo || '—' }} · 序列号 {{ source.serialNo || '—' }}
          </p>
          <p>
            登记数量 {{ source.requestedQuantity }} · 已过账 {{ source.postedQuantity ?? '—' }} ·
            剩余
            {{ source.remainingQuantity ?? '—' }}
          </p>
          <p>
            入库状态：{{ receiptStatusLabel(source.receiptStatus) }} · 库存状态：{{
              linkLabels[source.linkStatus ?? ''] || source.linkStatus
            }}
          </p>
          <p v-if="!source.isInventoryLinkEstablished">尚未建立库存来源关联。</p>
          <p v-if="source.inventoryPostingFailureMessage" class="text-destructive">
            {{ source.inventoryPostingFailureMessage }}
          </p>
          <h3 class="font-semibold">库存移动</h3>
          <NvDataTable
            class="min-w-0"
            :columns="movementColumns"
            :rows="movements"
            row-key="movementId"
            :searchable="false"
            :column-settings="false"
            empty-message="暂无该入库单的库存移动。"
          >
            <template #cell-qualityStatus="{ row }"
              ><NvStatusBadge
                :value="row.qualityStatus"
                :label="labelFor(QUALITY_STATUS_LABELS, row.qualityStatus)"
            /></template>
          </NvDataTable>
          <h3 class="font-semibold">对应库存余额</h3>
          <NvDataTable
            class="min-w-0"
            :row-key="
              (row) =>
                JSON.stringify([
                  row.skuCode,
                  row.siteCode,
                  row.locationCode,
                  row.lotNo,
                  row.serialNo,
                  row.qualityStatus,
                  row.ownerType,
                  row.ownerId,
                  row.productionDate,
                  row.expiryDate,
                ])
            "
            :columns="balanceColumns"
            :rows="balances"
            :searchable="false"
            :column-settings="false"
            empty-message="暂无对应库存余额。"
          >
            <template #cell-qualityStatus="{ row }"
              ><NvStatusBadge
                :value="row.qualityStatus"
                :label="labelFor(QUALITY_STATUS_LABELS, row.qualityStatus)"
            /></template>
          </NvDataTable>
        </template>
      </section>
    </NvDialogContent>
  </NvDialog>
</template>
