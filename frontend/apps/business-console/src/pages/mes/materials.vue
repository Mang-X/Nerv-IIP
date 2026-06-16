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
import { ArrowUpRightIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed } from 'vue'
import { RouterLink } from 'vue-router'

definePage({ meta: { requiresAuth: true, title: '领料与齐套' } })

const {
  filters,
  materialIssueRequests,
  materialIssueRequestsError,
  materialIssueRequestsPending,
  materialIssueRequestsTotal,
  refreshMaterialIssueRequests,
} = useMesMaterialIssueRequests()
const { page, pageSize } = usePagedList(filters, { resetOn: [() => filters.status] })

// 待收料：已下发但收料未齐的领料申请——驱动「催收料」动作（非机械计数，不冒充后端总量）。
const awaitingReceiptCount = computed(
  () => materialIssueRequests.value.filter((r) => r.status !== 'Closed' && receiptShortfall(r) > 0).length,
)
const errorMessage = computed(() => formatError(materialIssueRequestsError.value))

type RequestRow = (typeof materialIssueRequests)['value'][number]
const columns: DataTableColumn<RequestRow>[] = [
  { key: 'requestId', header: '申请号', cellClass: 'font-medium', accessor: (r) => r.requestId ?? '无' },
  { key: 'workOrderId', header: '工单', accessor: (r) => r.workOrderId ?? '无' },
  { key: 'materialId', header: '物料' },
  { key: 'receivedQuantity', header: '收料进度', width: 'w-44' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'wmsRequestId', header: '出库单', width: 'w-28' },
  { key: 'requestedAtUtc', header: '申请时间', width: 'w-44' },
]

// 收料缺口 = 应领 − 已收（缺口 > 0 表示尚未收齐）。
function receiptShortfall(row: RequestRow) {
  return Math.max(0, (row.requestedQuantity ?? 0) - (row.receivedQuantity ?? 0))
}
// 收料完成度（0–1），用于进度条宽度与够料/缺料的颜色区分。
function receiptRatio(row: RequestRow) {
  const requested = row.requestedQuantity ?? 0
  if (requested <= 0) return row.receivedQuantity != null ? 1 : 0
  return Math.min(1, Math.max(0, (row.receivedQuantity ?? 0) / requested))
}
function formatQuantity(value?: number | null) {
  return new Intl.NumberFormat(undefined, { maximumFractionDigits: 3 }).format(value ?? 0)
}
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
    <PageHeader title="领料与齐套" :breadcrumbs="[{ label: '制造执行' }]" :count="`${materialIssueRequestsTotal} 条领料申请`">
      <template #actions>
        <Button size="sm" type="button" variant="outline" :disabled="materialIssueRequestsPending" @click="refreshMaterialIssueRequests">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
      </template>
    </PageHeader>

    <SectionCards :columns="3">
      <SectionCard description="待收料的领料申请" :value="awaitingReceiptCount" hint="已发起但仓库尚未收齐，需跟催出库" />
    </SectionCards>

    <Toolbar :show-search="false">
      <template #filters>
        <!-- TODO(#420): 领料状态枚举待后端确认（如 待出库/部分收料/已齐/已关闭），暂以自由文本筛选。 -->
        <Input v-model="filters.status" class="h-9 w-40" placeholder="按状态筛选（如已关闭）" aria-label="领料状态" />
      </template>
    </Toolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <DataTable
      :columns="columns"
      :rows="materialIssueRequests"
      row-key="requestId"
      :loading="materialIssueRequestsPending"
      empty-message="暂无领料申请。齐套检查通过后，从工单详情发起领料即会在此跟踪收料进度。"
    >
      <template #cell-materialId="{ row }">
        <span v-if="row.materialId">{{ row.materialId }}</span>
        <span v-else class="text-muted-foreground">—</span>
      </template>
      <template #cell-receivedQuantity="{ row }">
        <div class="flex flex-col gap-1">
          <span class="text-sm tabular-nums">
            已收 {{ formatQuantity(row.receivedQuantity) }} / 应领 {{ formatQuantity(row.requestedQuantity) }}
          </span>
          <div class="h-1.5 w-full overflow-hidden rounded-full bg-muted" role="presentation">
            <div
              class="h-full rounded-full transition-all"
              :class="receiptShortfall(row) > 0 ? 'bg-warning' : 'bg-success'"
              :style="{ width: `${Math.round(receiptRatio(row) * 100)}%` }"
            />
          </div>
        </div>
      </template>
      <template #cell-status="{ row }"><StatusBadge :value="row.status" /></template>
      <template #cell-wmsRequestId="{ row }">
        <RouterLink
          v-if="row.wmsRequestId"
          class="inline-flex items-center gap-1 text-sm font-medium text-brand hover:underline"
          :to="{ path: '/wms/outbound' }"
        >
          查看出库
          <ArrowUpRightIcon class="size-3.5" aria-hidden="true" />
        </RouterLink>
        <span v-else class="text-sm text-muted-foreground">未下发</span>
      </template>
      <template #cell-requestedAtUtc="{ row }">{{ formatDateTime(row.requestedAtUtc) }}</template>
    </DataTable>

    <DataTablePagination v-model:page="page" v-model:page-size="pageSize" :total-items="materialIssueRequestsTotal" />
  </BusinessLayout>
</template>
