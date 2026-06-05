<script setup lang="ts">
import type { DataTableColumn } from '@nerv-iip/ui'
import { useMesWipSummary } from '@/composables/useBusinessMes'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Button,
  DataTable,
  DataTablePagination,
  Input,
  PageHeader,
  SectionCard,
  SectionCards,
  StatusBadge,
  Toolbar,
} from '@nerv-iip/ui'
import { RefreshCwIcon } from 'lucide-vue-next'
import { computed, ref } from 'vue'

definePage({ meta: { requiresAuth: true, title: '在制跟踪' } })

const { filters, refreshWip, wipError, wipPending, wipRows, wipTotal } = useMesWipSummary()
const { page, pageSize } = usePagedList(filters, { resetOn: [() => filters.status] })

const keyword = ref('')
const filtered = computed(() => {
  const kw = keyword.value.trim().toLowerCase()
  if (!kw) return wipRows.value
  return wipRows.value.filter((r) =>
    [r.workOrderId, r.operationTaskId, r.workCenterId, r.status].some((v) => (v ?? '').toLowerCase().includes(kw)),
  )
})

const goodTotal = computed(() => wipRows.value.reduce((s, r) => s + (r.goodQuantity ?? 0), 0))
const scrapTotal = computed(() => wipRows.value.reduce((s, r) => s + (r.scrapQuantity ?? 0), 0))
const errorMessage = computed(() => formatError(wipError.value))

type WipRow = (typeof wipRows)['value'][number]
const columns: DataTableColumn<WipRow>[] = [
  { key: 'workOrderId', header: '工单', cellClass: 'font-medium', accessor: (r) => r.workOrderId ?? '无' },
  { key: 'operationTaskId', header: '工序任务', accessor: (r) => r.operationTaskId ?? '无' },
  { key: 'workCenterId', header: '工作中心', accessor: (r) => r.workCenterId ?? '无' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'plannedQuantity', header: '计划数', align: 'end', width: 'w-20' },
  { key: 'goodQuantity', header: '良品', align: 'end', width: 'w-20' },
  { key: 'scrapQuantity', header: '报废', align: 'end', width: 'w-20' },
  { key: 'blockingReasons', header: '阻塞原因', accessor: (r) => r.blockingReasons?.join('，') || '无' },
]

function formatQuantity(value?: number) {
  return new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 3 }).format(value ?? 0)
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
</script>

<template>
  <BusinessLayout>
    <PageHeader title="在制跟踪" :breadcrumbs="[{ label: '制造执行' }]" :count="`${wipTotal} 行在制`">
      <template #actions>
        <Button size="sm" type="button" variant="outline" :disabled="wipPending" @click="refreshWip">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
      </template>
    </PageHeader>

    <SectionCards :columns="3">
      <SectionCard description="在制行" :value="wipTotal" hint="后端筛选总数" />
      <SectionCard description="本页良品数" :value="formatQuantity(goodTotal)" hint="当前页合计" />
      <SectionCard description="本页报废数" :value="formatQuantity(scrapTotal)" hint="当前页合计" />
    </SectionCards>

    <Toolbar v-model:search="keyword" search-placeholder="搜索工单、工序、工作中心">
      <template #filters>
        <Input v-model="filters.status" class="h-9 w-32" placeholder="状态（可选）" aria-label="在制状态" />
      </template>
    </Toolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <DataTable
      :columns="columns"
      :rows="filtered"
      :row-key="(r) => `${r.workOrderId}-${r.operationTaskId}`"
      :loading="wipPending"
      empty-message="暂无在制数据。工单释放并排程后，在制行会出现在这里。"
    >
      <template #cell-status="{ row }"><StatusBadge :value="row.status" /></template>
      <template #cell-plannedQuantity="{ row }"><span class="tabular-nums">{{ formatQuantity(row.plannedQuantity) }}</span></template>
      <template #cell-goodQuantity="{ row }"><span class="tabular-nums">{{ formatQuantity(row.goodQuantity) }}</span></template>
      <template #cell-scrapQuantity="{ row }"><span class="tabular-nums">{{ formatQuantity(row.scrapQuantity) }}</span></template>
    </DataTable>

    <DataTablePagination v-model:page="page" v-model:page-size="pageSize" :total-items="wipTotal" />
  </BusinessLayout>
</template>
