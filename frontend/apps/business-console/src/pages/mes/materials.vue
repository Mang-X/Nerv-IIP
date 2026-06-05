<script setup lang="ts">
import type { DataTableColumn } from '@nerv-iip/ui'
import { useMesMaterialIssueRequests } from '@/composables/useBusinessMes'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Button,
  DataTable,
  Input,
  PageHeader,
  SectionCard,
  SectionCards,
  StatusBadge,
  Toolbar,
} from '@nerv-iip/ui'
import { RefreshCwIcon } from 'lucide-vue-next'
import { computed, ref } from 'vue'

definePage({ meta: { requiresAuth: true, title: '齐套与物料' } })

const {
  filters,
  materialIssueRequests,
  materialIssueRequestsError,
  materialIssueRequestsPending,
  refreshMaterialIssueRequests,
} = useMesMaterialIssueRequests()

const keyword = ref('')
const filtered = computed(() => {
  const kw = keyword.value.trim().toLowerCase()
  if (!kw) return materialIssueRequests.value
  return materialIssueRequests.value.filter((r) =>
    [r.requestId, r.workOrderId, r.wmsRequestId].some((v) => (v ?? '').toLowerCase().includes(kw)),
  )
})

const openCount = computed(() => materialIssueRequests.value.filter((x) => x.status !== 'Closed').length)
const closedCount = computed(() => materialIssueRequests.value.filter((x) => x.status === 'Closed').length)
const errorMessage = computed(() => formatError(materialIssueRequestsError.value))

type RequestRow = (typeof materialIssueRequests)['value'][number]
const columns: DataTableColumn<RequestRow>[] = [
  { key: 'requestId', header: '申请号', cellClass: 'font-medium', accessor: (r) => r.requestId ?? '无' },
  { key: 'workOrderId', header: '工单', accessor: (r) => r.workOrderId ?? '无' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'wmsRequestId', header: '仓库单号', accessor: (r) => r.wmsRequestId ?? '未下发' },
  { key: 'requestedAtUtc', header: '申请时间', width: 'w-44' },
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
    <PageHeader title="齐套与物料" :breadcrumbs="[{ label: '制造执行' }]" :count="`${filtered.length} 条领料申请`">
      <template #actions>
        <Button size="sm" type="button" variant="outline" :disabled="materialIssueRequestsPending" @click="refreshMaterialIssueRequests">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
      </template>
    </PageHeader>

    <SectionCards :columns="3">
      <SectionCard description="领料申请" :value="materialIssueRequests.length" hint="齐套 / 领料 / 线边收料" />
      <SectionCard description="待处理" :value="openCount" hint="尚未完成收料" />
      <SectionCard description="已关闭" :value="closedCount" hint="已完成收料" />
    </SectionCards>

    <Toolbar v-model:search="keyword" search-placeholder="搜索申请号、工单、仓库单号">
      <template #filters>
        <Input v-model="filters.status" class="h-9 w-32" placeholder="状态（可选）" aria-label="领料状态" />
      </template>
    </Toolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <DataTable
      :columns="columns"
      :rows="filtered"
      row-key="requestId"
      :loading="materialIssueRequestsPending"
      empty-message="暂无领料申请。齐套检查通过后从工单详情发起领料。"
    >
      <template #cell-status="{ row }"><StatusBadge :value="row.status" /></template>
      <template #cell-requestedAtUtc="{ row }">{{ formatDateTime(row.requestedAtUtc) }}</template>
    </DataTable>

    <p v-if="!materialIssueRequestsPending && materialIssueRequests.length >= filters.take" class="text-xs text-muted-foreground">
      已加载前 {{ filters.take }} 条领料申请（后端返回上限），使用搜索或状态筛选定位更多申请。
    </p>
  </BusinessLayout>
</template>
