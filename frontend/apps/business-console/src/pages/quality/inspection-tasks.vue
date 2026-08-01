<script setup lang="ts">
import type { NvDataTableColumn, NvMetricFacet } from '@nerv-iip/ui'
import type { BusinessConsoleQualityInspectionTaskItem } from '@nerv-iip/api-client'
import CodeWithNameCell from '@/components/business/CodeWithNameCell.vue'
import WorkerSelect from '@/components/masterData/WorkerSelect.vue'
import {
  useQualityInspectionTasks,
  isInspectionTaskOverdue,
} from '@/composables/useQualityInspectionTasks'
import { useSkuNames } from '@/composables/useSkuNames'
import { useBusinessWorkers } from '@/composables/useBusinessMasterData'
import { usePagedList } from '@/composables/usePagedList'
import { useQualitySkuCatalog } from '@/composables/useQualityPickerCatalog'
import ListScopeMeta from '@/components/business/ListScopeMeta.vue'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvButton,
  NvDataTable,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvEntityPicker,
  NvField,
  NvFieldLabel,
  NvInput,
  NvMetricCard,
  NvPageHeader,
} from '@nerv-iip/ui'
import { AlertCircleIcon, ArrowRightIcon, ClipboardCheckIcon, RefreshCwIcon } from '@lucide/vue'
import {
  inlineErrorMessage,
  isForbiddenError,
  notifyError,
  notifyOperationFailure,
  notifySuccess,
} from '@/utils/notify'
import { computed, ref, shallowRef, watch } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '待检工作台',
    requiredPermissions: ['business.quality.inspection-records.read'],
  },
})

const route = useRoute()
const router = useRouter()
const initialSourceDocumentNo = firstQuery(route.query.sourceDocumentNo)
const initialInspectionTaskId = firstQuery(route.query.inspectionTaskId)
const {
  filters,
  hasLocator,
  tasks,
  total,
  pending,
  error,
  refreshTasks,
  lastUpdatedAt,
  hasSuccessfulResponse: tasksHasSuccessfulResponse,
  hasFailedResponse: tasksHasFailedResponse,
  claimInspectionTask,
  assignInspectionTask,
} = useQualityInspectionTasks({
  status: 'pending',
  ...(initialSourceDocumentNo ? { sourceDocumentNo: initialSourceDocumentNo } : {}),
  ...(initialInspectionTaskId ? { inspectionTaskId: initialInspectionTaskId } : {}),
})
// 待检任务只回 SKU 编码，物料名在主数据里；查不到就只显编码，不编造物料名。
const { resolveSkuName } = useSkuNames()
const { workers } = useBusinessWorkers({ pageSize: 500 })
const workerByUserId = computed(
  () =>
    new Map(
      workers.value.filter((worker) => worker.userId).map((worker) => [worker.userId, worker]),
    ),
)
const { page, pageSize } = usePagedList(filters, {
  initialPageSize: '200',
  resetOn: [() => filters.sourceType, () => filters.skuCode],
})

const sourceTabs = [
  { label: '全部来源', value: 'all' as const },
  { label: '来料', value: 'receiving' as const },
  { label: '过程', value: 'operation' as const },
  { label: '终检', value: 'final' as const },
]

// 物料筛选只选不填：待检任务按 SKU 编码过滤，敲错一个字符就是空列表。
const skuCatalog = useQualitySkuCatalog()
const skuModel = computed({
  get: () => filters.skuCode ?? '',
  set: (value: string) => {
    filters.skuCode = value.trim() ? value : undefined
  },
})

const listErrorMessage = computed(() => formatError(error.value))
const today = new Date()
const overdueCount = computed(
  () => tasks.value.filter((task) => isInspectionTaskOverdue(task, today)).length,
)
/** 待检堆在哪个环节，是班组长决定先派谁去检的依据。 */
const sourceFacets = computed<NvMetricFacet[]>(() =>
  sourceTabs
    .filter((tab) => tab.value !== 'all')
    .map((tab) => ({
      key: tab.value,
      label: tab.label,
      value: tasks.value.filter((task) => task.sourceType === tab.value).length,
    })),
)
/**
 * 按时率＝当前待检任务里未超期的占比，目标 100%。
 * 没有待检任务时分母为 0，不存在"按时率"这回事——返回 null 走无样本态，
 * 绝不把空集合画成 100% 满绿（那会让人误以为刚刚检完一批）。
 */
const onTimeRate = computed(() => {
  const loaded = tasks.value.length
  if (loaded === 0) return null
  return Math.round(((loaded - overdueCount.value) / loaded) * 100)
})
const locatorMessage = computed(() => {
  if (filters.sourceDocumentNo) return `正在定位收货单 ${filters.sourceDocumentNo} 的待检任务`
  if (filters.inspectionTaskId) return `正在定位待检任务 ${filters.inspectionTaskId}`
  return ''
})
const emptyMessage = computed(() =>
  locatorMessage.value
    ? `${locatorMessage.value.replace('正在定位', '未找到')}。请确认来源单据已生成待检任务，或清除定位条件后查看全部任务。`
    : '当前没有待检任务。免检 SKU 不会生成任务；若刚完成收货或报工，请刷新后再查看。',
)
const scopeHint = computed(() =>
  locatorMessage.value
    ? `共定位到 ${total.value} 个待检任务。`
    : filters.sourceType === 'all'
      ? `服务总数 ${total.value} 个待检任务。`
      : `本页匹配 ${tasks.value.length} 个 / 服务总数 ${total.value} 个；后续页面可能还有匹配任务。`,
)
const sourceTypeHint = computed(() =>
  filters.sourceType === 'all'
    ? '质检待检任务服务（组织/环境范围，状态：待检）'
    : `质检待检任务服务（组织/环境范围，状态：待检；${sourceLabel(filters.sourceType)}筛选仅按当前页匹配）`,
)
const scopeText = computed(() =>
  filters.organizationId && filters.environmentId
    ? '当前登录组织 / 当前业务环境'
    : '组织/环境范围未就绪',
)
const emptyExplanation = computed(() =>
  !filters.organizationId || !filters.environmentId
    ? '缺少组织或环境范围，未发起查询。'
    : filters.sourceType !== 'all'
      ? `当前页没有符合“${sourceLabel(filters.sourceType)}”的任务；服务总数为 ${total.value}，后续页面可能还有匹配任务。`
      : '当前列表为组织范围的待检任务，暂不支持按检验人员筛选；空态不代表个人待检。',
)

const claimPendingTaskId = shallowRef('')
const assignmentDialogOpen = ref(false)
const assignmentTask = shallowRef<BusinessConsoleQualityInspectionTaskItem>()
const assignmentTargetUserId = ref('')
const assignmentReason = ref('')
const assignmentPending = ref(false)

function inspectionTaskHolderLabel(task: BusinessConsoleQualityInspectionTaskItem) {
  const worker = task.assignedInspectorUserId
    ? workerByUserId.value.get(task.assignedInspectorUserId.trim())
    : undefined
  const displayName = worker?.displayName?.trim()
  const employeeNo = worker?.employeeNo?.trim()
  return displayName && employeeNo ? `${displayName} · ${employeeNo}` : '—'
}

function isTaskClaimedByAnother(task: BusinessConsoleQualityInspectionTaskItem) {
  return task.blockReasons?.includes('task-already-claimed') ?? false
}

function openReassignment(task: BusinessConsoleQualityInspectionTaskItem) {
  assignmentTask.value = task
  assignmentTargetUserId.value = ''
  assignmentReason.value = ''
  assignmentDialogOpen.value = true
}

async function submitReassignment() {
  const task = assignmentTask.value
  const inspectionTaskId = task?.inspectionTaskId?.trim()
  const targetUserId = assignmentTargetUserId.value.trim()
  const reason = assignmentReason.value.trim()
  if (!task || !inspectionTaskId || !targetUserId) {
    notifyError(new Error('请选择改派对象'), '请选择改派对象。')
    return
  }
  if (!reason) {
    notifyError(new Error('请填写改派原因'), '请填写改派原因。')
    return
  }

  assignmentPending.value = true
  try {
    await assignInspectionTask(inspectionTaskId, targetUserId, reason, task.version ?? 0)
    notifySuccess('待检任务已改派。')
    assignmentDialogOpen.value = false
    await refreshTasks()
  } catch (error) {
    notifyOperationFailure('改派失败', error, '任务状态已变化，请刷新待检工作台后重试。')
  } finally {
    assignmentPending.value = false
  }
}

watch(
  () =>
    [firstQuery(route.query.sourceDocumentNo), firstQuery(route.query.inspectionTaskId)] as const,
  ([sourceDocumentNo, inspectionTaskId]) => {
    filters.sourceDocumentNo = sourceDocumentNo || undefined
    filters.inspectionTaskId = inspectionTaskId || undefined
    filters.skip = 0
  },
)

const columns: NvDataTableColumn<BusinessConsoleQualityInspectionTaskItem>[] = [
  {
    key: 'sourceDocumentId',
    header: '来源引用',
    width: 'w-40',
    accessor: (row) => row.sourceDocumentId ?? '—',
  },
  {
    key: 'sourceType',
    header: '来源类型',
    width: 'w-24',
    accessor: (row) => sourceLabel(row.sourceType),
  },
  { key: 'skuCode', header: '物料', width: 'w-44' },
  {
    key: 'assignedInspectorUserId',
    header: '当前持有人',
    width: 'w-36',
    accessor: (row) => inspectionTaskHolderLabel(row),
  },
  {
    key: 'createdAtUtc',
    header: '生成时间',
    width: 'w-40',
    accessor: (row) => formatDateTime(row.createdAtUtc),
  },
  {
    key: 'dueAtUtc',
    header: '时限',
    width: 'w-36',
    accessor: (row) => formatDateTime(row.dueAtUtc),
  },
  {
    key: 'actions',
    header: '操作',
    width: 'w-32',
    headerClass: 'sticky right-0 z-20 bg-card shadow-[-1px_0_0_0_var(--border)]',
    cellClass: 'sticky right-0 z-10 bg-card shadow-[-1px_0_0_0_var(--border)]',
    accessor: () => '',
  },
]

function sourceLabel(value?: string | null) {
  return sourceTabs.find((tab) => tab.value === value)?.label ?? '其他来源'
}

function firstQuery(value: unknown) {
  const text = Array.isArray(value) ? value[0] : value
  return typeof text === 'string' ? text.trim() : ''
}

function formatDateTime(value?: string | null) {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return '—'
  return new Intl.DateTimeFormat('zh-CN', { dateStyle: 'short', timeStyle: 'short' }).format(date)
}

/**
 * 加载失败的行内文案。**按状态码判 403**，不能按 `error instanceof Error` 判：
 * generated client 在 `throwOnError` 下抛的是解析后的响应体对象，那条判定对真实 403 永远不成立，
 * 「无权限」空态会退化成普通失败态（MAN-698 台账 / #1298 规格轴）。
 * 其余失败交给 `inlineErrorMessage` 走分层透传（服务端中文理由原样上屏、英文 HTTP 文案映射人话）。
 */
function formatError(errorValue: unknown) {
  if (!errorValue) return ''
  if (isForbiddenError(errorValue)) {
    return '当前账号没有查看质检待检任务的权限，请联系管理员申请质量模块权限。'
  }
  return inlineErrorMessage(errorValue, '待检任务加载失败，请稍后重试。')
}

function sourceDocumentRoute(task: BusinessConsoleQualityInspectionTaskItem) {
  const sourceService = task.sourceService?.trim().toLowerCase()
  if (task.sourceType === 'receiving') return sourceService === 'wms' ? '/wms/inbound' : ''
  if (sourceService !== 'mes') return ''
  const workOrderId =
    task.sourceType === 'final' ? task.sourceDocumentLineId : task.sourceDocumentId
  return workOrderId ? `/mes/work-orders/${encodeURIComponent(workOrderId)}` : ''
}

// 方案选择：让“开始检验”先 claim 成功再打开表单；这样按钮语义与后端状态一致，不会留下“看似接手、实际未认领”的中间态。
async function goToInspectionForm(task: BusinessConsoleQualityInspectionTaskItem) {
  const inspectionTaskId = task.inspectionTaskId?.trim()
  if (!inspectionTaskId) return
  if (!task.allowedActions?.includes('claim')) {
    if (isTaskClaimedByAnother(task)) openReassignment(task)
    return
  }

  claimPendingTaskId.value = inspectionTaskId
  try {
    await claimInspectionTask(inspectionTaskId, task.version ?? 0)
    await router.push({
      path: '/quality/inspections',
      query: {
        inspectionTaskId,
        sourceDocumentId: task.sourceDocumentId ?? undefined,
        sourceType: task.sourceType ?? undefined,
        sourceService: task.sourceService ?? undefined,
        skuCode: task.skuCode ?? undefined,
        inspectionPlanId: task.inspectionPlanId ?? undefined,
        quantity: task.quantity?.toString() ?? undefined,
        batchNo: task.batchNo ?? undefined,
        serialNo: task.serialNo ?? undefined,
        action: 'create',
      },
    })
  } catch (error) {
    notifyOperationFailure('认领失败', error, '任务刚被其他检验员认领，请刷新列表后使用“改派”。')
    await refreshTasks()
  } finally {
    claimPendingTaskId.value = ''
  }
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="质检待检工作台"
      :breadcrumbs="[{ label: '质量管理' }]"
      :count="`${total} 个待检任务`"
    >
      <template #actions>
        <NvButton size="sm" variant="outline" :disabled="pending" @click="refreshTasks">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
      <NvMetricCard
        variant="facets"
        label="待检任务"
        :value="total"
        unit="个"
        :facets="sourceFacets"
      />
      <NvMetricCard
        variant="alert"
        label="已超期"
        :value="overdueCount"
        unit="个"
        :tone="overdueCount > 0 ? 'danger' : 'neutral'"
        :status="
          overdueCount > 0
            ? { label: '需优先处理', tone: 'danger' }
            : { label: '无超期', tone: 'success' }
        "
        :foot-start="
          overdueCount > 0
            ? '超期任务已在下方列表置顶，先检完再看其余任务。'
            : '当前待检任务都还在检验时限内。'
        "
      />
      <NvMetricCard
        v-if="onTimeRate !== null"
        variant="target"
        label="时限内完成率"
        :value="onTimeRate"
        unit="%"
        :progress="onTimeRate"
        target-label="目标 100%"
        :progress-tone="onTimeRate >= 100 ? 'success' : onTimeRate >= 90 ? 'warning' : 'danger'"
        :foot-start="`${overdueCount} 个已超期`"
        :foot-end="`共 ${tasks.length} 个在检`"
      />
      <NvMetricCard
        v-else
        variant="alert"
        label="时限内完成率"
        value="—"
        tone="neutral"
        :status="{ label: '暂无样本', tone: 'neutral' }"
        foot-start="当前没有待检任务，暂不计算时限内完成率。"
      />
    </div>

    <div class="grid gap-4 rounded-xl border bg-card p-4 shadow-sm">
      <div class="flex flex-col gap-3 lg:flex-row lg:items-end lg:justify-between">
        <h2 class="text-base font-semibold">先处理最紧急的任务</h2>
        <div class="flex flex-wrap gap-2" role="tablist" aria-label="来源类型">
          <NvButton
            v-for="tab in sourceTabs"
            :key="tab.value"
            size="sm"
            :variant="filters.sourceType === tab.value ? 'default' : 'outline'"
            role="tab"
            :aria-selected="filters.sourceType === tab.value"
            @click="filters.sourceType = tab.value"
          >
            {{ tab.label }}
          </NvButton>
        </div>
      </div>

      <div v-if="locatorMessage" class="flex flex-wrap items-center justify-between gap-2">
        <p class="text-sm font-medium" role="status">{{ locatorMessage }}</p>
        <NvButton size="sm" variant="ghost" @click="router.replace('/quality/inspection-tasks')">
          查看全部待检任务
        </NvButton>
      </div>

      <div class="grid gap-3 sm:grid-cols-[minmax(0,280px)_auto] sm:items-end">
        <NvField>
          <NvFieldLabel for="inspection-task-sku">按 SKU 查找</NvFieldLabel>
          <NvEntityPicker
            id="inspection-task-sku"
            v-model="skuModel"
            :options="skuCatalog.skuOptions.value"
            title="选择 SKU"
            placeholder="选择 SKU"
            source-text="数据来自基础数据物料主数据"
            :loading="skuCatalog.skusPending.value"
            clearable
            aria-label="按 SKU 查找"
          />
        </NvField>
        <p class="text-sm text-muted-foreground">{{ scopeHint }}</p>
      </div>
      <ListScopeMeta
        :scope="scopeText"
        :source="sourceTypeHint"
        :loaded="tasks.length"
        :total="total"
        :updated-at="lastUpdatedAt"
        :empty="tasksHasSuccessfulResponse && !error && tasks.length === 0"
        :failed="tasksHasFailedResponse || Boolean(error)"
        failure-explanation="质检待检任务服务未成功返回，请重试。"
        :empty-explanation="emptyExplanation"
      />
    </div>

    <p
      v-if="listErrorMessage"
      class="flex items-center gap-2 text-sm text-destructive"
      role="alert"
    >
      <AlertCircleIcon aria-hidden="true" />
      {{ listErrorMessage }}
      <NvButton size="sm" variant="outline" @click="refreshTasks">重试</NvButton>
    </p>

    <NvDataTable
      v-if="!listErrorMessage"
      :manual="!hasLocator"
      :page="page"
      :page-size="pageSize"
      :page-size-options="[50, 100, 200]"
      :total-items="total"
      :columns="columns"
      :rows="tasks"
      row-key="inspectionTaskId"
      :loading="pending"
      :searchable="false"
      :column-settings="false"
      :empty-message="emptyMessage"
      @update:page="page = $event"
      @update:page-size="(value) => (pageSize = String(value))"
    >
      <template #cell-sourceDocumentId="{ row }">
        <RouterLink
          v-if="sourceDocumentRoute(row)"
          class="font-medium underline underline-offset-2"
          :to="sourceDocumentRoute(row)"
        >
          {{ row.sourceDocumentId ?? '—' }}
        </RouterLink>
        <span v-else>{{ row.sourceDocumentId ?? '—' }}</span>
      </template>
      <template #cell-sourceType="{ row }">{{ sourceLabel(row.sourceType) }}</template>
      <template #cell-skuCode="{ row }">
        <CodeWithNameCell :code="row.skuCode" :name="resolveSkuName(row.skuCode)" />
      </template>
      <template #cell-assignedInspectorUserId="{ row }">
        <span :data-testid="`inspection-task-assignee-${row.inspectionTaskId}`">
          {{ inspectionTaskHolderLabel(row) }}
        </span>
      </template>
      <template #cell-dueAtUtc="{ row }">
        <span
          v-if="isInspectionTaskOverdue(row)"
          class="inline-flex items-center gap-1 font-medium text-destructive"
        >
          <AlertCircleIcon aria-hidden="true" />
          已超期 · {{ formatDateTime(row.dueAtUtc) }}
        </span>
        <span v-else>{{ formatDateTime(row.dueAtUtc) }}</span>
      </template>
      <template #cell-actions="{ row }">
        <NvButton
          v-if="row.allowedActions?.includes('claim')"
          size="sm"
          :disabled="!row.inspectionTaskId || claimPendingTaskId === row.inspectionTaskId"
          @click="goToInspectionForm(row)"
        >
          <ClipboardCheckIcon aria-hidden="true" />
          认领并开始检验
          <ArrowRightIcon aria-hidden="true" />
        </NvButton>
        <NvButton
          v-if="isTaskClaimedByAnother(row)"
          size="sm"
          variant="outline"
          @click="openReassignment(row)"
        >
          改派
        </NvButton>
      </template>
    </NvDataTable>

    <NvDialog v-model:open="assignmentDialogOpen">
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>改派待检任务</NvDialogTitle>
          <NvDialogDescription>
            当前持有人：{{
              assignmentTask ? inspectionTaskHolderLabel(assignmentTask) : '—'
            }}。改派前请确认交接对象与原因。
          </NvDialogDescription>
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submitReassignment">
          <NvField>
            <NvFieldLabel for="inspection-task-assignee">改派给</NvFieldLabel>
            <WorkerSelect
              id="inspection-task-assignee"
              v-model="assignmentTargetUserId"
              placeholder="选择检验员"
            />
          </NvField>
          <NvField>
            <NvFieldLabel for="inspection-task-assignment-reason">改派原因</NvFieldLabel>
            <NvInput
              id="inspection-task-assignment-reason"
              v-model="assignmentReason"
              placeholder="例如：原检验员调班"
            />
          </NvField>
          <NvDialogFooter>
            <NvButton type="button" variant="outline" @click="assignmentDialogOpen = false">
              取消
            </NvButton>
            <NvButton type="submit" :disabled="assignmentPending">确认改派</NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>

    <p class="text-xs text-muted-foreground">
      <RouterLink class="underline underline-offset-2" to="/quality/inspections"
        >查看检验记录</RouterLink
      >
    </p>
  </BusinessLayout>
</template>
