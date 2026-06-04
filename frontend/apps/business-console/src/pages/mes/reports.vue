<script setup lang="ts">
import type { DataTableColumn } from '@nerv-iip/ui'
import { useMesProductionReports } from '@/composables/useBusinessMes'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Button,
  DataTable,
  Input,
  PageHeader,
  SectionCard,
  SectionCards,
  Toolbar,
} from '@nerv-iip/ui'
import { RefreshCwIcon } from 'lucide-vue-next'
import { computed, ref } from 'vue'

definePage({ meta: { requiresAuth: true, title: '报工与完工' } })

const { filters, productionReports, productionReportsError, productionReportsPending, refreshProductionReports } = useMesProductionReports()

const keyword = ref('')
const rows = computed(() => {
  const kw = keyword.value.trim().toLowerCase()
  if (!kw) return productionReports.value
  return productionReports.value.filter((r) =>
    [r.productionReportId, r.workOrderId, r.operationTaskId].some((v) => (v ?? '').toLowerCase().includes(kw)),
  )
})
const goodTotal = computed(() => productionReports.value.reduce((s, r) => s + (r.goodQuantity ?? 0), 0))
const scrapTotal = computed(() => productionReports.value.reduce((s, r) => s + (r.scrapQuantity ?? 0), 0))
const errorMessage = computed(() => formatError(productionReportsError.value))

type ReportRow = (typeof rows)['value'][number]
const columns: DataTableColumn<ReportRow>[] = [
  { key: 'productionReportId', header: '报工单', cellClass: 'font-medium' },
  { key: 'workOrderId', header: '工单' },
  { key: 'operationTaskId', header: '工序任务' },
  { key: 'goodQuantity', header: '合格数', align: 'end', width: 'w-20' },
  { key: 'scrapQuantity', header: '不良数', align: 'end', width: 'w-20' },
  { key: 'reworkQuantity', header: '返工数', align: 'end', width: 'w-20' },
  { key: 'reportedAtUtc', header: '报工时间', width: 'w-44' },
]

function formatDateTime(value?: string | null) {
  if (!value) return '未指定'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
</script>

<template>
  <BusinessLayout>
    <PageHeader title="报工与完工" :breadcrumbs="[{ label: '制造执行' }]" :count="`${rows.length} 条报工`">
      <template #actions>
        <Button size="sm" type="button" variant="outline" :disabled="productionReportsPending" @click="refreshProductionReports">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
      </template>
    </PageHeader>

    <SectionCards :columns="3">
      <SectionCard description="报工记录" :value="productionReports.length" hint="合格/不良/返工依据" />
      <SectionCard description="合格总数" :value="goodTotal" hint="累计合格数量" />
      <SectionCard description="不良总数" :value="scrapTotal" hint="累计不良数量" />
    </SectionCards>

    <Toolbar v-model:search="keyword" search-placeholder="搜索报工单、工单、工序">
      <template #filters>
        <Input v-model="filters.status" class="h-9 w-32" placeholder="状态（可选）" aria-label="报工状态" />
      </template>
    </Toolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <DataTable
      :columns="columns"
      :rows="rows"
      row-key="productionReportId"
      :loading="productionReportsPending"
      empty-message="暂无报工记录。新增报工请从工单或工序上下文进入。"
    >
      <template #cell-goodQuantity="{ row }"><span class="tabular-nums">{{ row.goodQuantity ?? 0 }}</span></template>
      <template #cell-scrapQuantity="{ row }"><span class="tabular-nums">{{ row.scrapQuantity ?? 0 }}</span></template>
      <template #cell-reworkQuantity="{ row }"><span class="tabular-nums">{{ row.reworkQuantity ?? 0 }}</span></template>
      <template #cell-reportedAtUtc="{ row }">{{ formatDateTime(row.reportedAtUtc) }}</template>
    </DataTable>
  </BusinessLayout>
</template>
