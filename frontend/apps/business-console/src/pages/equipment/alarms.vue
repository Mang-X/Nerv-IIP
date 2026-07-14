<script setup lang="ts">
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { useBusinessEquipmentAlarms } from '@/composables/useBusinessEquipment'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { useAuthStore } from '@/stores/auth'
import { notifyError, notifySuccess } from '@/utils/notify'
import {
  NvAlertDialog,
  NvAlertDialogAction,
  NvAlertDialogCancel,
  NvAlertDialogContent,
  NvAlertDialogDescription,
  NvAlertDialogFooter,
  NvAlertDialogHeader,
  NvAlertDialogTitle,
  NvBadge,
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
  NvFieldError,
  NvFieldLabel,
  NvInput,
  NvPageHeader,
  NvRadioGroup,
  NvRadioGroupItem,
  NvRowActions,
  NvSectionCard,
  NvSectionCards,
  NvTooltip,
  NvTooltipContent,
  NvTooltipProvider,
  NvTooltipTrigger,
} from '@nerv-iip/ui'
import {
  BellOffIcon,
  CheckCircle2Icon,
  EyeIcon,
  LineChartIcon,
  RefreshCwIcon,
  Settings2Icon,
  TriangleAlertIcon,
  Undo2Icon,
  WrenchIcon,
} from 'lucide-vue-next'
import type { LocationQueryRaw } from 'vue-router'
import { computed, reactive, ref, watch } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '设备报警',
    requiredPermissions: ['business.iiot.alarms.read'],
  },
})

const router = useRouter()
const route = useRoute()
const auth = useAuthStore()
const {
  acknowledgeAlarm,
  alarms,
  alarmsError,
  alarmsPending,
  refreshAlarms,
  shelveAlarm,
  unshelveAlarm,
} = useBusinessEquipmentAlarms()

const errorMessage = computed(() => formatError(alarmsError.value))
const criticalCount = computed(
  () =>
    alarms.value.filter((a) => ['critical', 'blocked'].includes((a.severity ?? '').toLowerCase()))
      .length,
)
const warningCount = computed(
  () => alarms.value.filter((a) => (a.severity ?? '').toLowerCase() === 'warning').length,
)
const shelvedCount = computed(() => alarms.value.filter((a) => isShelved(a)).length)
const escalatedCount = computed(() => alarms.value.filter((a) => Boolean(a.escalatedAtUtc)).length)
const permissionCodes = computed(() => auth.principal?.permissionCodes ?? [])
const canManageAlarms = computed(() => permissionCodes.value.includes(P.iiotAlarmsWrite))
const currentActor = computed(
  () => auth.principal?.loginName ?? auth.principal?.principalId ?? 'business-console',
)

type Alarm = (typeof alarms)['value'][number]

// ── Shelve duration presets (issue #795: 硬编码 30m → 选项组) ──
const DURATION_PRESETS = [
  { value: '30', label: '30 分钟' },
  { value: '120', label: '2 小时' },
  { value: '480', label: '8 小时' },
  { value: 'custom', label: '自定义' },
] as const

function alarmKey(row: Alarm) {
  return row.alarmEventId ?? `${row.deviceAssetId}-${row.alarmCode}`
}

const columns: NvDataTableColumn<Alarm>[] = [
  {
    key: 'alarmEventId',
    header: '报警',
    cellClass: 'font-medium',
    accessor: (r) => r.alarmEventId ?? '无编号',
  },
  { key: 'deviceAssetId', header: '设备', accessor: (r) => r.deviceAssetId ?? '无设备' },
  { key: 'alarmCode', header: '报警代码', accessor: (r) => r.alarmCode ?? '无代码' },
  { key: 'severity', header: '级别', width: 'w-24' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'lifecycle', header: '处置', width: 'w-64' },
  { key: 'raisedAtUtc', header: '发生时间', width: 'w-44' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-12' },
]

// Quick-filter views. 升级 is ORTHOGONAL to disposition — an escalated alarm may also be
// acknowledged/shelved, so each view is an independent predicate (a row can match several)
// rather than one mutually-exclusive bucket. This keeps escalated+acknowledged alarms in
// 已确认 with their acknowledger visible.
const VIEW_TABS = [
  { value: 'all', label: '全部' },
  { value: 'escalated', label: '已升级' },
  { value: 'raised', label: '待确认' },
  { value: 'shelved', label: '已搁置' },
  { value: 'acknowledged', label: '已确认' },
] as const
type AlarmView = (typeof VIEW_TABS)[number]['value']
const DEFAULT_PAGE_SIZE = 10

function queryView(): AlarmView {
  return VIEW_TABS.find((v) => v.value === route.query.view)?.value ?? 'all'
}
function queryInt(key: string, fallback: number): number {
  const raw = route.query[key]
  const n = typeof raw === 'string' ? Number.parseInt(raw, 10) : Number.NaN
  return Number.isInteger(n) && n > 0 ? n : fallback
}

// View + pagination are the controlled source of truth and round-trip through the URL
// (§5.3 双向同步:view / page / pageSize).
const activeView = ref<AlarmView>(queryView())
const page = ref(queryInt('page', 1))
const pageSize = ref(queryInt('pageSize', DEFAULT_PAGE_SIZE))

function matchesView(row: Alarm, view: AlarmView): boolean {
  switch (view) {
    case 'escalated':
      return Boolean(row.escalatedAtUtc)
    case 'shelved':
      return isShelved(row)
    case 'acknowledged':
      return Boolean(row.acknowledgedAtUtc)
    case 'raised':
      return !row.acknowledgedAtUtc && !isShelved(row)
    default:
      return true
  }
}
const viewCounts = computed<Record<AlarmView, number>>(() => {
  const counts: Record<AlarmView, number> = {
    all: 0,
    escalated: 0,
    raised: 0,
    shelved: 0,
    acknowledged: 0,
  }
  for (const view of VIEW_TABS)
    counts[view.value] = alarms.value.filter((a) => matchesView(a, view.value)).length
  return counts
})
const filteredAlarms = computed(() => alarms.value.filter((a) => matchesView(a, activeView.value)))
const totalItems = computed(() => filteredAlarms.value.length)
// Slice here (manual pagination) so page/pageSize are parent-owned and URL-syncable.
const pagedAlarms = computed(() => {
  const start = (page.value - 1) * pageSize.value
  return filteredAlarms.value.slice(start, start + pageSize.value)
})

// Explicit view switch (user click): reset to page 1 + prune the selection to the now-visible
// rows so bulk actions never touch rows hidden by the current view (误操作 guard). Page reset
// lives here (not in a watcher) so URL-driven view changes keep their own page from the query.
function selectView(view: AlarmView) {
  activeView.value = view
  page.value = 1
}
watch(activeView, () => {
  const visible = new Set<string | number>(filteredAlarms.value.map(alarmKey))
  selectedKeys.value = selectedKeys.value.filter((k) => visible.has(k))
})
function updatePageSize(size: number) {
  pageSize.value = size
  page.value = 1
}
// Clamp the page if the filtered set shrank below the current window.
watch(totalItems, (total) => {
  const maxPage = Math.max(1, Math.ceil(total / pageSize.value))
  if (page.value > maxPage) page.value = maxPage
})

// State → URL (default values are omitted from the query).
watch([activeView, page, pageSize], () => {
  const query: LocationQueryRaw = { ...route.query }
  if (activeView.value === 'all') delete query.view
  else query.view = activeView.value
  if (page.value > 1) query.page = String(page.value)
  else delete query.page
  if (pageSize.value !== DEFAULT_PAGE_SIZE) query.pageSize = String(pageSize.value)
  else delete query.pageSize
  if (JSON.stringify(query) !== JSON.stringify(route.query)) void router.replace({ query })
})
// URL → State (external navigation / browser back-forward reflect back into the view).
watch(
  () => [route.query.view, route.query.page, route.query.pageSize],
  () => {
    const nextView = queryView()
    if (nextView !== activeView.value) activeView.value = nextView
    const nextPage = queryInt('page', 1)
    if (nextPage !== page.value) page.value = nextPage
    const nextSize = queryInt('pageSize', DEFAULT_PAGE_SIZE)
    if (nextSize !== pageSize.value) pageSize.value = nextSize
  },
)

// De-emphasize resolved rows (已确认 / 已搁置), but never dim escalated ones — they
// must stay identifiable at a glance (验收: 升级报警 3 秒内可辨识).
function rowClass(row: Alarm) {
  if (row.escalatedAtUtc) return undefined
  return isShelved(row) || row.acknowledgedAtUtc ? 'opacity-55' : undefined
}

function severityLabel(value?: string | null) {
  const labels: Record<string, string> = {
    blocked: '阻塞',
    critical: '严重',
    info: '信息',
    warning: '预警',
  }
  return value ? (labels[value.toLowerCase()] ?? value) : '未知'
}
function severityVariant(value?: string | null) {
  const severity = value?.toLowerCase()
  if (severity === 'critical' || severity === 'blocked') return 'danger'
  if (severity === 'warning') return 'warning'
  return 'neutral'
}
function statusLabel(value?: string | null) {
  const labels: Record<string, string> = {
    acknowledged: '已确认',
    cleared: '已解除',
    raised: '已触发',
    shelved: '已搁置',
  }
  return value ? (labels[value.toLowerCase()] ?? value) : '未知'
}
function statusVariant(value?: string | null) {
  const status = value?.toLowerCase()
  if (status === 'cleared') return 'success'
  if (status === 'shelved') return 'warning'
  if (status === 'acknowledged') return 'neutral'
  return 'danger'
}
function isShelved(row: Alarm) {
  return (row.status ?? '').toLowerCase() === 'shelved'
}
function canAcknowledge(row: Alarm) {
  return (
    canManageAlarms.value &&
    Boolean(row.alarmEventId) &&
    !row.acknowledgedAtUtc &&
    (row.status ?? '').toLowerCase() !== 'cleared'
  )
}
function canShelve(row: Alarm) {
  return (
    canManageAlarms.value &&
    Boolean(row.alarmEventId) &&
    !isShelved(row) &&
    (row.status ?? '').toLowerCase() !== 'cleared'
  )
}
function canUnshelve(row: Alarm) {
  return canManageAlarms.value && Boolean(row.alarmEventId) && isShelved(row)
}
// Disposition only. 升级 is surfaced separately by the row icon/tooltip (orthogonal). An alarm
// can be BOTH shelved and acknowledged (canAcknowledge does not exclude shelved), so show both
// facts — otherwise such a row would appear in 已确认 without its acknowledger (#795).
function lifecycleLabel(row: Alarm) {
  const parts: string[] = []
  if (isShelved(row)) {
    const by = row.shelvedBy ? ` · ${row.shelvedBy}` : ''
    parts.push(`搁置至 ${formatDateTime(row.shelvedUntilUtc)}${by}`)
  }
  if (row.acknowledgedAtUtc) {
    const by = row.acknowledgedBy ? ` · ${row.acknowledgedBy}` : ''
    parts.push(`确认于 ${formatDateTime(row.acknowledgedAtUtc)}${by}`)
  }
  return parts.length ? parts.join('；') : '待确认'
}
function escalationTooltip(row: Alarm) {
  const lines = [`升级时间：${formatDateTime(row.escalatedAtUtc)}`]
  if (row.escalationReason) lines.push(`原因：${row.escalationReason}`)
  const chain = row.escalationRecipientRefs?.filter(Boolean) ?? []
  if (chain.length) lines.push(`升级链：${chain.join(' → ')}`)
  return lines.join('\n')
}

// ── Selection + batch actions (A1 §5.2 首个批量模式落地) ──
const selectedKeys = ref<(string | number)[]>([])
const selectedAlarms = computed(() =>
  alarms.value.filter((a) => selectedKeys.value.includes(alarmKey(a))),
)
const ackTargets = computed(() => selectedAlarms.value.filter(canAcknowledge))
const shelveTargets = computed(() => selectedAlarms.value.filter(canShelve))
function clearSelection() {
  selectedKeys.value = []
}

async function handleAcknowledge(row: Alarm) {
  if (!row.alarmEventId) return notifyError('报警编号缺失，无法确认。')
  try {
    await acknowledgeAlarm(row.alarmEventId, currentActor.value)
    notifySuccess('报警已确认。')
    await refreshAlarms().catch(() => {})
  } catch (error) {
    notifyError(error, '报警确认失败，请稍后重试。')
  }
}
async function handleUnshelve(row: Alarm) {
  if (!row.alarmEventId) return notifyError('报警编号缺失，无法解除搁置。')
  try {
    await unshelveAlarm(row.alarmEventId)
    notifySuccess('报警搁置已解除。')
    await refreshAlarms().catch(() => {})
  } catch (error) {
    notifyError(error, '解除搁置失败，请稍后重试。')
  }
}

// ── Batch acknowledge (confirm + 条数复述; ack 是 first-write-wins 故天然幂等) ──
const batchAck = reactive({ open: false, submitting: false })
function openBatchAck() {
  if (!ackTargets.value.length) return
  batchAck.open = true
}
async function confirmBatchAck() {
  const targets = ackTargets.value
  if (!targets.length) return
  batchAck.submitting = true
  try {
    const results = await Promise.allSettled(
      targets.map((row) => acknowledgeAlarm(row.alarmEventId!, currentActor.value)),
    )
    // Commit the failed-row retention before the best-effort refresh so a refresh failure
    // cannot strand it (A1 §5.2); acknowledge is first-write-wins, so re-confirming is safe.
    const failed = targets.filter((_, i) => results[i].status === 'rejected')
    selectedKeys.value = failed.length ? failed.map(alarmKey) : []
    batchAck.open = false
    summarizeBatch('确认', results)
    await refreshAlarms().catch(() => {})
  } finally {
    batchAck.submitting = false
  }
}

// ── Shelve dialog (shared by single-row + batch; duration option group) ──
// Backend clamps DurationMinutes to 1..1440 (ShelveAlarmCommandValidator.InclusiveBetween).
const MAX_SHELVE_MINUTES = 24 * 60
const shelve = reactive({
  open: false,
  targets: [] as Alarm[],
  duration: '30' as string,
  customMinutes: 60,
  reason: '',
  submitting: false,
  // Shelve instant frozen once per batch intent (see openShelve) so a partial-failure
  // retry reuses the same window + idempotency keys.
  batchAtUtc: '',
  // Set after a submit leaves failed rows: locks duration/reason and blocks dialog close so
  // the frozen shelvedAtUtc/key/payload cannot drift on retry (idempotent no-op preserved).
  // Cleared only by a full success or an explicit 放弃重试.
  locked: false,
})
const resolvedMinutes = computed(() =>
  shelve.duration === 'custom' ? Math.trunc(Number(shelve.customMinutes)) : Number(shelve.duration),
)
const shelveIsBatch = computed(() => shelve.targets.length > 1)
// 批量搁置是「对多行做同一破坏性动作」→ A1 §5.2 要求原因必填;单条搁置保持可选。
const reasonRequired = computed(() => shelveIsBatch.value)
// Field-level validation (inline red text, not toast — 见 feedback-and-notifications 规范).
const customMinutesError = computed(() => {
  if (shelve.duration !== 'custom') return ''
  const n = Number(shelve.customMinutes)
  if (!Number.isInteger(n) || n < 1 || n > MAX_SHELVE_MINUTES) {
    return `请输入 1–${MAX_SHELVE_MINUTES} 之间的整数分钟。`
  }
  return ''
})
const reasonError = computed(() =>
  reasonRequired.value && !shelve.reason.trim() ? '批量搁置需填写搁置原因。' : '',
)
const shelveInvalid = computed(
  () => Boolean(customMinutesError.value) || Boolean(reasonError.value),
)

function openShelve(rows: Alarm[]) {
  const targets = rows.filter(canShelve)
  if (!targets.length) return
  shelve.targets = targets
  shelve.duration = '30'
  shelve.customMinutes = 60
  shelve.reason = ''
  shelve.locked = false
  // Freeze the shelve instant for the whole batch intent: the backend derives the shelve
  // window from ShelvedAtUtc and de-dupes on the idempotency key (first-write-wins), so a
  // retry of the failed rows with the SAME frozen key is a no-op, not a re-shelve.
  shelve.batchAtUtc = new Date().toISOString()
  shelve.open = true
}
async function confirmShelve() {
  const targets = shelve.targets
  if (!targets.length || shelveInvalid.value) return
  const minutes = resolvedMinutes.value
  const shelvedAtUtc = shelve.batchAtUtc || new Date().toISOString()
  const reason = shelve.reason.trim() || 'operator-shelved'
  shelve.submitting = true
  try {
    const results = await Promise.allSettled(
      targets.map((row) =>
        shelveAlarm(row.alarmEventId!, currentActor.value, minutes, reason, {
          shelvedAtUtc,
          idempotencyKey: `shelve:${row.alarmEventId}:${shelvedAtUtc}:${minutes}`,
        }),
      ),
    )
    // Commit the failed-row / lock state BEFORE refreshing: keep only the failed rows selected
    // + queued, lock the payload (frozen shelvedAtUtc/minutes/reason/key) and block dialog close
    // so a retry stays an idempotent no-op even if the first attempt actually succeeded on the
    // backend but the response was lost (A1 §5.2「失败行可定位」+ 稳定重试). Refresh is a
    // best-effort last step — its failure must never strand the retry state.
    const failed = targets.filter((_, i) => results[i].status === 'rejected')
    if (failed.length) {
      shelve.targets = failed
      selectedKeys.value = failed.map(alarmKey)
      shelve.locked = true
    } else {
      clearSelection()
      shelve.locked = false
      shelve.open = false
    }
    summarizeBatch('搁置', results)
    await refreshAlarms().catch(() => {})
  } finally {
    shelve.submitting = false
  }
}
// Give up the frozen retry intent explicitly (the only way out of a locked dialog).
function abandonShelve() {
  shelve.locked = false
  shelve.batchAtUtc = ''
  shelve.targets = []
  clearSelection()
  shelve.open = false
}
// Block close (Esc / overlay / 取消) while locked so the frozen intent cannot be lost; a
// fresh open resets the lock. Explicit 放弃重试 uses abandonShelve.
function onShelveOpenChange(open: boolean) {
  if (!open && shelve.locked) return
  shelve.open = open
}

function summarizeBatch(action: string, results: PromiseSettledResult<unknown>[]) {
  const ok = results.filter((r) => r.status === 'fulfilled').length
  const failed = results.length - ok
  if (failed === 0) {
    notifySuccess(`已${action} ${ok} 条报警。`)
  } else if (ok === 0) {
    notifyError(`${action}失败 ${failed} 条，请稍后重试。`)
  } else {
    notifyError(`${action}成功 ${ok} 条，失败 ${failed} 条，请稍后重试失败项。`)
  }
}

function recordDowntime(deviceAssetId?: string | null) {
  void router.push({ path: '/mes/downtime', query: { deviceAssetId: deviceAssetId ?? undefined } })
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
    <NvPageHeader
      title="设备报警"
      :breadcrumbs="[{ label: '设备监控（IoT）' }]"
      :count="`${alarms.length} 条未解除`"
    >
      <template #actions>
        <NvButton size="sm" type="button" variant="outline" as-child>
          <RouterLink to="/equipment">设备看板</RouterLink>
        </NvButton>
        <NvButton size="sm" type="button" variant="outline" as-child>
          <RouterLink to="/equipment/telemetry/alarm-rules"
            ><Settings2Icon aria-hidden="true" />报警规则</RouterLink
          >
        </NvButton>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="alarmsPending"
          @click="refreshAlarms"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <NvSectionCards :columns="4">
      <NvSectionCard description="报警数量" :value="alarms.length" hint="当前未解除" />
      <NvSectionCard description="严重报警" :value="criticalCount" hint="需立即处理" />
      <NvSectionCard description="预警报警" :value="warningCount" hint="需要跟踪" />
      <NvSectionCard
        description="升级 / 搁置"
        :value="`${escalatedCount} / ${shelvedCount}`"
        hint="当前处置状态"
      />
    </NvSectionCards>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <!-- 快捷筛选:升级是与处置正交的独立视图(可与已确认/已搁置重叠) -->
    <div class="flex flex-wrap items-center gap-1.5" role="tablist" aria-label="报警筛选">
      <NvButton
        v-for="view in VIEW_TABS"
        :key="view.value"
        size="sm"
        type="button"
        role="tab"
        :variant="activeView === view.value ? 'default' : 'ghost'"
        :aria-selected="activeView === view.value"
        @click="selectView(view.value)"
      >
        {{ view.label }}
        <span class="ms-1 text-xs tabular-nums opacity-70">{{ viewCounts[view.value] }}</span>
      </NvButton>
    </div>

    <NvDataTable
      v-model:selected="selectedKeys"
      :columns="columns"
      :rows="pagedAlarms"
      :row-key="alarmKey"
      :row-class="rowClass"
      :loading="alarmsPending"
      :searchable="false"
      :column-settings="false"
      :selectable="canManageAlarms"
      manual
      :page="page"
      :total-items="totalItems"
      :page-size="pageSize"
      empty-message="当前没有未解除设备报警。"
      @update:page="page = $event"
      @update:page-size="updatePageSize"
    >
      <template #bulk-actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="!ackTargets.length"
          @click="openBatchAck"
        >
          <CheckCircle2Icon aria-hidden="true" />
          批量确认{{ ackTargets.length ? ` (${ackTargets.length})` : '' }}
        </NvButton>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="!shelveTargets.length"
          @click="openShelve(shelveTargets)"
        >
          <BellOffIcon aria-hidden="true" />
          批量搁置{{ shelveTargets.length ? ` (${shelveTargets.length})` : '' }}
        </NvButton>
      </template>
      <template #cell-alarmEventId="{ row }">
        <div class="flex items-center gap-1.5">
          <NvTooltipProvider v-if="row.escalatedAtUtc">
            <NvTooltip>
              <NvTooltipTrigger as-child>
                <TriangleAlertIcon
                  class="size-4 shrink-0 text-destructive"
                  aria-label="报警已升级"
                />
              </NvTooltipTrigger>
              <NvTooltipContent>
                <span class="whitespace-pre-line">{{ escalationTooltip(row) }}</span>
              </NvTooltipContent>
            </NvTooltip>
          </NvTooltipProvider>
          <span>{{ row.alarmEventId ?? '无编号' }}</span>
        </div>
      </template>
      <template #cell-deviceAssetId="{ row }">
        <RouterLink
          :to="`/equipment/${row.deviceAssetId}`"
          class="text-brand underline-offset-4 hover:underline"
          >{{ row.deviceAssetId ?? '无设备' }}</RouterLink
        >
      </template>
      <template #cell-severity="{ row }">
        <NvBadge class="rounded-sm" :variant="severityVariant(row.severity)">{{
          severityLabel(row.severity)
        }}</NvBadge>
      </template>
      <template #cell-status="{ row }">
        <NvBadge class="rounded-sm" :variant="statusVariant(row.status)">{{
          statusLabel(row.status)
        }}</NvBadge>
      </template>
      <template #cell-lifecycle="{ row }">
        <span class="text-sm text-muted-foreground">{{ lifecycleLabel(row) }}</span>
      </template>
      <template #cell-raisedAtUtc="{ row }">{{ formatDateTime(row.raisedAtUtc) }}</template>
      <template #cell-actions="{ row }">
        <NvRowActions :label="`报警操作 ${row.alarmEventId ?? ''}`">
          <NvDropdownMenuItem v-if="canAcknowledge(row)" @click="handleAcknowledge(row)">
            <CheckCircle2Icon aria-hidden="true" />
            确认报警
          </NvDropdownMenuItem>
          <NvDropdownMenuItem v-if="canShelve(row)" @click="openShelve([row])">
            <BellOffIcon aria-hidden="true" />
            搁置报警…
          </NvDropdownMenuItem>
          <NvDropdownMenuItem v-if="canUnshelve(row)" @click="handleUnshelve(row)">
            <Undo2Icon aria-hidden="true" />
            解除搁置
          </NvDropdownMenuItem>
          <NvDropdownMenuItem as-child>
            <RouterLink :to="`/equipment/${row.deviceAssetId}`"
              ><EyeIcon aria-hidden="true" />设备详情</RouterLink
            >
          </NvDropdownMenuItem>
          <NvDropdownMenuItem as-child>
            <RouterLink
              :to="{
                path: '/equipment/telemetry/history',
                query: { deviceAssetId: row.deviceAssetId },
              }"
            >
              <LineChartIcon aria-hidden="true" />
              历史趋势
            </RouterLink>
          </NvDropdownMenuItem>
          <NvDropdownMenuItem @click="recordDowntime(row.deviceAssetId)">
            <WrenchIcon aria-hidden="true" />
            记录停机
          </NvDropdownMenuItem>
          <NvDropdownMenuItem as-child>
            <RouterLink
              :to="{
                path: '/maintenance/work-orders',
                query: { deviceAssetId: row.deviceAssetId, sourceAlarmId: row.alarmEventId },
              }"
            >
              <WrenchIcon aria-hidden="true" />
              创建维修工单
            </RouterLink>
          </NvDropdownMenuItem>
        </NvRowActions>
      </template>
    </NvDataTable>

    <!-- 搁置对话框:时长选项组(30m/2h/8h/自定义) + 原因(批量必填);失败重试时锁定字段+禁关闭 -->
    <NvDialog :open="shelve.open" @update:open="onShelveOpenChange">
      <NvDialogContent class="sm:max-w-lg">
        <NvDialogHeader>
          <NvDialogTitle>{{
            shelveIsBatch ? `批量搁置 ${shelve.targets.length} 条报警` : '搁置报警'
          }}</NvDialogTitle>
          <NvDialogDescription>
            <template v-if="shelve.locked"
              >已提交，{{
                shelve.targets.length
              }}
              条搁置失败。请原样重试（时长/原因已锁定以复用相同幂等键），或放弃重试。</template
            >
            <template v-else
              >搁置期间该报警暂不再提醒；到期后自动恢复。请选择搁置时长{{
                reasonRequired ? '并填写原因' : ''
              }}。</template
            >
          </NvDialogDescription>
        </NvDialogHeader>

        <div class="grid gap-4 py-1">
          <NvField>
            <NvFieldLabel>搁置时长</NvFieldLabel>
            <NvRadioGroup
              v-model="shelve.duration"
              :disabled="shelve.locked"
              class="grid grid-cols-2 gap-2.5"
            >
              <NvRadioGroupItem
                v-for="preset in DURATION_PRESETS"
                :key="preset.value"
                :value="preset.value"
                >{{ preset.label }}</NvRadioGroupItem
              >
            </NvRadioGroup>
          </NvField>

          <NvField v-if="shelve.duration === 'custom'">
            <NvFieldLabel for="shelve-custom-minutes"
              >自定义分钟数（1–{{ MAX_SHELVE_MINUTES }}）</NvFieldLabel
            >
            <NvInput
              id="shelve-custom-minutes"
              v-model="shelve.customMinutes"
              type="number"
              min="1"
              :max="MAX_SHELVE_MINUTES"
              step="1"
              class="w-40"
              placeholder="分钟"
              :disabled="shelve.locked"
              :invalid="Boolean(customMinutesError)"
              :aria-invalid="customMinutesError ? 'true' : undefined"
              :aria-describedby="customMinutesError ? 'shelve-custom-minutes-error' : undefined"
            />
            <NvFieldError
              v-if="customMinutesError"
              id="shelve-custom-minutes-error"
              :errors="[customMinutesError]"
            />
          </NvField>

          <NvField>
            <NvFieldLabel for="shelve-reason"
              >搁置原因{{ reasonRequired ? '（必填）' : '（可选）' }}</NvFieldLabel
            >
            <NvInput
              id="shelve-reason"
              v-model="shelve.reason"
              placeholder="例如：等待备件到货 / 计划内检修"
              :disabled="shelve.locked"
              :invalid="Boolean(reasonError)"
              :aria-invalid="reasonError ? 'true' : undefined"
              :aria-describedby="reasonError ? 'shelve-reason-error' : undefined"
            />
            <NvFieldError v-if="reasonError" id="shelve-reason-error" :errors="[reasonError]" />
          </NvField>
        </div>

        <NvDialogFooter>
          <NvButton
            v-if="shelve.locked"
            type="button"
            variant="outline"
            :disabled="shelve.submitting"
            @click="abandonShelve"
            >放弃重试</NvButton
          >
          <NvButton v-else type="button" variant="outline" @click="onShelveOpenChange(false)"
            >取消</NvButton
          >
          <NvButton
            type="button"
            :disabled="shelve.submitting || shelveInvalid"
            @click="confirmShelve"
          >
            {{
              shelve.locked
                ? `重试 ${shelve.targets.length} 条`
                : shelveIsBatch
                  ? `搁置 ${shelve.targets.length} 条`
                  : '确认搁置'
            }}
          </NvButton>
        </NvDialogFooter>
      </NvDialogContent>
    </NvDialog>

    <!-- 批量确认:AlertDialog + 条数复述 -->
    <NvAlertDialog v-model:open="batchAck.open">
      <NvAlertDialogContent>
        <NvAlertDialogHeader>
          <NvAlertDialogTitle>批量确认报警</NvAlertDialogTitle>
          <NvAlertDialogDescription>
            将确认选中的
            {{ ackTargets.length }} 条报警。确认后记录当前操作人与时间，可重复执行且不影响已确认项。
          </NvAlertDialogDescription>
        </NvAlertDialogHeader>
        <NvAlertDialogFooter>
          <NvAlertDialogCancel :disabled="batchAck.submitting">取消</NvAlertDialogCancel>
          <NvAlertDialogAction :disabled="batchAck.submitting" @click="confirmBatchAck">
            确认 {{ ackTargets.length }} 条
          </NvAlertDialogAction>
        </NvAlertDialogFooter>
      </NvAlertDialogContent>
    </NvAlertDialog>
  </BusinessLayout>
</template>
