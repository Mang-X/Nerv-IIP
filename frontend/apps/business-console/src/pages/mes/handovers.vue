<script setup lang="ts">
import type {
  BusinessConsoleMesCreateShiftHandoverRequest,
  BusinessConsoleMesShiftHandoverOpenIssue,
  BusinessConsoleMesShiftHandoverUnfinishedWorkOrder,
  BusinessConsoleMesShiftHandoverWipItem,
  BusinessConsoleResourceItem,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn, StatusTone } from '@nerv-iip/ui'
import { CheckCircle2Icon, PlusIcon, RefreshCwIcon } from '@lucide/vue'
import {
  NvButton,
  NvDataTable,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvDropdownMenuItem,
  NvField,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvMetricCard,
  NvPageHeader,
  NvRowActions,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvSheet,
  NvSheetContent,
  NvSheetDescription,
  NvSheetHeader,
  NvSheetTitle,
  NvStatusBadge,
  NvToolbar,
  Spinner,
} from '@nerv-iip/ui'
import { computed, reactive, ref, watch } from 'vue'
import { useBusinessMasterDataResources } from '@/composables/useBusinessMasterData'
import { makeIdempotencyKey, useMesShiftHandovers } from '@/composables/useBusinessMes'
import { useMesKeywordFilter } from '@/composables/mes/useMesKeywordFilter'
import { usePagedList } from '@/composables/usePagedList'
import { pagedBreakdownSegments } from '@/composables/metricSegments'
import {
  mesHandoverStatusOptions,
  useMesReferenceLabels,
} from '@/composables/mes/useMesReferenceLabels'
import {
  labelFor,
  normalizeCode,
  MES_HANDOVER_ISSUE_CATEGORY_LABELS,
  MES_HANDOVER_ISSUE_SEVERITY_LABELS,
  MES_HANDOVER_STATUS_LABELS,
} from '@/data/businessLabels'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { useAuthStore } from '@/stores/auth'
import {
  errorStatusCode,
  inlineErrorMessage,
  notifyError,
  notifyOperationFailure,
  notifySuccess,
} from '@/utils/notify'

definePage({
  meta: {
    requiresAuth: true,
    title: '班次交接',
    requiredPermissions: ['business.mes.handovers.read'],
  },
})

const {
  acceptShiftHandover,
  createShiftHandover,
  detailHandoverId,
  filters,
  handoverDetail,
  handoverDetailError,
  handoverDetailPending,
  handovers,
  handoversError,
  handoversPending,
  handoversTotal,
  refreshHandovers,
} = useMesShiftHandovers()
const { statusLabel } = useMesReferenceLabels()
const { keyword } = useMesKeywordFilter(filters)
const { page, pageSize } = usePagedList(filters, {
  resetOn: [() => filters.status, () => filters.keyword],
})

const HANDOVERS_MANAGE_PERMISSION = 'business.mes.handovers.manage'
const CATALOG_TAKE = 500
const SYSTEM_ID_PATTERN =
  /^[{(]?[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}[)}]?$/i
const auth = useAuthStore()
const canManageHandovers = computed(() =>
  (auth.principal?.permissionCodes ?? []).includes(HANDOVERS_MANAGE_PERMISSION),
)
const handoverContextReady = computed(() =>
  Boolean(filters.organizationId.trim() && filters.environmentId.trim()),
)

const createEntryBlocker = computed(() => {
  if (!canManageHandovers.value) return '没有交接单管理权限'
  if (!handoverContextReady.value) return '请先完成业务上下文选择'
  return ''
})

const shiftCatalog = useBusinessMasterDataResources('shift')
const teamCatalog = useBusinessMasterDataResources('team')
shiftCatalog.filters.take = CATALOG_TAKE
teamCatalog.filters.take = CATALOG_TAKE

type DirectoryOption = { value: string; label: string }

function isSystemIdentifier(value: string) {
  return SYSTEM_ID_PATTERN.test(value.trim())
}

function toDirectoryOptions(resources: BusinessConsoleResourceItem[]): DirectoryOption[] {
  return resources
    .map((resource) => {
      const value = resource.code?.trim()
      if (!value || resource.active === false || isSystemIdentifier(value)) return undefined

      const displayName = resource.displayName?.trim()
      const label = displayName && !isSystemIdentifier(displayName) ? displayName : value
      return { value, label }
    })
    .filter((option): option is DirectoryOption => Boolean(option))
    .sort((a, b) => a.label.localeCompare(b.label, 'zh-Hans-CN'))
}

const shiftOptions = computed(() => toDirectoryOptions(shiftCatalog.resources.value))
const teamOptions = computed(() => toDirectoryOptions(teamCatalog.resources.value))
const shiftLabels = computed(
  () => new Map(shiftOptions.value.map((option) => [option.value, option.label])),
)
const teamLabels = computed(
  () => new Map(teamOptions.value.map((option) => [option.value, option.label])),
)

function resolveShiftLabel(value?: string | null) {
  if (!value || isSystemIdentifier(value)) return '未排班'
  return shiftLabels.value.get(value) ?? value
}

function resolveTeamLabel(value?: string | null) {
  if (!value || isSystemIdentifier(value)) return undefined
  return teamLabels.value.get(value) ?? value
}

const statusFilter = computed({
  get: () => filters.status || 'all',
  set: (value: string) => {
    filters.status = value === 'all' ? undefined : value
  },
})
const currentPageOpenIssueTotal = computed(() =>
  handovers.value.reduce((s, r) => s + (r.openIssueCount ?? 0), 0),
)
const currentPageAcceptedCount = computed(
  () => handovers.value.filter((r) => (r.handoverStatus ?? '').toLowerCase() === 'accepted').length,
)
const handoverSegments = computed(() =>
  pagedBreakdownSegments(handovers.value.length, [
    {
      key: 'open',
      label: '待接班',
      value: handovers.value.length - currentPageAcceptedCount.value,
      tone: 'warning',
    },
    { key: 'accepted', label: '已接班', value: currentPageAcceptedCount.value, tone: 'success' },
  ]),
)
const errorMessage = computed(() => inlineErrorMessage(handoversError.value))

type HandoverRow = (typeof handovers)['value'][number]
const columns: NvDataTableColumn<HandoverRow>[] = [
  {
    key: 'handoverId',
    header: '交接单',
    cellClass: 'font-medium',
    accessor: () => '—',
  },
  { key: 'shiftId', header: '班次', accessor: (r) => resolveShiftLabel(r.shiftId) },
  {
    key: 'teamId',
    header: '班组',
    accessor: (r) => r.teamName?.trim() || resolveTeamLabel(r.teamId) || '未指派',
  },
  { key: 'outgoingUserName', header: '交班人', accessor: outgoingUserLabel },
  { key: 'incomingUserName', header: '接班人', accessor: incomingUserLabel },
  { key: 'handoverStatus', header: '状态', width: 'w-24' },
  { key: 'detailCounts', header: '交接明细' },
  { key: 'openIssueCount', header: '未结事项', align: 'end', width: 'w-24' },
  { key: 'createdAtUtc', header: '创建时间', width: 'w-44' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-24' },
]

// 交接人显示名由网关按员工目录解析后回显；目录解不出时读面只剩用户 id，那是工程标识符、
// 不能上屏，所以退回「未记录」。接班人在未接班时本来就没有，说法要和「解析不出」区分开。
function outgoingUserLabel(row: { outgoingUserName?: string | null }) {
  return row.outgoingUserName?.trim() || '未记录'
}
function incomingUserLabel(row: {
  incomingUserName?: string | null
  acceptedAtUtc?: string | null
}) {
  return row.incomingUserName?.trim() || (row.acceptedAtUtc ? '未记录' : '待接班')
}

const detailOpen = ref(false)
// 抽屉正文只认详情读面，不用列表行垫底：详情取数失败时 `handoverDetail` 为空而列表行还在，
// 垫上去就会让三张明细表以 `rows=[]` 渲染成「交班时点没有登记…」——那是把「没取到」
// 谎报成「没登记」，还与同屏的错误横幅、列表行自己的计数三方矛盾。失败时只留横幅。
const detailWipItems = computed<BusinessConsoleMesShiftHandoverWipItem[]>(
  () => handoverDetail.value?.wipItems ?? [],
)
const detailUnfinishedWorkOrders = computed<BusinessConsoleMesShiftHandoverUnfinishedWorkOrder[]>(
  () => handoverDetail.value?.unfinishedWorkOrders ?? [],
)
const detailOpenIssues = computed<BusinessConsoleMesShiftHandoverOpenIssue[]>(
  () => handoverDetail.value?.openIssues ?? [],
)
const detailErrorMessage = computed(() => inlineErrorMessage(handoverDetailError.value))

function openDetail(row: HandoverRow) {
  const handoverId = row.handoverId?.trim()
  if (!handoverId) return
  detailHandoverId.value = handoverId
  detailOpen.value = true
}

watch(detailOpen, (open) => {
  if (open) return
  detailHandoverId.value = ''
})

const ISSUE_SEVERITY_TONES: Readonly<Record<string, StatusTone>> = {
  low: 'neutral',
  medium: 'warning',
  high: 'danger',
}
function issueSeverityTone(value?: string | null): StatusTone {
  return ISSUE_SEVERITY_TONES[normalizeCode(value)] ?? 'neutral'
}

const wipColumns: NvDataTableColumn<BusinessConsoleMesShiftHandoverWipItem>[] = [
  { key: 'workOrderId', header: '工单', cellClass: 'font-medium' },
  {
    key: 'operationTaskId',
    header: '工序任务',
    accessor: (row) => row.operationTaskId?.trim() || '按工单登记',
  },
  { key: 'quantity', header: '在制数量', align: 'end', width: 'w-28' },
]

const unfinishedWorkOrderColumns: NvDataTableColumn<BusinessConsoleMesShiftHandoverUnfinishedWorkOrder>[] =
  [
    { key: 'workOrderId', header: '工单', cellClass: 'font-medium' },
    { key: 'plannedQuantity', header: '计划数量', align: 'end', width: 'w-24' },
    { key: 'completedQuantity', header: '完成数量', align: 'end', width: 'w-24' },
    { key: 'workOrderStatus', header: '工单状态', width: 'w-28' },
  ]

const openIssueColumns: NvDataTableColumn<BusinessConsoleMesShiftHandoverOpenIssue>[] = [
  {
    key: 'category',
    header: '类别',
    width: 'w-20',
    accessor: (row) => labelFor(MES_HANDOVER_ISSUE_CATEGORY_LABELS, row.category, '未分类'),
  },
  { key: 'severity', header: '严重度', width: 'w-24' },
  { key: 'description', header: '问题描述' },
  { key: 'referenceId', header: '关联单据', accessor: (row) => row.referenceId?.trim() || '无' },
]

/**
 * 三类明细的读面都没有 id 字段（WipItem / UnfinishedWorkOrder / OpenIssue 只有业务属性），
 * 拿属性拼键就会撞——同类别同描述的两条遗留问题、同工单同工序的两条在制清点都是合法数据。
 *
 * 这里查的是**对象引用**在本数组里的位置，成立条件只有一条：同一数组内各元素引用互不相同
 * （读面反序列化出来的对象天然满足）。所以给某列加排序也不必重做这个键——`NvDataTable`
 * 排序后透传的仍是同一批对象引用。真正会坏的是换成「渲染时的下标」那类与数据脱钩的写法。
 */
function rowPositionKey<T>(rows: readonly T[]) {
  return (row: T) => String(rows.indexOf(row))
}

const createDialogOpen = ref(false)
const createShowErrors = ref(false)
const createPending = ref(false)
const createIdempotencyKey = ref('')
const createForm = reactive({ shiftId: '', teamId: '' })
const selectedShift = computed(() =>
  shiftOptions.value.find((option) => option.value === createForm.shiftId),
)
const selectedTeam = computed(() =>
  teamOptions.value.find((option) => option.value === createForm.teamId),
)
const createFormReady = computed(
  () =>
    canManageHandovers.value &&
    handoverContextReady.value &&
    Boolean(selectedShift.value && selectedTeam.value),
)

function resetCreateForm() {
  createForm.shiftId = ''
  createForm.teamId = ''
  createShowErrors.value = false
}

function openCreateDialog() {
  if (createEntryBlocker.value) return
  resetCreateForm()
  createIdempotencyKey.value = makeIdempotencyKey('mes-handover-create')
  createDialogOpen.value = true
}

function readReceiptOutcome(response: unknown, action: string): 'accepted' | 'confirmed' {
  if (!isRecord(response) || !isRecord(response.data) || response.data.accepted !== true) {
    throw new Error(`${action}未返回有效回执，请刷新列表核实后再重试。`)
  }

  const receipt = response.data.operationReceipt
  // 当前 handover gateway 只保证 accepted=true，operationReceipt 仍可能为空；此响应本身就是受理回执。
  if (receipt == null) return 'accepted'
  if (!isRecord(receipt) || (receipt.outcome !== 'accepted' && receipt.outcome !== 'confirmed')) {
    throw new Error(`${action}未返回有效回执，请刷新列表核实后再重试。`)
  }

  return receipt.outcome
}

function receiptMessage(action: 'create' | 'accept', outcome: 'accepted' | 'confirmed') {
  const status = outcome === 'confirmed' ? '确认' : '受理'
  return action === 'create'
    ? `班次交接创建成功，服务端已${status}。`
    : `接班已${status}，服务端已${status}。`
}

async function refreshAfterWrite() {
  try {
    await refreshHandovers()
    return true
  } catch (error) {
    notifyError(error, '班次交接列表刷新失败，请稍后手动刷新。')
    return false
  }
}

function isDeterministicAcceptFailure(error: unknown) {
  const status = errorStatusCode(error)
  return status !== undefined && status >= 400 && status < 500
}

async function submitCreate() {
  createShowErrors.value = true
  if (!createFormReady.value || createPending.value) return

  const body: BusinessConsoleMesCreateShiftHandoverRequest = {
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
    shiftId: createForm.shiftId,
    teamId: createForm.teamId,
    teamName: selectedTeam.value?.label,
    idempotencyKey:
      createIdempotencyKey.value ||
      (createIdempotencyKey.value = makeIdempotencyKey('mes-handover-create')),
  }

  createPending.value = true
  try {
    const response = await createShiftHandover(body)
    const outcome = readReceiptOutcome(response, '创建班次交接')
    createDialogOpen.value = false
    createShowErrors.value = false
    notifySuccess(receiptMessage('create', outcome))
    await refreshAfterWrite()
  } catch (error) {
    notifyOperationFailure('创建班次交接失败', error, '创建班次交接失败，请稍后重试。')
  } finally {
    createPending.value = false
  }
}

const acceptDialogOpen = ref(false)
const acceptTarget = ref<HandoverRow | null>(null)
const acceptPendingId = ref<string | null>(null)
const acceptIdempotencyKeys = new Map<string, string>()
// accept 当前没有服务端 replay 回执；网络结果不确定时锁住该行，避免再次触发状态机。
const acceptOutcomeUnknownIds = reactive(new Set<string>())

function isOpenHandover(row: HandoverRow) {
  return (row.handoverStatus ?? '').toLowerCase() === 'open'
}

function canAcceptRow(row: HandoverRow) {
  const handoverId = row.handoverId?.trim()
  return (
    canManageHandovers.value &&
    handoverContextReady.value &&
    isOpenHandover(row) &&
    Boolean(handoverId) &&
    acceptPendingId.value !== handoverId &&
    !acceptOutcomeUnknownIds.has(handoverId ?? '')
  )
}

const acceptOutcomeUnknown = computed(() => {
  const handoverId = acceptTarget.value?.handoverId?.trim()
  return Boolean(handoverId && acceptOutcomeUnknownIds.has(handoverId))
})

function openAcceptDialog(row: HandoverRow) {
  if (!canAcceptRow(row)) return
  const handoverId = row.handoverId?.trim()
  if (!handoverId) return

  acceptTarget.value = row
  if (!acceptIdempotencyKeys.has(handoverId)) {
    acceptIdempotencyKeys.set(handoverId, makeIdempotencyKey('mes-handover-accept'))
  }
  acceptDialogOpen.value = true
}

const acceptTargetShiftLabel = computed(() => resolveShiftLabel(acceptTarget.value?.shiftId))
const acceptTargetTeamLabel = computed(
  () =>
    acceptTarget.value?.teamName?.trim() ||
    resolveTeamLabel(acceptTarget.value?.teamId) ||
    '未指派',
)

async function submitAccept() {
  const target = acceptTarget.value
  const handoverId = target?.handoverId?.trim()
  if (!target || !handoverId || !canAcceptRow(target)) return

  const idempotencyKey = acceptIdempotencyKeys.get(handoverId)
  if (!idempotencyKey) return

  acceptPendingId.value = handoverId
  try {
    const response = await acceptShiftHandover(handoverId, {
      organizationId: filters.organizationId,
      environmentId: filters.environmentId,
      idempotencyKey,
    })
    const outcome = readReceiptOutcome(response, '接班')
    acceptDialogOpen.value = false
    acceptTarget.value = null
    acceptIdempotencyKeys.delete(handoverId)
    acceptOutcomeUnknownIds.delete(handoverId)
    notifySuccess(receiptMessage('accept', outcome))
    await refreshAfterWrite()
  } catch (error) {
    if (isDeterministicAcceptFailure(error)) {
      acceptOutcomeUnknownIds.delete(handoverId)
      notifyOperationFailure('接班失败', error, '接班失败，请检查权限或交接单状态后重试。')
      return
    }

    acceptOutcomeUnknownIds.add(handoverId)
    const refreshed = await refreshAfterWrite()
    const refreshedTarget = handovers.value.find((row) => row.handoverId?.trim() === handoverId)
    if (refreshed && refreshedTarget && !isOpenHandover(refreshedTarget)) {
      acceptDialogOpen.value = false
      acceptTarget.value = null
      acceptIdempotencyKeys.delete(handoverId)
      acceptOutcomeUnknownIds.delete(handoverId)
      notifySuccess('接班已受理，列表已确认。')
    } else {
      notifyOperationFailure(
        '接班结果待确认',
        error,
        '接班结果尚未确认，请刷新页面核实；本页已阻止重复提交。',
      )
    }
  } finally {
    acceptPendingId.value = null
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

function formatDateTime(value?: string | null) {
  if (!value) return '未指定'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="班次交接"
      :breadcrumbs="[{ label: '制造执行' }]"
      :count="`${handoversTotal} 条交接`"
    >
      <template #actions>
        <NvButton
          aria-label="新建班次交接"
          size="sm"
          type="button"
          :disabled="Boolean(createEntryBlocker)"
          :title="createEntryBlocker || '新建班次交接单'"
          @click="openCreateDialog"
        >
          <PlusIcon aria-hidden="true" />
          新建交接
        </NvButton>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="handoversPending"
          @click="refreshHandovers"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
      <NvMetricCard
        variant="breakdown"
        label="当前列表交接单"
        :value="handovers.length"
        unit="张"
        :segments="handoverSegments"
      />
      <NvMetricCard
        variant="alert"
        label="当前列表未结事项"
        :value="currentPageOpenIssueTotal"
        unit="项"
        :tone="currentPageOpenIssueTotal > 0 ? 'warning' : 'neutral'"
        :status="
          currentPageOpenIssueTotal > 0
            ? { label: '当前页需跟进', tone: 'warning' }
            : { label: '当前页无遗留', tone: 'success' }
        "
        :foot-start="
          currentPageOpenIssueTotal > 0
            ? '仅统计当前列表页返回的交接单；详情以交接单为准。'
            : '当前列表页没有未结事项。'
        "
      />
    </div>

    <NvToolbar :show-search="false">
      <template #filters>
        <NvInput
          v-model="keyword"
          class="h-9 w-56"
          placeholder="交接单 / 班次 / 班组"
          aria-label="搜索班次交接"
        />
        <NvSelect v-model="statusFilter">
          <NvSelectTrigger class="h-9 w-32" aria-label="交接状态"
            ><NvSelectValue
          /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem
              v-for="option in mesHandoverStatusOptions"
              :key="option.value"
              :value="option.value"
              >{{ option.label }}</NvSelectItem
            >
          </NvSelectContent>
        </NvSelect>
      </template>
    </NvToolbar>

    <NvDataTable
      manual
      data-testid="handovers-table"
      :page="page"
      :page-size="pageSize"
      :total-items="handoversTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :columns="columns"
      :rows="handovers"
      row-key="handoverId"
      :loading="handoversPending"
      :error="handoversError"
      :error-message="errorMessage"
      :searchable="false"
      :column-settings="false"
      row-class="cursor-pointer"
      empty-message="暂无班次交接。点击上方「新建交接」登记未完成事项，接班人可在这里确认接收。"
      @retry="refreshHandovers"
      @row-click="openDetail"
    >
      <template #cell-handoverStatus="{ row }">
        <NvStatusBadge
          :value="row.handoverStatus"
          :label="labelFor(MES_HANDOVER_STATUS_LABELS, row.handoverStatus) || '未知'"
        />
      </template>
      <template #cell-detailCounts="{ row }">
        <span class="text-sm text-muted-foreground">
          在制
          <span class="font-medium tabular-nums text-foreground">{{ row.wipItemCount ?? 0 }}</span>
          · 未完工单
          <span class="font-medium tabular-nums text-foreground">{{
            row.unfinishedWorkOrderCount ?? 0
          }}</span>
          · 遗留
          <span class="font-medium tabular-nums text-foreground">{{
            row.openIssueDetailCount ?? 0
          }}</span>
        </span>
      </template>
      <template #cell-openIssueCount="{ row }"
        ><span class="tabular-nums">{{ row.openIssueCount ?? 0 }}</span></template
      >
      <template #cell-createdAtUtc="{ row }">{{ formatDateTime(row.createdAtUtc) }}</template>
      <template #cell-actions="{ row }">
        <!-- NvDataTable 的 row-click 挂在整行上；操作列自己是交互区，点它不该顺带打开详情抽屉。
             NvRowActions 的根是 reka 的 DropdownMenuRoot（不渲染元素），事件修饰符落不到 DOM 上，
             所以由这层 span 承接 stop。 -->
        <span v-if="canManageHandovers && isOpenHandover(row)" class="inline-flex" @click.stop>
          <NvRowActions label="班次交接操作">
            <NvDropdownMenuItem
              data-testid="accept-handover"
              :disabled="!canAcceptRow(row)"
              @click="openAcceptDialog(row)"
            >
              <CheckCircle2Icon aria-hidden="true" />
              接班
            </NvDropdownMenuItem>
          </NvRowActions>
        </span>
      </template>
    </NvDataTable>

    <NvSheet v-model:open="detailOpen">
      <NvSheetContent
        data-testid="handover-detail"
        class="w-full gap-0 overflow-y-auto sm:max-w-3xl"
      >
        <NvSheetHeader>
          <NvSheetTitle>班次交接明细</NvSheetTitle>
          <NvSheetDescription>
            交班时点的在制清点、未完工单进度与遗留问题，供接班人逐项核对后再确认接班。
          </NvSheetDescription>
        </NvSheetHeader>

        <!-- 抽屉宽度与视口无关（这里最宽 768px，窄屏则是整屏），所以内部多列必须按**容器**宽度
             决定：父级开 `@container`，断点用 `@md:`。用 `sm:` 之类视口断点会在宽屏窄抽屉里
             把字段压成竖排单字（护栏与踩坑记录见 container-breakpoint.contract.test.ts）。
             `grid-cols-1` + `[&>*]:min-w-0` 解掉栅格子项默认的 `min-width:auto`，
             否则下面三张表会按内容最小宽把抽屉顶破。 -->
        <div class="@container grid grid-cols-1 content-start gap-4 px-4 pb-4 [&>*]:min-w-0">
          <p
            v-if="detailErrorMessage"
            class="rounded-lg border border-destructive/40 bg-destructive/5 p-3 text-sm text-destructive"
            role="alert"
          >
            {{ detailErrorMessage }}
          </p>
          <p
            v-else-if="handoverDetailPending && !handoverDetail"
            class="flex items-center gap-2 text-sm text-muted-foreground"
            role="status"
          >
            <Spinner aria-hidden="true" />
            正在加载交接明细…
          </p>

          <template v-if="handoverDetail">
            <dl class="grid gap-3 @md:grid-cols-2">
              <div class="rounded-lg border bg-card p-3">
                <dt class="text-xs text-muted-foreground">状态</dt>
                <dd class="mt-1">
                  <NvStatusBadge
                    :value="handoverDetail.handoverStatus"
                    :label="
                      labelFor(MES_HANDOVER_STATUS_LABELS, handoverDetail.handoverStatus) || '未知'
                    "
                  />
                </dd>
              </div>
              <div class="rounded-lg border bg-card p-3">
                <dt class="text-xs text-muted-foreground">未结事项</dt>
                <dd class="mt-1 text-lg font-semibold tabular-nums">
                  {{ handoverDetail.openIssueCount ?? 0 }}
                </dd>
              </div>
              <div class="rounded-lg border bg-card p-3">
                <dt class="text-xs text-muted-foreground">班次</dt>
                <dd class="mt-1 text-sm">{{ resolveShiftLabel(handoverDetail.shiftId) }}</dd>
              </div>
              <div class="rounded-lg border bg-card p-3">
                <dt class="text-xs text-muted-foreground">班组</dt>
                <dd class="mt-1 text-sm">
                  {{
                    handoverDetail.teamName?.trim() ||
                    resolveTeamLabel(handoverDetail.teamId) ||
                    '未指派'
                  }}
                </dd>
              </div>
              <div class="rounded-lg border bg-card p-3">
                <dt class="text-xs text-muted-foreground">交班人</dt>
                <dd class="mt-1 text-sm">{{ outgoingUserLabel(handoverDetail) }}</dd>
              </div>
              <div class="rounded-lg border bg-card p-3">
                <dt class="text-xs text-muted-foreground">接班人</dt>
                <dd class="mt-1 text-sm">{{ incomingUserLabel(handoverDetail) }}</dd>
              </div>
              <div class="rounded-lg border bg-card p-3">
                <dt class="text-xs text-muted-foreground">创建时间</dt>
                <dd class="mt-1 text-sm">{{ formatDateTime(handoverDetail.createdAtUtc) }}</dd>
              </div>
              <div class="rounded-lg border bg-card p-3">
                <dt class="text-xs text-muted-foreground">接班时间</dt>
                <dd class="mt-1 text-sm">
                  {{
                    handoverDetail.acceptedAtUtc
                      ? formatDateTime(handoverDetail.acceptedAtUtc)
                      : '尚未接班'
                  }}
                </dd>
              </div>
            </dl>

            <section class="grid grid-cols-1 gap-2 [&>*]:min-w-0">
              <h3 class="text-sm font-semibold text-foreground">在制清点</h3>
              <NvDataTable
                :columns="wipColumns"
                :rows="detailWipItems"
                :row-key="rowPositionKey(detailWipItems)"
                :loading="handoverDetailPending"
                :searchable="false"
                :column-settings="false"
                :pagination="false"
                empty-message="交班时点没有登记在制清点。"
              >
                <template #cell-quantity="{ row }"
                  ><span class="tabular-nums">{{ row.quantity ?? 0 }}</span></template
                >
              </NvDataTable>
            </section>

            <section class="grid grid-cols-1 gap-2 [&>*]:min-w-0">
              <h3 class="text-sm font-semibold text-foreground">未完工单</h3>
              <NvDataTable
                :columns="unfinishedWorkOrderColumns"
                :rows="detailUnfinishedWorkOrders"
                :row-key="rowPositionKey(detailUnfinishedWorkOrders)"
                :loading="handoverDetailPending"
                :searchable="false"
                :column-settings="false"
                :pagination="false"
                empty-message="交班时点没有未完工单。"
              >
                <template #cell-plannedQuantity="{ row }"
                  ><span class="tabular-nums">{{ row.plannedQuantity ?? 0 }}</span></template
                >
                <template #cell-completedQuantity="{ row }"
                  ><span class="tabular-nums">{{ row.completedQuantity ?? 0 }}</span></template
                >
                <template #cell-workOrderStatus="{ row }">
                  <NvStatusBadge
                    :value="row.workOrderStatus"
                    :label="statusLabel(row.workOrderStatus)"
                  />
                </template>
              </NvDataTable>
            </section>

            <section class="grid grid-cols-1 gap-2 [&>*]:min-w-0">
              <h3 class="text-sm font-semibold text-foreground">设备与质量遗留问题</h3>
              <NvDataTable
                :columns="openIssueColumns"
                :rows="detailOpenIssues"
                :row-key="rowPositionKey(detailOpenIssues)"
                :loading="handoverDetailPending"
                :searchable="false"
                :column-settings="false"
                :pagination="false"
                empty-message="交班时点没有登记遗留问题。"
              >
                <template #cell-severity="{ row }">
                  <NvStatusBadge
                    :value="row.severity"
                    :label="labelFor(MES_HANDOVER_ISSUE_SEVERITY_LABELS, row.severity, '未定级')"
                    :tone="issueSeverityTone(row.severity)"
                  />
                </template>
              </NvDataTable>
            </section>
          </template>
        </div>
      </NvSheetContent>
    </NvSheet>

    <NvDialog v-if="canManageHandovers" v-model:open="createDialogOpen">
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>创建班次交接</NvDialogTitle>
          <NvDialogDescription class="sr-only"
            >选择当前可见的班次和班组，创建班次交接单。</NvDialogDescription
          >
        </NvDialogHeader>
        <form data-testid="create-handover-form" class="grid gap-4" @submit.prevent="submitCreate">
          <p
            v-if="createShowErrors && !createFormReady"
            class="text-sm text-destructive"
            role="alert"
          >
            请选择班次和班组，并确认当前可见业务范围完整。
          </p>
          <NvFieldGroup class="grid gap-3">
            <NvField>
              <NvFieldLabel for="handover-create-shift-trigger">交接班次</NvFieldLabel>
              <NvSelect
                v-model="createForm.shiftId"
                data-testid="handover-create-shift"
                :data-invalid="createShowErrors && !selectedShift ? '' : undefined"
                :disabled="shiftOptions.length === 0"
              >
                <NvSelectTrigger id="handover-create-shift-trigger" aria-label="选择交接班次">
                  <NvSelectValue placeholder="选择班次" />
                </NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem
                    v-for="option in shiftOptions"
                    :key="option.value"
                    :value="option.value"
                  >
                    {{ option.label }}
                  </NvSelectItem>
                </NvSelectContent>
              </NvSelect>
              <p v-if="createShowErrors && !selectedShift" class="text-xs text-destructive">
                请选择交接班次。
              </p>
            </NvField>
            <NvField>
              <NvFieldLabel for="handover-create-team-trigger">交接班组</NvFieldLabel>
              <NvSelect
                v-model="createForm.teamId"
                data-testid="handover-create-team"
                :data-invalid="createShowErrors && !selectedTeam ? '' : undefined"
                :disabled="teamOptions.length === 0"
              >
                <NvSelectTrigger id="handover-create-team-trigger" aria-label="选择交接班组">
                  <NvSelectValue placeholder="选择班组" />
                </NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem
                    v-for="option in teamOptions"
                    :key="option.value"
                    :value="option.value"
                  >
                    {{ option.label }}
                  </NvSelectItem>
                </NvSelectContent>
              </NvSelect>
              <p v-if="createShowErrors && !selectedTeam" class="text-xs text-destructive">
                请选择交接班组。
              </p>
            </NvField>
          </NvFieldGroup>
          <p class="text-xs text-muted-foreground">未结事项由服务端按当前可见范围生成快照。</p>
          <NvDialogFooter>
            <NvButton
              type="button"
              variant="outline"
              :disabled="createPending"
              @click="createDialogOpen = false"
            >
              取消
            </NvButton>
            <NvButton type="submit" :disabled="createPending">
              <Spinner v-if="createPending" aria-hidden="true" />
              <PlusIcon v-else aria-hidden="true" />
              创建交接单
            </NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>

    <NvDialog v-if="canManageHandovers" v-model:open="acceptDialogOpen">
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>确认接班</NvDialogTitle>
          <NvDialogDescription class="sr-only"
            >确认接收当前待接班交接单，服务端将按当前权限核验状态。</NvDialogDescription
          >
        </NvDialogHeader>
        <form class="grid gap-4" data-testid="accept-handover-form" @submit.prevent="submitAccept">
          <dl class="grid gap-2 text-sm">
            <div class="flex justify-between gap-4">
              <dt class="text-muted-foreground">班次</dt>
              <dd class="font-medium">{{ acceptTargetShiftLabel }}</dd>
            </div>
            <div class="flex justify-between gap-4">
              <dt class="text-muted-foreground">班组</dt>
              <dd class="font-medium">{{ acceptTargetTeamLabel }}</dd>
            </div>
          </dl>
          <p v-if="acceptOutcomeUnknown" class="text-sm text-destructive" role="alert">
            接班结果尚未确认，请刷新列表核实；本页已阻止重复提交。
          </p>
          <p v-else class="text-sm text-muted-foreground">
            确认接收当前待接班交接单；服务端将按当前权限核验状态。
          </p>
          <NvDialogFooter>
            <NvButton
              type="button"
              variant="outline"
              :disabled="acceptPendingId !== null"
              @click="acceptDialogOpen = false"
            >
              取消
            </NvButton>
            <NvButton type="submit" :disabled="acceptPendingId !== null || acceptOutcomeUnknown">
              <Spinner v-if="acceptPendingId !== null" aria-hidden="true" />
              <CheckCircle2Icon v-else-if="!acceptOutcomeUnknown" aria-hidden="true" />
              {{ acceptOutcomeUnknown ? '结果待确认' : '确认接班' }}
            </NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
