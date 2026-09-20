<script setup lang="ts">
import type {
  BusinessConsoleMesAndonCallResponse,
  ListBusinessConsoleMesAndonCallsData,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import {
  NvButton,
  NvDataTable,
  NvPageHeader,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvStatusBadge,
  NvToolbar,
  NvInput,
} from '@nerv-iip/ui'
import { RefreshCwIcon } from '@lucide/vue'
import { computed, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useMesAndon, type AndonAction } from '@/composables/mes/useMesAndon'
import MesWorkScopeSelect from '@/components/mes/MesWorkScopeSelect.vue'
import { useAuthStore } from '@/stores/auth'
import { formatDateTime } from '@/utils/format'
import { inlineErrorMessage, notifyOperationFailure, notifySuccess } from '@/utils/notify'

const categories = { materialShortage: '缺料', equipment: '设备', quality: '质量', process: '工艺' }
const route = useRoute()
const router = useRouter()
function filtersFromRoute(): Partial<ListBusinessConsoleMesAndonCallsData['query']> {
  const q = route.query
  const take = [10, 20, 50, 100].includes(Number(q.pageSize)) ? Number(q.pageSize) : 10
  const currentPage =
    Number.isSafeInteger(Number(q.page)) && Number(q.page) > 0 ? Number(q.page) : 1
  return {
    queue: q.queue === 'unclosed' || q.queue === 'all' ? q.queue : ('awaitingResponse' as const),
    category:
      typeof q.category === 'string' && q.category in categories
        ? (q.category as keyof typeof categories)
        : undefined,
    workCenterId:
      typeof q.workCenterId === 'string' ? q.workCenterId.trim() || undefined : undefined,
    skip: (currentPage - 1) * take,
    take,
  }
}
const { filters, scope, items, total, ready, error, pending, pendingAction, refresh, canAct, act } =
  useMesAndon(filtersFromRoute())
const page = computed({
  get: () => filters.skip / filters.take + 1,
  set: (value: number) => {
    filters.skip = (value - 1) * filters.take
  },
})
const pageSize = computed({
  get: () => String(filters.take),
  set: (value: string) => {
    filters.take = Number(value)
    filters.skip = 0
  },
})
function changeFilters(patch: Partial<typeof filters>) {
  Object.assign(filters, patch, { skip: 0 })
}
const hasFilters = computed(
  () => filters.queue !== 'awaitingResponse' || !!filters.category || !!filters.workCenterId,
)
function clearFilters() {
  changeFilters({ queue: 'awaitingResponse', category: undefined, workCenterId: undefined })
}
watch(
  () => route.query,
  () => {
    if (route.path === '/mes/andon') Object.assign(filters, filtersFromRoute())
  },
)
watch(
  () => [filters.queue, filters.category, filters.workCenterId, filters.skip, filters.take],
  () => {
    if (route.path !== '/mes/andon') return
    const query = { ...route.query }
    for (const key of ['queue', 'category', 'workCenterId', 'page', 'pageSize']) delete query[key]
    if (filters.queue !== 'awaitingResponse') query.queue = filters.queue
    if (filters.category) query.category = filters.category
    if (filters.workCenterId) query.workCenterId = filters.workCenterId
    if (page.value !== 1) query.page = String(page.value)
    if (filters.take !== 10) query.pageSize = String(filters.take)
    void router.replace({ query })
  },
  { flush: 'post' },
)
watch(scope.scopeSelectionValue, (value, previous) => {
  if (previous && value !== previous) filters.skip = 0
})
const auth = useAuthStore()
const canReadSource = computed(() =>
  auth.principal?.permissionCodes?.includes('business.mes.work-orders.read'),
)
const statuses = { open: '待响应', claimed: '处理中', closed: '已关闭' }
const columns: NvDataTableColumn<BusinessConsoleMesAndonCallResponse>[] = [
  {
    key: 'category',
    header: '呼叫分类',
    accessor: (row) => (row.category ? categories[row.category] : '—'),
  },
  { key: 'source', header: '来源工单 / 工序' },
  { key: 'workCenterId', header: '工作中心' },
  { key: 'status', header: '状态' },
  { key: 'raisedAtUtc', header: '发起时间', accessor: (row) => formatDateTime(row.raisedAtUtc) },
  { key: 'responderId', header: '响应人', accessor: (row) => row.responderId ?? '未认领' },
  {
    key: 'responseDurationSeconds',
    header: '首次响应时长',
    accessor: (row) =>
      row.responseDurationSeconds == null ? '未响应' : `${row.responseDurationSeconds} 秒`,
  },
  { key: 'escalation', header: '升级' },
  { key: 'actions', header: '操作' },
]
async function submit(row: BusinessConsoleMesAndonCallResponse, action: AndonAction) {
  try {
    await act(row, action)
    notifySuccess(action === 'claim' ? '安灯呼叫已认领。' : '安灯呼叫已关闭。')
  } catch (error) {
    notifyOperationFailure(
      action === 'claim' ? '认领失败' : '关闭失败',
      error,
      '操作失败，请刷新后重试。',
    )
  }
}
</script>

<template>
  <NvPageHeader title="安灯响应" :count="ready ? total : undefined">
    <template #actions
      ><NvButton variant="outline" :disabled="pending" @click="refresh"
        ><RefreshCwIcon class="size-4" />刷新</NvButton
      ></template
    >
  </NvPageHeader>
  <NvToolbar :show-search="false">
    <template #filters>
      <MesWorkScopeSelect
        class="shrink-0 whitespace-nowrap"
        permission-code="business.mes.operations.read"
      />
      <NvSelect
        :model-value="filters.queue"
        @update:model-value="changeFilters({ queue: $event as typeof filters.queue })"
      >
        <NvSelectTrigger aria-label="呼叫队列" class="w-48"><NvSelectValue /></NvSelectTrigger>
        <NvSelectContent>
          <NvSelectItem value="awaitingResponse">待响应</NvSelectItem>
          <NvSelectItem value="unclosed">待响应与处理中</NvSelectItem>
          <NvSelectItem value="all">全部呼叫</NvSelectItem>
        </NvSelectContent>
      </NvSelect>
      <NvSelect
        :model-value="filters.category ?? 'all'"
        @update:model-value="
          changeFilters({
            category: $event === 'all' ? undefined : ($event as keyof typeof categories),
          })
        "
      >
        <NvSelectTrigger aria-label="呼叫分类" class="w-32"><NvSelectValue /></NvSelectTrigger>
        <NvSelectContent
          ><NvSelectItem value="all">全部分类</NvSelectItem
          ><NvSelectItem v-for="(label, key) in categories" :key="key" :value="key">{{
            label
          }}</NvSelectItem></NvSelectContent
        >
      </NvSelect>
      <NvInput
        :model-value="filters.workCenterId ?? ''"
        aria-label="工作中心"
        placeholder="工作中心编号"
        class="w-48"
        @change="
          changeFilters({
            workCenterId: ($event.target as HTMLInputElement).value.trim() || undefined,
          })
        "
      />
    </template>
  </NvToolbar>
  <NvDataTable
    :columns="columns"
    :rows="items"
    row-key="id"
    :loading="pending"
    :error="error"
    :error-message="inlineErrorMessage(error, '安灯队列读取失败，请重试。')"
    empty-message="暂无安灯呼叫"
    :searchable="false"
    :client-sort="false"
    manual
    :page="page"
    :page-size="pageSize"
    :total-items="total"
    @update:page="page = $event"
    @update:page-size="pageSize = String($event)"
    @retry="refresh"
  >
    <template #empty>
      <template v-if="!scope.scopeReady.value">
        <p class="text-sm text-muted-foreground">{{ scope.scopeMessage.value }}</p>
        <NvButton variant="outline" @click="refresh">重新加载</NvButton>
      </template>
      <template v-else-if="hasFilters">
        <p class="text-sm text-muted-foreground">没有符合条件的安灯呼叫</p>
        <NvButton variant="outline" @click="clearFilters">清空筛选</NvButton>
      </template>
      <template v-else>
        <p class="text-sm text-muted-foreground">暂无待响应呼叫</p>
        <NvButton variant="outline" @click="changeFilters({ queue: 'all' })">查看全部呼叫</NvButton>
      </template>
    </template>
    <template #cell-source="{ row }">
      <div class="grid gap-1">
        <RouterLink
          v-if="canReadSource"
          :to="{
            path: `/mes/work-orders/${encodeURIComponent(row.workOrderId ?? '')}`,
            query: { operationTaskId: row.operationTaskId },
          }"
          class="text-primary underline underline-offset-4"
          >{{ row.operationTaskId }}</RouterLink
        >
        <span v-else>{{ row.operationTaskId }}</span>
        <span class="text-xs text-muted-foreground">{{ row.workOrderId }}</span>
      </div>
    </template>
    <template #cell-status="{ row }"
      ><NvStatusBadge
        :label="row.status ? statuses[row.status] : '—'"
        :tone="row.status === 'open' ? 'warning' : row.status === 'closed' ? 'neutral' : 'info'"
    /></template>
    <template #cell-escalation="{ row }">
      <div v-if="row.escalatedAtUtc" class="grid gap-1">
        <NvStatusBadge label="已升级" tone="warning" /><span class="text-xs">{{
          formatDateTime(row.escalatedAtUtc)
        }}</span
        ><span class="text-xs text-muted-foreground">{{ row.escalationRecipientId }}</span>
      </div>
      <span v-else class="text-muted-foreground">未升级</span>
    </template>
    <template #cell-actions="{ row }">
      <NvButton
        v-if="canAct(row, 'claim')"
        size="sm"
        :disabled="pendingAction !== null"
        @click="submit(row, 'claim')"
        >认领</NvButton
      >
      <NvButton
        v-else-if="canAct(row, 'close')"
        size="sm"
        variant="outline"
        :disabled="pendingAction !== null"
        @click="submit(row, 'close')"
        >关闭呼叫</NvButton
      >
      <span v-else class="text-muted-foreground">—</span>
    </template>
  </NvDataTable>
</template>
