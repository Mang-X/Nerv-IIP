<script setup lang="ts">
import type { BusinessConsoleQualityItem } from '@nerv-iip/api-client'
import type { EntityPickerOption, NvDataTableColumn } from '@nerv-iip/ui'
import {
  NvButton,
  NvDataTable,
  NvEntityPicker,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import { computed } from 'vue'

import { useQualityFirstArticleInspections } from '@/composables/quality/useBusinessQuality'
import { usePagedList } from '@/composables/shared/usePagedList'
import { inlineErrorMessage } from '@/utils/notify'

defineProps<{
  skuOptions: EntityPickerOption[]
  skusPending: boolean
}>()
const emit = defineEmits<{
  'open-record': [recordId: string]
}>()

const {
  firstArticleRecords,
  firstArticleRecordsError,
  firstArticleRecordsPending,
  firstArticleRecordsTotal,
  recordFilters,
  refreshFirstArticleRecords,
} = useQualityFirstArticleInspections()
const { page, pageSize } = usePagedList(recordFilters, {
  resetOn: [
    () => recordFilters.skuCode,
    () => recordFilters.result,
    () => recordFilters.sourceType,
  ],
})

const errorMessage = computed(() => inlineErrorMessage(firstArticleRecordsError.value))
const resultModel = computed({
  get: () => recordFilters.result || 'all',
  set: (value: string) => {
    recordFilters.result = value === 'all' ? undefined : value
  },
})
function resetFilters() {
  recordFilters.sourceType = undefined
  recordFilters.skuCode = undefined
  recordFilters.result = undefined
  page.value = 1
}
defineExpose({ refresh: refreshFirstArticleRecords, resetFilters })
const sourceModel = computed({
  get: () => recordFilters.sourceType || 'all',
  set: (value: string) => {
    recordFilters.sourceType = value === 'all' ? undefined : value
  },
})
const columns: NvDataTableColumn<BusinessConsoleQualityItem>[] = [
  { key: 'code', header: '检验记录', cellClass: 'font-medium' },
  { key: 'skuCode', header: '物料' },
  { key: 'sourceDocumentId', header: '来源单据' },
  { key: 'status', header: '结果', width: 'w-28' },
  { key: 'batchNo', header: '批次 / 序列号' },
]

function recordIdOf(row: BusinessConsoleQualityItem) {
  return row.id?.trim() || row.code?.trim() || ''
}
</script>

<template>
  <section class="grid gap-3" aria-label="检验记录">
    <NvToolbar :show-search="false">
      <template #filters>
        <NvSelect v-model="sourceModel">
          <NvSelectTrigger class="w-36" aria-label="按来源筛选检验记录">
            <NvSelectValue placeholder="全部来源" />
          </NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem value="all">全部来源</NvSelectItem>
            <NvSelectItem value="first-article">首件</NvSelectItem>
            <NvSelectItem value="receiving">来料</NvSelectItem>
            <NvSelectItem value="operation">过程（工序）</NvSelectItem>
            <NvSelectItem value="in-process">过程检验</NvSelectItem>
            <NvSelectItem value="final">终检</NvSelectItem>
            <NvSelectItem value="customer-return">客户退货</NvSelectItem>
            <NvSelectItem value="maintenance">维修</NvSelectItem>
          </NvSelectContent>
        </NvSelect>
        <NvEntityPicker
          v-model="recordFilters.skuCode"
          class="w-64"
          :options="skuOptions"
          :loading="skusPending"
          title="按物料筛选检验记录"
          placeholder="全部物料"
          clearable
          aria-label="按物料筛选检验记录"
        />
        <NvSelect v-model="resultModel">
          <NvSelectTrigger class="w-36" aria-label="按结果筛选检验记录">
            <NvSelectValue placeholder="全部结果" />
          </NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem value="all">全部结果</NvSelectItem>
            <NvSelectItem value="passed">合格</NvSelectItem>
            <NvSelectItem value="failed">不合格</NvSelectItem>
            <NvSelectItem value="conditional-release">让步放行</NvSelectItem>
          </NvSelectContent>
        </NvSelect>
      </template>
    </NvToolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">
      {{ errorMessage }}
    </p>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="firstArticleRecordsTotal"
      :columns="columns"
      :rows="firstArticleRecords"
      :row-key="(row) => recordIdOf(row)"
      :loading="firstArticleRecordsPending"
      :searchable="false"
      :column-settings="false"
      empty-message="当前筛选下没有检验记录。"
      @update:page="page = $event"
      @update:page-size="(value) => (pageSize = String(value))"
    >
      <template #cell-code="{ row }">
        <NvButton
          v-if="recordIdOf(row)"
          type="button"
          variant="link"
          class="h-auto p-0 font-medium"
          @click="emit('open-record', recordIdOf(row))"
        >
          {{ row.code ?? row.id }}
        </NvButton>
        <span v-else>—</span>
      </template>
      <template #cell-status="{ row }">
        <NvStatusBadge :value="row.status" />
      </template>
      <template #cell-batchNo="{ row }">
        {{ [row.batchNo, row.serialNo].filter(Boolean).join(' / ') || '—' }}
      </template>
    </NvDataTable>
  </section>
</template>
