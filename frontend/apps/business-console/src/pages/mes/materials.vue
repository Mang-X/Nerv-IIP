<script setup lang="ts">
import type { DataTableColumn } from '@nerv-iip/ui'
import { useMesMaterialIssueRequests } from '@/composables/useBusinessMes'
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
import { computed } from 'vue'

definePage({ meta: { requiresAuth: true, title: '齐套与物料' } })

const {
  filters,
  materialIssueRequests,
  materialIssueRequestsError,
  materialIssueRequestsPending,
  materialIssueRequestsTotal,
  refreshMaterialIssueRequests,
} = useMesMaterialIssueRequests()
const { page, pageSize } = usePagedList(filters, { resetOn: [() => filters.status] })

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
    <PageHeader title="齐套与物料" :breadcrumbs="[{ label: '制造执行' }]" :count="`${materialIssueRequestsTotal} 条领料申请`">
      <template #actions>
        <Button size="sm" type="button" variant="outline" :disabled="materialIssueRequestsPending" @click="refreshMaterialIssueRequests">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
      </template>
    </PageHeader>

    <SectionCards :columns="3">
      <SectionCard description="领料申请" :value="materialIssueRequestsTotal" hint="后端筛选总数" />
      <SectionCard description="本页待处理" :value="openCount" hint="当前页统计" />
      <SectionCard description="本页已关闭" :value="closedCount" hint="当前页统计" />
    </SectionCards>

    <Toolbar :show-search="false">
      <template #filters>
        <Input v-model="filters.status" class="h-9 w-32" placeholder="状态（可选）" aria-label="领料状态" />
      </template>
    </Toolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <DataTable
      :columns="columns"
      :rows="materialIssueRequests"
      row-key="requestId"
      :loading="materialIssueRequestsPending"
      empty-message="暂无领料申请。齐套检查通过后从工单详情发起领料。"
    >
      <template #cell-status="{ row }"><StatusBadge :value="row.status" /></template>
      <template #cell-requestedAtUtc="{ row }">{{ formatDateTime(row.requestedAtUtc) }}</template>
    </DataTable>

    <DataTablePagination v-model:page="page" v-model:page-size="pageSize" :total-items="materialIssueRequestsTotal" />
  </BusinessLayout>
</template>
