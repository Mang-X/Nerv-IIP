<script setup lang="ts">
import type { DateRange, NvDataTableColumn, NvMetricSegment } from '@nerv-iip/ui'
import {
  makeIdempotencyKey,
  useMesDowntimeEvents,
  useMesOperationTasks,
} from '@/composables/useBusinessMes'
import { pagedBreakdownSegments } from '@/composables/metricSegments'
import {
  mesDowntimeStatusOptions,
  useMesReferenceLabels,
} from '@/composables/mes/useMesReferenceLabels'
import { useMasterDataDisplayNames } from '@/composables/useMasterDataDisplayNames'
import { useMesKeywordFilter } from '@/composables/mes/useMesKeywordFilter'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import CodeWithNameCell from '@/components/business/CodeWithNameCell.vue'
import {
  NvButton,
  NvDataTable,
  NvDateRangePicker,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvField,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvMetricCard,
  NvPageHeader,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import { RefreshCwIcon } from '@lucide/vue'
import { computed, reactive, ref, shallowRef, watch } from 'vue'
import { useRoute } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { BUSINESS_PERMISSION_CODES } from '@/permissions'
import {
  inlineErrorMessage,
  isForbiddenError,
  notifyOperationFailure,
  notifySuccess,
} from '@/utils/notify'

definePage({
  meta: {
    requiresAuth: true,
    title: '设备与停机',
    requiredPermissions: ['business.mes.downtime.read'],
  },
})

const {
  downtimeEvents,
  downtimeEventsError,
  downtimeEventsPending,
  downtimeEventsTotal,
  downtimeReasonOptions,
  downtimeReasonSummary,
  downtimeReasonsError,
  downtimeReasonsPending,
  downtimeWriteCoversWorkOrder,
  downtimeWriteScope,
  downtimeWriteScopeMessage,
  downtimeWriteScopePending,
  downtimeWriteScopeReady,
  filters,
  recordDowntimeEvent,
  recordDowntimeEventPending,
  recoverDowntimeEvent,
  recoverDowntimeEventPending,
  refreshDowntimeWriteScope,
  refreshDowntimeEvents,
} = useMesDowntimeEvents()
const {
  operationTasks,
  operationTasksPending,
  operationListScopeMessage,
  operationListScopeReady,
  refreshOperationTasks,
} = useMesOperationTasks()
const { keyword } = useMesKeywordFilter(filters)
const { statusLabel } = useMesReferenceLabels()
const { page, pageSize } = usePagedList(filters, {
  resetOn: [
    () => filters.status,
    () => filters.keyword,
    () => filters.reasonCode,
    () => filters.windowStartUtc,
    () => filters.windowEndUtc,
  ],
})
const statusFilter = shallowRef('all')
const reasonFilter = shallowRef('all')
const route = useRoute()

const openCount = computed(
  () => downtimeEvents.value.filter((x) => x.status?.toLowerCase() === 'open').length,
)
// 停机的决策点是「还有多少台没恢复」——一张构成卡把总量与恢复进度放在一起。
const downtimeSegments = computed(() =>
  pagedBreakdownSegments(downtimeEventsTotal.value, [
    { key: 'open', label: '未恢复', value: openCount.value, tone: 'danger' },
    {
      key: 'recovered',
      label: '已恢复',
      value: downtimeEvents.value.length - openCount.value,
      tone: 'success',
    },
  ]),
)
// 停机时长按原因分类汇总：读面随列表一起返回，不受原因筛选影响，所以选中一个原因后仍能换到别的原因。
// 分段之和必须恒等于卡片主数值，所以主数值直接由分段求和，不另行取整（见 metricSegments 的语义前提）。
const downtimeHoursSegments = computed<NvMetricSegment[]>(() =>
  downtimeReasonSummary.value.map((row) => ({
    key: row.reasonCode ?? '',
    label: reasonText(row.reasonName, row.reasonCode),
    value: Number(((row.durationMinutes ?? 0) / 60).toFixed(1)),
    tone: (row.openCount ?? 0) > 0 ? 'danger' : 'brand',
  })),
)
const downtimeHoursTotal = computed(() =>
  Number(downtimeHoursSegments.value.reduce((sum, segment) => sum + segment.value, 0).toFixed(1)),
)
// 筛选项取自权威停机原因字典而不是本次汇总：汇总只含「当前筛选下真出现过的原因」，
// 一旦切状态把选中的原因筛没了，下拉会空白但过滤仍然生效，用户看不到也取消不掉。
// 只取纯名称：读面（列、分段卡、筛选）同一个原因必须显示同一串文字，且不把原因码打在界面上。
const reasonFilterOptions = computed(() => [
  { value: 'all', label: '全部原因' },
  ...downtimeReasonOptions.value.map((option) => ({ value: option.value, label: option.name })),
])
const errorMessage = computed(() => formatError(downtimeEventsError.value))
// 停机原因目录读失败的**唯一归因点**：写面（登记入口 blocker）与读面（原因筛选）共用同一句话。
// 归因分两处必然漂移——同一个 403 在两个面上会说成两种话；本页此前读面干脆什么都不说，
// 下拉静默只剩「全部原因」，用户看不出是没权限还是真没配。
// 网关在缺少停机原因词表读权限时回 403（ADR 0029 换绑后的权限码），生成客户端在
// throwOnError 下把它抛成 query error；笼统说「读取失败，请刷新」会让运维一直刷新，
// 掉到「组织尚未配置」则会让运维去配字典——两条都指错了地方。
const downtimeReasonsMessage = computed(() => {
  const error = downtimeReasonsError.value
  if (!error) return ''
  return isForbiddenError(error)
    ? '当前角色没有停机原因词表的读取权限，请联系管理员开通'
    : '停机原因读取失败，请刷新后重试'
})
watch(statusFilter, (value) => {
  filters.status = value === 'all' ? undefined : value
})
watch(reasonFilter, (value) => {
  filters.reasonCode = value === 'all' ? undefined : value
})

const windowRange = computed<DateRange>({
  get: () => ({
    start: toDateInput(filters.windowStartUtc),
    end: toInclusiveEndDateInput(filters.windowEndUtc),
  }),
  set: (range) => {
    if (range.start) filters.windowStartUtc = fromDateInput(range.start, 0)
    if (range.end) filters.windowEndUtc = fromDateInput(range.end, 1)
  },
})

function toDateInput(value?: string, dayOffset = 0) {
  if (!value) return null
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return null
  date.setDate(date.getDate() + dayOffset)
  const local = new Date(date.getTime() - date.getTimezoneOffset() * 60_000)
  return local.toISOString().slice(0, 10)
}

function toInclusiveEndDateInput(value?: string) {
  const date = value ? new Date(value) : null
  const isExclusiveDayBoundary =
    date?.getHours() === 0 &&
    date.getMinutes() === 0 &&
    date.getSeconds() === 0 &&
    date.getMilliseconds() === 0
  return toDateInput(value, isExclusiveDayBoundary ? -1 : 0)
}

function fromDateInput(value: string, dayOffset: number) {
  const [year, month, day] = value.split('-').map(Number)
  return new Date(year!, month! - 1, day! + dayOffset).toISOString()
}

// 停机读面只回设备编码，中文设备名在设备台账里，按编码 join 出来。
const { resolveDevice } = useMasterDataDisplayNames({ devices: true })

type DowntimeRow = (typeof downtimeEvents)['value'][number]

/** 原因中文名由门面按 Maintenance 停机原因目录解析；目录里没有的码（历史自由文本原因）照实显示原值。 */
function reasonText(reasonName?: string | null, reasonCode?: string | null) {
  return reasonName?.trim() || reasonCode?.trim() || '未指定'
}

function deviceCode(row: DowntimeRow) {
  return row.deviceAssetCode ?? row.deviceAssetId ?? ''
}
function deviceName(row: DowntimeRow) {
  return row.deviceAssetName ?? resolveDevice(deviceCode(row))
}
/** 「名称 编码」纯文本，供排序 / 导出用；名录查不到就只有编码，不编名字。 */
function deviceText(row: DowntimeRow) {
  const code = deviceCode(row)
  if (!code) return '未指定'
  const name = deviceName(row)
  return name ? `${name} ${code}` : code
}

const columns: NvDataTableColumn<DowntimeRow>[] = [
  {
    key: 'downtimeEventId',
    header: '停机事件',
    cellClass: 'font-medium',
    accessor: (r) => r.downtimeEventId ?? '无',
  },
  {
    key: 'workCenterId',
    header: '工作中心',
    accessor: (r) => r.workCenterId ?? '未指定',
  },
  {
    key: 'workOrderId',
    header: '工单',
    accessor: (r) => r.workOrderNo ?? r.workOrderId ?? '未关联',
  },
  {
    key: 'deviceAssetId',
    header: '设备',
    accessor: (r) => deviceText(r),
  },
  {
    key: 'reasonCode',
    header: '停机原因',
    accessor: (r) => reasonText(r.reasonName, r.reasonCode),
  },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'startedAtUtc', header: '开始', width: 'w-44' },
  { key: 'recoveredAtUtc', header: '恢复', width: 'w-44' },
  { key: 'actions', header: '操作', width: 'w-24', sortable: false, accessor: () => '' },
]

// ── 恢复停机（#1323：恢复通道从只读页断掉，这里补权威入口）──────────────
const auth = useAuthStore()
const canManageDowntime = computed(() =>
  (auth.principal?.permissionCodes ?? []).includes(BUSINESS_PERMISSION_CODES.mesDowntimeManage),
)
const canRecover = canManageDowntime

type OperationTask = (typeof operationTasks)['value'][number]
type DowntimeTarget = {
  key: string
  workOrderId: string
  operationTaskId: string
  workCenterId: string
  deviceAssetId: string
  operationTask: OperationTask
  label: string
}

const eligibleDowntimeTargets = computed<DowntimeTarget[]>(() => {
  const writeScope = downtimeWriteScope.value
  if (!writeScope || !canManageDowntime.value) return []
  return operationTasks.value.flatMap((task) => {
    const workOrderId = task.workOrderId?.trim()
    const operationTaskId = task.operationTaskId?.trim()
    const workCenterId = task.workCenterId?.trim()
    const deviceAssetId = task.deviceAssetId?.trim()
    if (
      !workOrderId ||
      !operationTaskId ||
      !workCenterId ||
      !deviceAssetId ||
      !downtimeWriteCoversWorkOrder({ operationTasks: [task] }, writeScope)
    ) {
      return []
    }
    return [
      {
        key: `operation:${operationTaskId}`,
        workOrderId,
        operationTaskId,
        workCenterId,
        deviceAssetId,
        operationTask: task,
        label: [
          task.workOrderNo || workOrderId,
          task.operationTaskNo || `第 ${task.operationSequence ?? '—'} 道工序`,
          task.workCenterName || task.workCenterCode || workCenterId,
          task.deviceAssetName || task.deviceAssetCode || deviceAssetId,
        ].join(' · '),
      },
    ]
  })
})

const recordDialogOpen = shallowRef(false)
const recordShowErrors = shallowRef(false)
const recordPreflightPending = shallowRef(false)
const recordForm = reactive({ targetKey: '', reasonCode: '', startedAtLocal: '' })
const pendingRecordIntent = shallowRef<{
  fingerprint: string
  idempotencyKey: string
} | null>(null)
const selectedDowntimeTarget = computed(() =>
  eligibleDowntimeTargets.value.find((target) => target.key === recordForm.targetKey),
)
const recordPending = computed(
  () =>
    recordPreflightPending.value ||
    recordDowntimeEventPending.value ||
    operationTasksPending.value ||
    downtimeReasonsPending.value,
)
const recordEntryBlocker = computed(() => {
  if (!canManageDowntime.value) return '没有停机登记权限'
  if (!filters.organizationId.trim() || !filters.environmentId.trim()) {
    return '尚未进入有效组织与环境'
  }
  if (downtimeWriteScopePending.value) return '正在核验停机登记范围'
  if (!downtimeWriteScopeReady.value) {
    return downtimeWriteScopeMessage.value || '停机登记范围未就绪'
  }
  if (!operationListScopeReady.value) {
    return operationListScopeMessage.value || '工序可见范围未就绪'
  }
  if (operationTasksPending.value) return '正在读取可登记停机的工序'
  if (downtimeReasonsPending.value) return '正在读取停机原因'
  if (downtimeReasonsMessage.value) return downtimeReasonsMessage.value
  if (downtimeReasonOptions.value.length === 0) return '当前组织尚未配置可用停机原因'
  if (eligibleDowntimeTargets.value.length === 0) {
    return '当前授权范围内暂无同时具备工作中心与设备上下文的工序'
  }
  return ''
})

function firstQuery(value: unknown) {
  if (Array.isArray(value)) return typeof value[0] === 'string' ? value[0] : ''
  return typeof value === 'string' ? value : ''
}

function toLocalDateTimeInput(date: Date) {
  const offset = date.getTimezoneOffset() * 60_000
  return new Date(date.getTime() - offset).toISOString().slice(0, 16)
}

function clearRecordIntent() {
  pendingRecordIntent.value = null
}

function openRecordDialog() {
  if (recordEntryBlocker.value) return
  const routeOperationTaskId = firstQuery(route.query.operationTaskId)
  const routeWorkOrderId = firstQuery(route.query.workOrderId)
  const routeDeviceAssetId = firstQuery(route.query.deviceAssetId)
  const preferred = eligibleDowntimeTargets.value.find((target) =>
    routeOperationTaskId
      ? target.operationTaskId === routeOperationTaskId &&
        (!routeWorkOrderId || target.workOrderId === routeWorkOrderId)
      : routeDeviceAssetId
        ? target.deviceAssetId === routeDeviceAssetId
        : !!routeWorkOrderId && target.workOrderId === routeWorkOrderId,
  )
  recordForm.targetKey = preferred?.key ?? ''
  recordForm.reasonCode = ''
  recordForm.startedAtLocal = toLocalDateTimeInput(new Date())
  recordShowErrors.value = false
  clearRecordIntent()
  recordDialogOpen.value = true
}

function validStartedAtUtc() {
  if (!recordForm.startedAtLocal.trim()) return undefined
  const date = new Date(recordForm.startedAtLocal)
  const time = date.getTime()
  if (!Number.isFinite(time) || time > Date.now()) return undefined
  return date.toISOString()
}

function findEligibleDowntimeTarget(targetKey: string) {
  const target = eligibleDowntimeTargets.value.find((candidate) => candidate.key === targetKey)
  const scope = downtimeWriteScope.value
  if (!target || !scope) return undefined
  return downtimeWriteCoversWorkOrder({ operationTasks: [target.operationTask] }, scope)
    ? target
    : undefined
}

async function submitDowntime() {
  const startedAtUtc = validStartedAtUtc()
  const reasonCode = recordForm.reasonCode.trim()
  const targetKey = recordForm.targetKey.trim()
  const reasonAvailable = downtimeReasonOptions.value.some((option) => option.value === reasonCode)
  if (
    recordEntryBlocker.value ||
    !targetKey ||
    !reasonAvailable ||
    !startedAtUtc ||
    recordPending.value
  ) {
    recordShowErrors.value = true
    return
  }

  recordPreflightPending.value = true
  let target: DowntimeTarget | undefined
  let scope: NonNullable<(typeof downtimeWriteScope)['value']>
  try {
    await Promise.all([refreshOperationTasks(), refreshDowntimeWriteScope()])
    target = findEligibleDowntimeTarget(targetKey)
    const latestScope = downtimeWriteScope.value
    if (!target || !latestScope) {
      throw new Error('所选工单或工序已不在当前主体可登记停机的范围，请重新选择。')
    }
    scope = latestScope
  } catch (error) {
    notifyOperationFailure(
      '停机登记前置检查失败',
      error,
      '未能确认当前工序、设备与授权范围，请刷新后重试。',
    )
    return
  } finally {
    recordPreflightPending.value = false
  }

  const fingerprint = JSON.stringify({
    organizationId: filters.organizationId.trim(),
    environmentId: filters.environmentId.trim(),
    workOrderId: target.workOrderId,
    operationTaskId: target.operationTaskId,
    workCenterId: target.workCenterId,
    deviceAssetId: target.deviceAssetId,
    reasonCode,
    startedAtUtc,
    scopeKind: scope.kind,
    scopeId: scope.id,
  })
  if (pendingRecordIntent.value?.fingerprint !== fingerprint) {
    pendingRecordIntent.value = {
      fingerprint,
      idempotencyKey: makeIdempotencyKey('record-downtime'),
    }
  }

  let response: Awaited<ReturnType<typeof recordDowntimeEvent>>
  try {
    response = await recordDowntimeEvent({
      workOrderId: target.workOrderId,
      operationTaskId: target.operationTaskId,
      workCenterId: target.workCenterId,
      deviceAssetId: target.deviceAssetId,
      reasonCode,
      startedAtUtc,
      idempotencyKey: pendingRecordIntent.value.idempotencyKey,
      scopeKind: scope.kind,
      scopeId: scope.id,
    })
    if (response?.data?.accepted !== true) {
      throw new Error('停机登记结果未确认，请刷新停机事件核实后再重试。')
    }
  } catch (error) {
    notifyOperationFailure('停机登记失败', error, '停机登记失败，请根据服务端原因检查后重试。')
    return
  }

  const downtimeEventId = response.data!.downstreamDocumentId?.trim()
  recordDialogOpen.value = false
  clearRecordIntent()
  notifySuccess(downtimeEventId ? `停机事件 ${downtimeEventId} 已登记。` : '停机登记已受理。')
  try {
    await refreshDowntimeEvents()
  } catch (error) {
    notifyOperationFailure(
      '停机已登记，但列表刷新失败',
      error,
      '停机已登记，但最新列表刷新失败，请手动刷新。',
    )
  }
}

const recoverTarget = ref<DowntimeRow | null>(null)
const recoverOpen = computed({
  get: () => recoverTarget.value !== null,
  set: (open: boolean) => {
    if (!open) recoverTarget.value = null
  },
})

function isOpenRow(row: DowntimeRow) {
  return row.status?.toLowerCase() === 'open'
}

async function confirmRecover() {
  const row = recoverTarget.value
  if (!row?.downtimeEventId) return
  try {
    await recoverDowntimeEvent(row.downtimeEventId, {
      organizationId: filters.organizationId,
      environmentId: filters.environmentId,
      recoveredAtUtc: new Date().toISOString(),
      // #1219 稳定幂等键：同一停机事件的恢复是同一业务意图，键不掺时间戳，
      // 重复点击/重试由后端幂等或 KnownException 兜住。
      idempotencyKey: `downtime-recover-${row.downtimeEventId}`,
    })
    notifySuccess('停机已恢复，该工作中心的开工拦截已解除。')
    recoverTarget.value = null
    void refreshDowntimeEvents()
  } catch (error) {
    notifyOperationFailure('恢复停机失败', error, '恢复停机失败，请稍后重试。')
  }
}

function formatDateTime(value?: string | null) {
  if (!value) return '未指定'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}
function formatError(error: unknown) {
  return inlineErrorMessage(error)
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="设备与停机"
      :breadcrumbs="[{ label: '制造执行' }]"
      :count="`${downtimeEventsTotal} 条停机事件`"
    >
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          :disabled="Boolean(recordEntryBlocker)"
          :title="recordEntryBlocker || '登记设备停机'"
          @click="openRecordDialog"
        >
          登记停机
        </NvButton>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="downtimeEventsPending"
          @click="refreshDowntimeEvents"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
      <NvMetricCard
        variant="breakdown"
        label="停机事件"
        :value="downtimeEventsTotal"
        unit="起"
        :segments="downtimeSegments"
      />
      <NvMetricCard
        variant="alert"
        label="未恢复停机"
        :value="openCount"
        unit="起"
        :tone="openCount > 0 ? 'danger' : 'neutral'"
        :status="
          openCount > 0
            ? { label: '需跟进恢复', tone: 'danger' }
            : { label: '全部恢复', tone: 'success' }
        "
        :foot-start="
          openCount > 0 ? '优先处理仍在影响工序执行的停机事件。' : '当前没有未恢复的停机事件。'
        "
      />
      <NvMetricCard
        variant="breakdown"
        label="停机时长按原因"
        :value="downtimeHoursTotal"
        unit="小时"
        :segments="downtimeHoursSegments"
        foot-start="未恢复停机按窗口结束或当前时刻（取较早者）累计。"
      />
    </div>

    <NvToolbar :show-search="false">
      <template #filters>
        <NvInput
          v-model="keyword"
          class="h-9 w-56"
          placeholder="工单 / 工序 / 设备"
          aria-label="搜索停机事件"
        />
        <NvSelect v-model="statusFilter">
          <NvSelectTrigger class="h-9 w-32" aria-label="停机状态"
            ><NvSelectValue
          /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem
              v-for="option in mesDowntimeStatusOptions"
              :key="option.value"
              :value="option.value"
              >{{ option.label }}</NvSelectItem
            >
          </NvSelectContent>
        </NvSelect>
        <NvSelect v-model="reasonFilter">
          <NvSelectTrigger class="h-9 w-44" aria-label="按停机原因筛选"
            ><NvSelectValue
          /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem
              v-for="option in reasonFilterOptions"
              :key="option.value"
              :value="option.value"
              >{{ option.label }}</NvSelectItem
            >
          </NvSelectContent>
        </NvSelect>
        <NvDateRangePicker v-model="windowRange" placeholder="选择统计窗口" />
      </template>
    </NvToolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>
    <p
      v-if="downtimeReasonsMessage"
      class="text-sm text-destructive"
      role="alert"
      data-testid="downtime-reasons-message"
    >
      {{ downtimeReasonsMessage }}
    </p>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="downtimeEventsTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :columns="columns"
      :rows="downtimeEvents"
      row-key="downtimeEventId"
      :loading="downtimeEventsPending"
      :searchable="false"
      :column-settings="false"
      empty-message="暂无停机事件。点击上方「登记停机」记录设备异常，登记后可在这里跟进恢复与影响范围。"
    >
      <template #cell-deviceAssetId="{ row }">
        <CodeWithNameCell :code="deviceCode(row)" :name="deviceName(row)" fallback="未指定" />
      </template>
      <template #cell-status="{ row }">
        <NvStatusBadge :value="row.status" :label="statusLabel(row.status)" />
      </template>
      <template #cell-startedAtUtc="{ row }">{{ formatDateTime(row.startedAtUtc) }}</template>
      <template #cell-recoveredAtUtc="{ row }">{{ formatDateTime(row.recoveredAtUtc) }}</template>
      <template #cell-actions="{ row }">
        <NvButton
          v-if="canRecover && isOpenRow(row)"
          size="sm"
          type="button"
          variant="outline"
          :disabled="recoverDowntimeEventPending"
          @click="recoverTarget = row"
        >
          恢复
        </NvButton>
        <span v-else class="text-muted-foreground">—</span>
      </template>
    </NvDataTable>

    <NvDialog v-model:open="recordDialogOpen">
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>确认登记停机</NvDialogTitle>
          <NvDialogDescription>
            从当前主体可见且可管理的工序中选择真实作业上下文，登记后将刷新停机事件列表。
          </NvDialogDescription>
        </NvDialogHeader>

        <NvFieldGroup class="grid gap-4">
          <NvField :data-invalid="recordShowErrors && !selectedDowntimeTarget">
            <NvFieldLabel>工单与工序</NvFieldLabel>
            <NvSelect v-model="recordForm.targetKey" aria-label="工单与工序">
              <NvSelectTrigger><NvSelectValue placeholder="选择工单与工序" /></NvSelectTrigger>
              <NvSelectContent>
                <NvSelectItem
                  v-for="target in eligibleDowntimeTargets"
                  :key="target.key"
                  :value="target.key"
                >
                  {{ target.label }}
                </NvSelectItem>
              </NvSelectContent>
            </NvSelect>
            <p v-if="recordShowErrors && !selectedDowntimeTarget" class="text-sm text-destructive">
              请选择同时具备工单、工序、工作中心与设备上下文的记录。
            </p>
          </NvField>

          <dl v-if="selectedDowntimeTarget" class="grid gap-2 rounded-md border p-3 text-sm">
            <div class="flex justify-between gap-4">
              <dt class="text-muted-foreground">工单</dt>
              <dd>
                {{
                  selectedDowntimeTarget.operationTask.workOrderNo ||
                  selectedDowntimeTarget.workOrderId
                }}
              </dd>
            </div>
            <div class="flex justify-between gap-4">
              <dt class="text-muted-foreground">工序</dt>
              <dd>
                {{
                  selectedDowntimeTarget.operationTask.operationTaskNo ||
                  selectedDowntimeTarget.operationTaskId
                }}
              </dd>
            </div>
            <div class="flex justify-between gap-4">
              <dt class="text-muted-foreground">工作中心</dt>
              <dd>
                {{
                  selectedDowntimeTarget.operationTask.workCenterName ||
                  selectedDowntimeTarget.workCenterId
                }}
              </dd>
            </div>
            <div class="flex justify-between gap-4">
              <dt class="text-muted-foreground">设备</dt>
              <dd>
                {{
                  selectedDowntimeTarget.operationTask.deviceAssetName ||
                  selectedDowntimeTarget.operationTask.deviceAssetCode ||
                  selectedDowntimeTarget.deviceAssetId
                }}
              </dd>
            </div>
          </dl>

          <NvField :data-invalid="recordShowErrors && !recordForm.reasonCode.trim()">
            <NvFieldLabel>停机原因</NvFieldLabel>
            <NvSelect v-model="recordForm.reasonCode" aria-label="停机原因">
              <NvSelectTrigger><NvSelectValue placeholder="选择停机原因" /></NvSelectTrigger>
              <NvSelectContent>
                <NvSelectItem
                  v-for="option in downtimeReasonOptions"
                  :key="option.value"
                  :value="option.value"
                >
                  {{ option.label }}
                </NvSelectItem>
              </NvSelectContent>
            </NvSelect>
            <p
              v-if="recordShowErrors && !recordForm.reasonCode.trim()"
              class="text-sm text-destructive"
            >
              请选择已配置的停机原因。
            </p>
          </NvField>

          <NvField :data-invalid="recordShowErrors && !validStartedAtUtc()">
            <NvFieldLabel>停机开始时间</NvFieldLabel>
            <NvInput
              v-model="recordForm.startedAtLocal"
              type="datetime-local"
              aria-label="停机开始时间"
              :max="toLocalDateTimeInput(new Date())"
            />
            <p v-if="recordShowErrors && !validStartedAtUtc()" class="text-sm text-destructive">
              请输入有效且不晚于当前时间的停机开始时间。
            </p>
          </NvField>
        </NvFieldGroup>

        <NvDialogFooter>
          <NvButton type="button" variant="outline" @click="recordDialogOpen = false">
            取消
          </NvButton>
          <NvButton
            type="button"
            :disabled="recordPending"
            data-testid="record-downtime-submit"
            @click="submitDowntime"
          >
            {{ recordPending ? '登记中…' : '确认登记' }}
          </NvButton>
        </NvDialogFooter>
      </NvDialogContent>
    </NvDialog>

    <NvDialog v-model:open="recoverOpen">
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>确认恢复停机</NvDialogTitle>
          <NvDialogDescription>
            确认后停机事件立即关闭，工作中心解除停机拦截，受影响工序可重新开工。
          </NvDialogDescription>
        </NvDialogHeader>
        <dl v-if="recoverTarget" class="grid gap-2 text-sm">
          <div class="flex justify-between gap-4">
            <dt class="text-muted-foreground">停机事件</dt>
            <dd class="font-medium">{{ recoverTarget.downtimeEventId }}</dd>
          </div>
          <div class="flex justify-between gap-4">
            <dt class="text-muted-foreground">工作中心</dt>
            <dd>{{ recoverTarget.workCenterId ?? '未指定' }}</dd>
          </div>
          <div class="flex justify-between gap-4">
            <dt class="text-muted-foreground">设备</dt>
            <dd>{{ deviceText(recoverTarget) }}</dd>
          </div>
          <div class="flex justify-between gap-4">
            <dt class="text-muted-foreground">停机开始</dt>
            <dd>{{ formatDateTime(recoverTarget.startedAtUtc) }}</dd>
          </div>
          <div class="flex justify-between gap-4">
            <dt class="text-muted-foreground">恢复人</dt>
            <dd>{{ auth.displayName }}</dd>
          </div>
        </dl>
        <NvDialogFooter>
          <NvButton type="button" variant="outline" @click="recoverOpen = false">取消</NvButton>
          <NvButton type="button" :disabled="recoverDowntimeEventPending" @click="confirmRecover">
            {{ recoverDowntimeEventPending ? '恢复中…' : '确认恢复' }}
          </NvButton>
        </NvDialogFooter>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
