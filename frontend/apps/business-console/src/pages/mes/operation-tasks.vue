<script setup lang="ts">
import BusinessContextBar from '@/components/business/BusinessContextBar.vue'
import BusinessEmptyState from '@/components/business/BusinessEmptyState.vue'
import BusinessFormStatus from '@/components/business/BusinessFormStatus.vue'
import BusinessMetricCell from '@/components/business/BusinessMetricCell.vue'
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import BusinessRowActions from '@/components/business/BusinessRowActions.vue'
import BusinessStatusBadge from '@/components/business/BusinessStatusBadge.vue'
import { useBusinessMasterDataResources } from '@/composables/useBusinessMasterData'
import { useMesOperationTasks } from '@/composables/useBusinessMes'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import type { BusinessConsoleMesOperationTaskRow, BusinessConsoleResourceItem } from '@nerv-iip/api-client'
import {
  Button,
  DropdownMenuItem,
  DropdownMenuSeparator,
  Field,
  FieldGroup,
  FieldLabel,
  Input,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
  Table,
  TableBody,
  TableCell,
  TableEmpty,
  TableHead,
  TableHeader,
  TableRow,
} from '@nerv-iip/ui'
import { ArrowDownIcon, ArrowUpDownIcon, ArrowUpIcon, ClipboardCheckIcon, EyeIcon, PlayCircleIcon, RefreshCwIcon, ShieldCheckIcon, WrenchIcon } from 'lucide-vue-next'
import { computed, reactive, watch } from 'vue'
import { useRouter } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '工序执行',
  },
})

const {
  filters,
  operationTasks,
  operationTasksError,
  operationTasksPending,
  refreshOperationTasks,
} = useMesOperationTasks()

const router = useRouter()
const { resources: siteResources } = useBusinessMasterDataResources('site')
const { resources: lineResources } = useBusinessMasterDataResources('production-line')
const { resources: workCenterResources } = useBusinessMasterDataResources('work-center')
const { resources: shiftResources } = useBusinessMasterDataResources('shift')
const errorMessage = computed(() => formatError(operationTasksError.value))
type SortColumn = 'operationTaskId' | 'workOrderId' | 'status' | 'operationSequence' | 'workCenterId' | 'plannedStartUtc'

const tableState = reactive({
  keyword: '',
  page: 1,
  pageSize: '10',
  sortBy: 'plannedStartUtc' as SortColumn,
  sortDirection: 'asc' as 'asc' | 'desc',
})
const readyCount = computed(() => visibleTasks.value.filter((item) => item.status === 'Ready').length)
const runningCount = computed(() => visibleTasks.value.filter((item) => ['Running', 'Started', 'InProgress'].includes(item.status ?? '')).length)
const blockedCount = computed(() => visibleTasks.value.filter((item) => ['Blocked', 'Held'].includes(item.status ?? '')).length)
const queueCards = computed(() => [
  {
    title: '可开工',
    value: readyCount.value,
    description: '优先确认人员、设备、物料后进入报工。',
    tone: 'border-emerald-500/20 bg-emerald-500/5',
  },
  {
    title: '执行中',
    value: runningCount.value,
    description: '关注报工节拍、质量确认和异常停机。',
    tone: 'border-blue-500/20 bg-blue-500/5',
  },
  {
    title: '受阻',
    value: blockedCount.value,
    description: '需要班组长、质检或设备人员处理。',
    tone: blockedCount.value > 0 ? 'border-destructive/30 bg-destructive/5' : 'border-muted bg-muted/30',
  },
])
const executionContext = reactive({
  siteCode: '',
  lineCode: '',
  workCenterCode: '',
  shiftCode: '',
})
const statusOptions = [
  { label: '全部状态', value: 'all' },
  { label: '可开工', value: 'Ready' },
  { label: '执行中', value: 'Running' },
  { label: '暂停', value: 'Paused' },
  { label: '已完成', value: 'Completed' },
  { label: '阻塞', value: 'Blocked' },
]
const statusFilter = computed({
  get: () => filters.status || 'all',
  set: (value: string) => {
    filters.status = value === 'all' ? undefined : value
  },
})
const siteOptions = computed(() => toResourceOptions(siteResources.value))
const lineOptions = computed(() => toResourceOptions(lineResources.value))
const workCenterOptions = computed(() => toResourceOptions(workCenterResources.value))
const shiftOptions = computed(() => toResourceOptions(shiftResources.value))
const visibleTasks = computed(() => {
  const keyword = tableState.keyword.trim().toLowerCase()
  const workCenter = executionContext.workCenterCode.trim().toLowerCase()
  const shift = executionContext.shiftCode.trim().toLowerCase()

  return operationTasks.value.filter((task) => {
    const statusMatched = !filters.status || task.status === filters.status
    const keywordMatched =
      !keyword ||
      [
        task.operationTaskId,
        task.workOrderId,
        task.status,
        task.workCenterId,
        task.deviceAssetId,
        task.shiftId,
      ].some((value) => (value ?? '').toLowerCase().includes(keyword))
    const workCenterMatched = !workCenter || (task.workCenterId ?? '').toLowerCase() === workCenter
    const shiftMatched = !shift || (task.shiftId ?? '').toLowerCase() === shift

    return statusMatched && keywordMatched && workCenterMatched && shiftMatched
  })
})
const sortedTasks = computed(() => {
  const direction = tableState.sortDirection === 'asc' ? 1 : -1

  return [...visibleTasks.value].sort((left, right) => {
    const leftValue = sortValue(left, tableState.sortBy)
    const rightValue = sortValue(right, tableState.sortBy)

    if (typeof leftValue === 'number' && typeof rightValue === 'number') {
      return (leftValue - rightValue) * direction
    }

    return String(leftValue).localeCompare(String(rightValue), 'zh-Hans-CN') * direction
  })
})
const pageSizeNumber = computed(() => Number(tableState.pageSize) || 10)
const totalPages = computed(() => Math.max(1, Math.ceil(sortedTasks.value.length / pageSizeNumber.value)))
const pagedTasks = computed(() => {
  const start = (tableState.page - 1) * pageSizeNumber.value
  return sortedTasks.value.slice(start, start + pageSizeNumber.value)
})
const paginationSummary = computed(() => {
  if (!sortedTasks.value.length) return '0 条'
  const start = (tableState.page - 1) * pageSizeNumber.value + 1
  const end = Math.min(tableState.page * pageSizeNumber.value, sortedTasks.value.length)
  return `${start}-${end} / ${sortedTasks.value.length} 条`
})

watch(
  () => [
    tableState.keyword,
    tableState.pageSize,
    filters.status,
    executionContext.workCenterCode,
    executionContext.shiftCode,
    operationTasks.value.length,
  ],
  () => {
    tableState.page = 1
  },
)

function openWorkOrder(workOrderId?: string | null) {
  if (!workOrderId) return
  void router.push({ path: `/mes/work-orders/${encodeURIComponent(workOrderId)}` })
}

function openRoute(path: string, task?: { operationTaskId?: string | null, workOrderId?: string | null, workCenterId?: string | null }) {
  void router.push({
    path,
    query: task
      ? {
          operationTaskId: task.operationTaskId ?? undefined,
          workOrderId: task.workOrderId ?? undefined,
          workCenterId: task.workCenterId ?? undefined,
        }
      : undefined,
  })
}

function formatDateTime(value?: string | null) {
  if (!value) return '无'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}

function setSort(column: SortColumn) {
  if (tableState.sortBy === column) {
    tableState.sortDirection = tableState.sortDirection === 'asc' ? 'desc' : 'asc'
    return
  }

  tableState.sortBy = column
  tableState.sortDirection = 'asc'
}

function sortIcon(column: SortColumn) {
  if (tableState.sortBy !== column) return ArrowUpDownIcon
  return tableState.sortDirection === 'asc' ? ArrowUpIcon : ArrowDownIcon
}

function sortValue(task: BusinessConsoleMesOperationTaskRow, column: SortColumn) {
  if (column === 'operationSequence') return task.operationSequence ?? 0
  if (column === 'plannedStartUtc') return task.plannedStartUtc ? new Date(task.plannedStartUtc).getTime() : 0
  return task[column] ?? ''
}

function previousPage() {
  tableState.page = Math.max(1, tableState.page - 1)
}

function nextPage() {
  tableState.page = Math.min(totalPages.value, tableState.page + 1)
}

function toResourceOptions(items: BusinessConsoleResourceItem[]) {
  return items
    .filter((item) => item.active !== false && item.code)
    .map((item) => ({
      label: item.displayName ? `${item.displayName} (${item.code})` : item.code!,
      value: item.code!,
    }))
}

function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败。' : ''
}
</script>

<template>
  <BusinessLayout>
    <section class="grid gap-4">
      <BusinessPageHeader
        domain="MES"
        title="工序执行"
        kicker="班组长 / 操作员"
        summary="以工序任务为现场工作单元，直接进入工单、报工、质检和异常处理，减少一线人员在系统里反复查找编号。"
      >
        <template #actions>
          <Button size="sm" type="button" variant="outline" :disabled="operationTasksPending" @click="refreshOperationTasks">
            <RefreshCwIcon data-icon="inline-start" />
            刷新
          </Button>
        </template>
      </BusinessPageHeader>

      <BusinessContextBar
        v-model:environment-id="filters.environmentId"
        v-model:line-code="executionContext.lineCode"
        v-model:organization-id="filters.organizationId"
        v-model:shift-code="executionContext.shiftCode"
        v-model:site-code="executionContext.siteCode"
        v-model:work-center-code="executionContext.workCenterCode"
        :line-options="lineOptions"
        :shift-options="shiftOptions"
        :site-options="siteOptions"
        title="生产范围"
        :work-center-options="workCenterOptions"
      >
        <FieldGroup class="grid gap-3 md:grid-cols-4">
          <Field>
            <FieldLabel for="operation-keyword">搜索</FieldLabel>
            <Input id="operation-keyword" v-model="tableState.keyword" placeholder="任务、工单、设备" />
          </Field>
          <Field>
            <FieldLabel for="operation-status">状态</FieldLabel>
            <Select v-model="statusFilter">
              <SelectTrigger id="operation-status" aria-label="工序状态">
                <SelectValue placeholder="全部状态" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem v-for="option in statusOptions" :key="option.value" :value="option.value">
                  {{ option.label }}
                </SelectItem>
              </SelectContent>
            </Select>
          </Field>
          <Field>
            <FieldLabel for="operation-take">加载数量</FieldLabel>
            <Input id="operation-take" v-model.number="filters.take" inputmode="numeric" type="number" />
          </Field>
          <Field>
            <FieldLabel for="operation-page-size">每页显示</FieldLabel>
            <Select v-model="tableState.pageSize">
              <SelectTrigger id="operation-page-size">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="10">10 条</SelectItem>
                <SelectItem value="20">20 条</SelectItem>
                <SelectItem value="50">50 条</SelectItem>
              </SelectContent>
            </Select>
          </Field>
        </FieldGroup>
        <BusinessFormStatus :error="errorMessage" />
      </BusinessContextBar>

      <div class="grid gap-3 lg:grid-cols-3">
        <div
          v-for="card in queueCards"
          :key="card.title"
          class="grid gap-3 rounded-lg border p-4"
          :class="card.tone"
        >
          <div class="flex items-center justify-between">
            <p class="text-sm font-semibold text-foreground">{{ card.title }}</p>
            <span class="text-2xl font-semibold tabular-nums">{{ card.value }}</span>
          </div>
          <p class="text-sm leading-6 text-muted-foreground">{{ card.description }}</p>
        </div>
      </div>

      <div class="grid gap-3 md:grid-cols-3">
        <BusinessMetricCell label="任务数" :value="visibleTasks.length" detail="当前筛选结果" />
        <BusinessMetricCell label="可开工" :value="readyCount" detail="Ready 状态任务" />
        <BusinessMetricCell label="非可开工" :value="visibleTasks.length - readyCount" detail="需关注任务" />
      </div>

      <div class="overflow-hidden rounded-lg border bg-background">
        <div class="flex items-center justify-between border-b px-4 py-3">
          <h2 class="text-sm font-semibold text-foreground">工序任务列表</h2>
          <span class="text-sm text-muted-foreground">显示 {{ paginationSummary }}</span>
        </div>
        <div class="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>
                  <Button class="-ml-3" size="sm" type="button" variant="ghost" @click="setSort('operationTaskId')">
                    工序任务
                    <component :is="sortIcon('operationTaskId')" data-icon="inline-end" />
                  </Button>
                </TableHead>
                <TableHead>
                  <Button class="-ml-3" size="sm" type="button" variant="ghost" @click="setSort('workOrderId')">
                    工单
                    <component :is="sortIcon('workOrderId')" data-icon="inline-end" />
                  </Button>
                </TableHead>
                <TableHead>
                  <Button class="-ml-3" size="sm" type="button" variant="ghost" @click="setSort('status')">
                    状态
                    <component :is="sortIcon('status')" data-icon="inline-end" />
                  </Button>
                </TableHead>
                <TableHead>
                  <Button class="-ml-3" size="sm" type="button" variant="ghost" @click="setSort('operationSequence')">
                    序号
                    <component :is="sortIcon('operationSequence')" data-icon="inline-end" />
                  </Button>
                </TableHead>
                <TableHead>
                  <Button class="-ml-3" size="sm" type="button" variant="ghost" @click="setSort('workCenterId')">
                    工作中心
                    <component :is="sortIcon('workCenterId')" data-icon="inline-end" />
                  </Button>
                </TableHead>
                <TableHead>设备</TableHead>
                <TableHead>班次</TableHead>
                <TableHead>
                  <Button class="-ml-3" size="sm" type="button" variant="ghost" @click="setSort('plannedStartUtc')">
                    计划开始
                    <component :is="sortIcon('plannedStartUtc')" data-icon="inline-end" />
                  </Button>
                </TableHead>
                <TableHead>质量状态</TableHead>
                <TableHead class="text-right">操作</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow v-for="task in pagedTasks" :key="task.operationTaskId">
                <TableCell class="font-medium">{{ task.operationTaskId ?? '无编号' }}</TableCell>
                <TableCell>
                  <button
                    v-if="task.workOrderId"
                    class="font-medium text-primary underline-offset-4 hover:underline"
                    type="button"
                    @click="openWorkOrder(task.workOrderId)"
                  >
                    {{ task.workOrderId }}
                  </button>
                  <span v-else>无</span>
                </TableCell>
                <TableCell>
                  <BusinessStatusBadge :value="task.status" />
                </TableCell>
                <TableCell class="tabular-nums">{{ task.operationSequence ?? 0 }}</TableCell>
                <TableCell>{{ task.workCenterId ?? '无' }}</TableCell>
                <TableCell>{{ task.deviceAssetId ?? '未指定' }}</TableCell>
                <TableCell>{{ task.shiftId ?? '未指定' }}</TableCell>
                <TableCell>{{ formatDateTime(task.plannedStartUtc) }}</TableCell>
                <TableCell>{{ task.qualityStatus ?? '未检' }}</TableCell>
                <TableCell class="text-right">
                  <BusinessRowActions :label="`工序任务操作 ${task.operationTaskId ?? ''}`">
                    <DropdownMenuItem :disabled="!task.workOrderId" @click="openWorkOrder(task.workOrderId)">
                      <EyeIcon data-icon="inline-start" />
                      查看工单
                    </DropdownMenuItem>
                    <DropdownMenuItem @click="openRoute('/mes/reports', task)">
                      <PlayCircleIcon data-icon="inline-start" />
                      进入执行
                    </DropdownMenuItem>
                    <DropdownMenuItem @click="openRoute('/mes/reports', task)">
                      <ClipboardCheckIcon data-icon="inline-start" />
                      生产报工
                    </DropdownMenuItem>
                    <DropdownMenuSeparator />
                    <DropdownMenuItem @click="openRoute('/quality/inspections', task)">
                      <ShieldCheckIcon data-icon="inline-start" />
                      呼叫质检
                    </DropdownMenuItem>
                    <DropdownMenuItem @click="openRoute('/mes/downtime', task)">
                      <WrenchIcon data-icon="inline-start" />
                      记录异常
                    </DropdownMenuItem>
                  </BusinessRowActions>
                </TableCell>
              </TableRow>
              <TableEmpty v-if="!visibleTasks.length && !operationTasksPending" :colspan="10">
                <BusinessEmptyState
                  title="当前没有工序任务"
                  description="请检查工单释放、排程结果和工作中心筛选；可开工任务会出现在这里。"
                  action="建议先从工单与派工确认是否已经释放，再回到这里执行。"
                />
              </TableEmpty>
              <TableEmpty v-if="operationTasksPending" :colspan="10">正在加载工序任务…</TableEmpty>
            </TableBody>
          </Table>
        </div>
        <div class="flex flex-wrap items-center justify-between gap-3 border-t px-4 py-3">
          <p class="text-sm text-muted-foreground">
            第 {{ tableState.page }} / {{ totalPages }} 页
          </p>
          <div class="flex items-center gap-2">
            <Button size="sm" type="button" variant="outline" :disabled="tableState.page <= 1" @click="previousPage">
              上一页
            </Button>
            <Button size="sm" type="button" variant="outline" :disabled="tableState.page >= totalPages" @click="nextPage">
              下一页
            </Button>
          </div>
        </div>
      </div>
    </section>
  </BusinessLayout>
</template>
