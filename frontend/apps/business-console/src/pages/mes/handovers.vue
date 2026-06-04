<script setup lang="ts">
import type { DataTableColumn } from '@nerv-iip/ui'
import { useMesShiftHandovers } from '@/composables/useBusinessMes'
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

definePage({ meta: { requiresAuth: true, title: '班次交接' } })

const { filters, handovers, handoversError, handoversPending, refreshHandovers } = useMesShiftHandovers()

const keyword = ref('')
const filtered = computed(() => {
  const kw = keyword.value.trim().toLowerCase()
  if (!kw) return handovers.value
  return handovers.value.filter((r) =>
    [r.handoverId, r.shiftId, r.teamId].some((v) => (v ?? '').toLowerCase().includes(kw)),
  )
})

const openIssueTotal = computed(() => handovers.value.reduce((s, r) => s + (r.openIssueCount ?? 0), 0))
const errorMessage = computed(() => formatError(handoversError.value))

const page = ref(1)
const pageSize = ref('10')
const pageSizeNumber = computed(() => Number(pageSize.value) || 10)
const pagedRows = computed(() => {
  const start = (page.value - 1) * pageSizeNumber.value
  return filtered.value.slice(start, start + pageSizeNumber.value)
})
watch([keyword, pageSize, () => handovers.value.length], () => {
  page.value = 1
})

type HandoverRow = (typeof handovers)['value'][number]
const columns: DataTableColumn<HandoverRow>[] = [
  { key: 'handoverId', header: '交接单', cellClass: 'font-medium', accessor: (r) => r.handoverId ?? '无' },
  { key: 'shiftId', header: '班次', accessor: (r) => r.shiftId ?? '无' },
  { key: 'teamId', header: '班组', accessor: (r) => r.teamId ?? '无' },
  { key: 'handoverStatus', header: '状态', width: 'w-24' },
  { key: 'openIssueCount', header: '未结事项', align: 'end', width: 'w-24' },
  { key: 'createdAtUtc', header: '创建时间', width: 'w-44' },
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
    <PageHeader title="班次交接" :breadcrumbs="[{ label: '制造执行' }]" :count="`${filtered.length} 条交接`">
      <template #actions>
        <Button size="sm" type="button" variant="outline" :disabled="handoversPending" @click="refreshHandovers">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
      </template>
    </PageHeader>

    <SectionCards :columns="3">
      <SectionCard description="交接单" :value="handovers.length" hint="承接未完成工单与现场事项" />
      <SectionCard description="未结事项" :value="openIssueTotal" hint="待下一班次承接处理" />
      <SectionCard description="当前班次" :value="filtered.length" hint="当前筛选结果" />
    </SectionCards>

    <Toolbar v-model:search="keyword" search-placeholder="搜索交接单、班次、班组">
      <template #filters>
        <Input v-model="filters.status" class="h-9 w-32" placeholder="状态（可选）" aria-label="交接状态" />
      </template>
    </Toolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <DataTable
      :columns="columns"
      :rows="pagedRows"
      row-key="handoverId"
      :loading="handoversPending"
      empty-message="暂无班次交接。班次结束时创建交接单，记录未完成事项。"
    >
      <template #cell-handoverStatus="{ row }"><StatusBadge :value="row.handoverStatus" /></template>
      <template #cell-openIssueCount="{ row }"><span class="tabular-nums">{{ row.openIssueCount ?? 0 }}</span></template>
      <template #cell-createdAtUtc="{ row }">{{ formatDateTime(row.createdAtUtc) }}</template>
    </DataTable>

    <DataTablePagination
      v-model:page="page"
      v-model:page-size="pageSize"
      :total-items="filtered.length"
    />

    <p v-if="!handoversPending && handovers.length >= filters.take" class="text-xs text-muted-foreground">
      已加载前 {{ filters.take }} 条交接（后端返回上限），使用搜索或状态筛选定位更多交接单。
    </p>
  </BusinessLayout>
</template>
