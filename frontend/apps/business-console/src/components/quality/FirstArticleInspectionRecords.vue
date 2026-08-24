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
import { RefreshCwIcon } from '@lucide/vue'
import { computed } from 'vue'

import { useQualityFirstArticleInspections } from '@/composables/useBusinessQuality'
import { usePagedList } from '@/composables/usePagedList'
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
  resetOn: [() => recordFilters.skuCode, () => recordFilters.result],
})

const errorMessage = computed(() => inlineErrorMessage(firstArticleRecordsError.value))
const resultModel = computed({
  get: () => recordFilters.result || 'all',
  set: (value: string) => {
    recordFilters.result = value === 'all' ? undefined : value
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
  <section class="grid gap-3" aria-labelledby="first-article-records-title">
    <div class="flex flex-wrap items-start justify-between gap-3">
      <div>
        <h2 id="first-article-records-title" class="text-lg font-semibold">首件检验记录</h2>
        <p class="text-sm text-muted-foreground">
          仅显示首件确认产生的记录，可按物料和结果快速定位并打开打印详情。
        </p>
      </div>
      <NvButton
        type="button"
        size="sm"
        variant="outline"
        :disabled="firstArticleRecordsPending"
        @click="refreshFirstArticleRecords"
      >
        <RefreshCwIcon aria-hidden="true" />
        刷新记录
      </NvButton>
    </div>

    <NvToolbar :show-search="false">
      <template #filters>
        <NvEntityPicker
          v-model="recordFilters.skuCode"
          class="w-64"
          :options="skuOptions"
          :loading="skusPending"
          title="按物料筛选首件记录"
          placeholder="全部物料"
          source-text="数据来自物料主数据"
          clearable
          aria-label="按物料筛选首件记录"
        />
        <NvSelect v-model="resultModel">
          <NvSelectTrigger class="w-36" aria-label="按结果筛选首件记录">
            <NvSelectValue placeholder="全部结果" />
          </NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem value="all">全部结果</NvSelectItem>
            <NvSelectItem value="passed">合格</NvSelectItem>
            <NvSelectItem value="rejected">不合格</NvSelectItem>
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
      empty-message="当前筛选下没有首件检验记录。首件记录会在现场完成首件确认后出现在这里。"
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
