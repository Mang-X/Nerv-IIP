<script setup lang="ts">
import type { DataTableColumn } from '@nerv-iip/ui'
import { useMesWipSummary } from '@/composables/useBusinessMes'
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
import { computed, ref, watch } from 'vue'

definePage({ meta: { requiresAuth: true, title: '在制跟踪' } })

const { filters, refreshWip, wipError, wipPending, wipRows } = useMesWipSummary()

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

const page = ref(1)
const pageSize = ref('10')
const pageSizeNumber = computed(() => Number(pageSize.value) || 10)
const pagedRows = computed(() => {
  const start = (page.value - 1) * pageSizeNumber.value
  return filtered.value.slice(start, start + pageSizeNumber.value)
})
watch([keyword, pageSize, () => wipRows.value.length], () => {
  page.value = 1
})

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
    <PageHeader title="在制跟踪" :breadcrumbs="[{ label: '制造执行' }]" :count="`${filtered.length} 行在制`">
      <template #actions>
        <Button size="sm" type="button" variant="outline" :disabled="wipPending" @click="refreshWip">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
      </template>
    </PageHeader>

    <SectionCards :columns="3">
      <SectionCard description="在制行" :value="wipRows.length" hint="工单 / 工序粒度" />
      <SectionCard description="良品数" :value="formatQuantity(goodTotal)" hint="已报工良品" />
      <SectionCard description="报废数" :value="formatQuantity(scrapTotal)" hint="已报工报废" />
    </SectionCards>

    <Toolbar v-model:search="keyword" search-placeholder="搜索工单、工序、工作中心">
      <template #filters>
        <Input v-model="filters.status" class="h-9 w-32" placeholder="状态（可选）" aria-label="在制状态" />
      </template>
    </Toolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <DataTable
      :columns="columns"
      :rows="pagedRows"
      :row-key="(r) => `${r.workOrderId}-${r.operationTaskId}`"
      :loading="wipPending"
      empty-message="暂无在制数据。工单释放并排程后，在制行会出现在这里。"
    >
      <template #cell-status="{ row }"><StatusBadge :value="row.status" /></template>
      <template #cell-plannedQuantity="{ row }"><span class="tabular-nums">{{ formatQuantity(row.plannedQuantity) }}</span></template>
      <template #cell-goodQuantity="{ row }"><span class="tabular-nums">{{ formatQuantity(row.goodQuantity) }}</span></template>
      <template #cell-scrapQuantity="{ row }"><span class="tabular-nums">{{ formatQuantity(row.scrapQuantity) }}</span></template>
    </DataTable>

    <DataTablePagination
      v-model:page="page"
      v-model:page-size="pageSize"
      :total-items="filtered.length"
    />

    <p v-if="!wipPending && wipRows.length >= filters.take" class="text-xs text-muted-foreground">
      已加载前 {{ filters.take }} 行在制（后端返回上限），使用搜索或状态筛选定位更多在制行。
    </p>
  </BusinessLayout>
</template>
