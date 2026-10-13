<script setup lang="ts">
import { useMesProductionReportDetail } from '@/composables/useBusinessMes'
import { inlineErrorMessage } from '@/utils/notify'
import { formatDateTime } from '@/utils/format'
import {
  NvButton,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvSpinner,
} from '@nerv-iip/ui'
import { computed } from 'vue'

const reportNo = defineModel<string | null>('reportNo', { default: null })
const { detail, detailError, detailPending, refresh } = useMesProductionReportDetail(reportNo)
const open = computed({
  get: () => !!reportNo.value,
  set: (value) => {
    if (!value) reportNo.value = null
  },
})
const report = computed(() => detail.value?.report)
const errorMessage = computed(() => inlineErrorMessage(detailError.value))
const fields = computed(() => [
  { label: '工单', value: report.value?.workOrderNo || '—' },
  { label: '工序任务', value: report.value?.operationTaskNo || '—' },
  { label: '操作人', value: report.value?.reportedByDisplayName?.trim() || '姓名不可用' },
  {
    label: '报工时间',
    value: formatDateTime(report.value?.reportedAtUtc),
  },
  { label: '良品', value: report.value?.goodQuantity ?? 0 },
  { label: '报废', value: report.value?.scrapQuantity ?? 0 },
  { label: '返工', value: report.value?.reworkQuantity ?? 0 },
])
</script>

<template>
  <NvDialog v-model:open="open">
    <NvDialogContent class="sm:max-w-lg">
      <NvDialogHeader>
        <NvDialogTitle>报工详情 · {{ reportNo }}</NvDialogTitle>
        <NvDialogDescription>查看本条报工的操作人与生产记录。</NvDialogDescription>
      </NvDialogHeader>
      <div v-if="detailError" class="space-y-2">
        <p>{{ errorMessage }}</p>
        <NvButton type="button" variant="outline" @click="refresh">重试</NvButton>
      </div>
      <div v-else-if="detailPending || !report" class="flex items-center gap-2">
        <NvSpinner class="size-4" /> 加载报工详情…
      </div>
      <dl v-else class="grid grid-cols-2 gap-4 text-sm">
        <div v-for="field in fields" :key="field.label">
          <dt class="text-muted-foreground">{{ field.label }}</dt>
          <dd class="mt-1 font-medium">{{ field.value }}</dd>
        </div>
      </dl>
      <NvDialogFooter>
        <NvButton type="button" variant="outline" @click="reportNo = null">关闭</NvButton>
      </NvDialogFooter>
    </NvDialogContent>
  </NvDialog>
</template>
