<script setup lang="ts">
import type {
  BusinessConsoleWmsInboundOrderItem,
  BusinessConsoleWmsOutboundOrderItem,
  BusinessConsoleWmsWcsTaskItem,
} from '@nerv-iip/api-client'
import type { DataTableColumn } from '@nerv-iip/ui'
import { useWmsInboundOrders, useWmsOutboundOrders, useWmsWcsTasks } from '@/composables/useBusinessWms'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Button,
  DataTable,
  DataTablePagination,
  PageHeader,
  SectionCard,
  SectionCards,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
  StatusBadge,
  Toolbar,
} from '@nerv-iip/ui'
import { watchDebounced } from '@vueuse/core'
import { RefreshCwIcon } from 'lucide-vue-next'
import { computed, shallowRef, watch } from 'vue'

definePage({ meta: { requiresAuth: true, title: '仓储作业' } })

const {
  filters: inboundFilters,
  inboundOrders,
  inboundOrdersError,
  inboundOrdersPending,
  inboundOrdersTotal,
  refreshInboundOrders,
} = useWmsInboundOrders()
const {
  filters: outboundFilters,
  outboundOrders,
  outboundOrdersError,
  outboundOrdersPending,
  outboundOrdersTotal,
  refreshOutboundOrders,
} = useWmsOutboundOrders()
const {
  filters: wcsFilters,
  wcsTasks,
  wcsTasksError,
  wcsTasksPending,
  wcsTasksTotal,
  refreshWcsTasks,
} = useWmsWcsTasks()

const inboundKeyword = shallowRef('')
const inboundStatus = shallowRef('all')
const outboundKeyword = shallowRef('')
const outboundStatus = shallowRef('all')
const wcsKeyword = shallowRef('')
const wcsStatus = shallowRef('all')
const wcsFailed = shallowRef('all')

const { page: inboundPage, pageSize: inboundPageSize } = usePagedList(inboundFilters, { resetOn: [inboundKeyword, inboundStatus] })
const { page: outboundPage, pageSize: outboundPageSize } = usePagedList(outboundFilters, { resetOn: [outboundKeyword, outboundStatus] })
const { page: wcsPage, pageSize: wcsPageSize } = usePagedList(wcsFilters, { resetOn: [wcsKeyword, wcsStatus, wcsFailed] })

watch(inboundStatus, (value) => {
  inboundFilters.status = value === 'all' ? undefined : value
})
watch(outboundStatus, (value) => {
  outboundFilters.status = value === 'all' ? undefined : value
})
watch(wcsStatus, (value) => {
  wcsFilters.status = value === 'all' ? undefined : value
})
watch(wcsFailed, (value) => {
  wcsFilters.failed = value === 'all' ? undefined : value === 'failed'
})
watchDebounced(inboundKeyword, (value) => {
  inboundFilters.keyword = value.trim() || undefined
}, { debounce: 300, maxWait: 1000 })
watchDebounced(outboundKeyword, (value) => {
  outboundFilters.keyword = value.trim() || undefined
}, { debounce: 300, maxWait: 1000 })
watchDebounced(wcsKeyword, (value) => {
  wcsFilters.keyword = value.trim() || undefined
}, { debounce: 300, maxWait: 1000 })

const orderStatusOptions = [
  { label: '全部状态', value: 'all' },
  { label: '进行中', value: 'Open' },
  { label: '已完成', value: 'Completed' },
]
const wcsStatusOptions = [
  { label: '全部状态', value: 'all' },
  { label: '已下发', value: 'Dispatched' },
  { label: '已完成', value: 'Completed' },
  { label: '失败', value: 'Failed' },
]

const inboundOpenCount = computed(() => countByStatus(inboundOrders.value, 'Open'))
const outboundOpenCount = computed(() => countByStatus(outboundOrders.value, 'Open'))
const wcsFailedCount = computed(() => wcsTasks.value.filter((task) => task.failedAtUtc || task.status === 'Failed').length)
const pageBusy = computed(() => inboundOrdersPending.value || outboundOrdersPending.value || wcsTasksPending.value)
const pageError = computed(() => formatError(inboundOrdersError.value || outboundOrdersError.value || wcsTasksError.value))

const inboundColumns: DataTableColumn<BusinessConsoleWmsInboundOrderItem>[] = [
  { key: 'inboundOrderNo', header: '入库单号', cellClass: 'font-medium', accessor: (row) => row.inboundOrderNo ?? row.inboundOrderId ?? '无' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'createdAtUtc', header: '创建时间', accessor: (row) => formatDateTime(row.createdAtUtc) },
]
const outboundColumns: DataTableColumn<BusinessConsoleWmsOutboundOrderItem>[] = [
  { key: 'outboundOrderNo', header: '出库单号', cellClass: 'font-medium', accessor: (row) => row.outboundOrderNo ?? row.outboundOrderId ?? '无' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'createdAtUtc', header: '创建时间', accessor: (row) => formatDateTime(row.createdAtUtc) },
]
const wcsColumns: DataTableColumn<BusinessConsoleWmsWcsTaskItem>[] = [
  { key: 'externalTaskId', header: '外部任务', cellClass: 'font-medium', accessor: (row) => row.externalTaskId ?? row.wcsTaskId ?? '无' },
  { key: 'warehouseTaskId', header: '仓库任务', accessor: (row) => row.warehouseTaskId ?? '无' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'attemptCount', header: '次数', align: 'end', width: 'w-16', accessor: (row) => row.attemptCount ?? 0 },
  { key: 'failureCode', header: '失败原因', accessor: (row) => row.failureCode ?? '无' },
  { key: 'dispatchedAtUtc', header: '下发时间', accessor: (row) => formatDateTime(row.dispatchedAtUtc) },
]

function refreshAll() {
  void refreshInboundOrders()
  void refreshOutboundOrders()
  void refreshWcsTasks()
}
function resetInboundFilters() {
  inboundKeyword.value = ''
  inboundStatus.value = 'all'
}
function resetOutboundFilters() {
  outboundKeyword.value = ''
  outboundStatus.value = 'all'
}
function resetWcsFilters() {
  wcsKeyword.value = ''
  wcsStatus.value = 'all'
  wcsFailed.value = 'all'
}
function countByStatus(rows: Array<{ status?: string | null }>, status: string) {
  return rows.filter((row) => row.status === status).length
}
function formatDateTime(value?: string | null) {
  if (!value) return '无'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
</script>

<template>
  <BusinessLayout>
    <PageHeader title="仓储作业" :breadcrumbs="[{ label: '供应链执行' }]" :count="`${inboundOrdersTotal + outboundOrdersTotal + wcsTasksTotal} 条仓储记录`">
      <template #actions>
        <Button size="sm" type="button" variant="outline" :disabled="pageBusy" @click="refreshAll">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
      </template>
    </PageHeader>

    <SectionCards :columns="4">
      <SectionCard description="入库总数" :value="inboundOrdersTotal" hint="后端筛选总数" />
      <SectionCard description="出库总数" :value="outboundOrdersTotal" hint="后端筛选总数" />
      <SectionCard description="WCS 任务" :value="wcsTasksTotal" hint="后端筛选总数" />
      <SectionCard description="本页异常" :value="wcsFailedCount" hint="当前页失败任务" />
    </SectionCards>

    <p v-if="pageError" class="text-sm text-destructive" role="alert">{{ pageError }}</p>

    <section class="grid gap-3">
      <div class="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h2 class="text-lg font-semibold text-foreground">收货入库</h2>
          <p class="text-sm text-muted-foreground">本页进行中 {{ inboundOpenCount }} 单</p>
        </div>
        <Toolbar v-model:search="inboundKeyword" search-placeholder="搜索入库单号">
          <template #filters>
            <Select v-model="inboundStatus">
              <SelectTrigger class="h-9 w-32" aria-label="入库状态"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem v-for="option in orderStatusOptions" :key="option.value" :value="option.value">{{ option.label }}</SelectItem>
              </SelectContent>
            </Select>
          </template>
          <template #actions>
            <Button type="button" variant="ghost" size="sm" @click="resetInboundFilters">重置</Button>
          </template>
        </Toolbar>
      </div>
      <DataTable
        :columns="inboundColumns"
        :rows="inboundOrders"
        :row-key="(row) => row.inboundOrderId ?? row.inboundOrderNo ?? '无'"
        :loading="inboundOrdersPending"
        empty-message="当前没有收货入库记录。"
      >
        <template #cell-status="{ row }"><StatusBadge :value="row.status" /></template>
      </DataTable>
      <DataTablePagination v-model:page="inboundPage" v-model:page-size="inboundPageSize" :total-items="inboundOrdersTotal" />
    </section>

    <section class="grid gap-3">
      <div class="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h2 class="text-lg font-semibold text-foreground">出库发货</h2>
          <p class="text-sm text-muted-foreground">本页进行中 {{ outboundOpenCount }} 单</p>
        </div>
        <Toolbar v-model:search="outboundKeyword" search-placeholder="搜索出库单号">
          <template #filters>
            <Select v-model="outboundStatus">
              <SelectTrigger class="h-9 w-32" aria-label="出库状态"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem v-for="option in orderStatusOptions" :key="option.value" :value="option.value">{{ option.label }}</SelectItem>
              </SelectContent>
            </Select>
          </template>
          <template #actions>
            <Button type="button" variant="ghost" size="sm" @click="resetOutboundFilters">重置</Button>
          </template>
        </Toolbar>
      </div>
      <DataTable
        :columns="outboundColumns"
        :rows="outboundOrders"
        :row-key="(row) => row.outboundOrderId ?? row.outboundOrderNo ?? '无'"
        :loading="outboundOrdersPending"
        empty-message="当前没有出库发货记录。"
      >
        <template #cell-status="{ row }"><StatusBadge :value="row.status" /></template>
      </DataTable>
      <DataTablePagination v-model:page="outboundPage" v-model:page-size="outboundPageSize" :total-items="outboundOrdersTotal" />
    </section>

    <section class="grid gap-3">
      <div class="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h2 class="text-lg font-semibold text-foreground">WCS 任务</h2>
          <p class="text-sm text-muted-foreground">本页异常 {{ wcsFailedCount }} 个</p>
        </div>
        <Toolbar v-model:search="wcsKeyword" search-placeholder="搜索 WCS 任务">
          <template #filters>
            <Select v-model="wcsStatus">
              <SelectTrigger class="h-9 w-32" aria-label="WCS 状态"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem v-for="option in wcsStatusOptions" :key="option.value" :value="option.value">{{ option.label }}</SelectItem>
              </SelectContent>
            </Select>
            <Select v-model="wcsFailed">
              <SelectTrigger class="h-9 w-32" aria-label="异常标记"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="all">全部任务</SelectItem>
                <SelectItem value="failed">仅异常</SelectItem>
                <SelectItem value="normal">非异常</SelectItem>
              </SelectContent>
            </Select>
          </template>
          <template #actions>
            <Button type="button" variant="ghost" size="sm" @click="resetWcsFilters">重置</Button>
          </template>
        </Toolbar>
      </div>
      <DataTable
        :columns="wcsColumns"
        :rows="wcsTasks"
        :row-key="(row) => row.wcsTaskId ?? row.externalTaskId ?? '无'"
        :loading="wcsTasksPending"
        empty-message="当前没有 WCS 任务。"
      >
        <template #cell-status="{ row }"><StatusBadge :value="row.status" /></template>
      </DataTable>
      <DataTablePagination v-model:page="wcsPage" v-model:page-size="wcsPageSize" :total-items="wcsTasksTotal" />
    </section>
  </BusinessLayout>
</template>
