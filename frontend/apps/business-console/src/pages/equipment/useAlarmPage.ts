import { statusActionGate } from '@nerv-iip/business-core'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import {
  LifecycleStateChangedError,
  recoverLifecycleAction,
} from '@/composables/shared/lifecycleAction'
import { useBusinessEquipmentAlarms } from '@/composables/equipment/useBusinessEquipment'
import { useMasterDataDisplayNames } from '@/composables/master-data/useMasterDataDisplayNames'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { useAuthStore } from '@/stores/auth'
import {
  inlineErrorMessage,
  notifyError,
  notifyOperationFailure,
  notifySuccess,
} from '@/utils/notify'
import type { LocationQueryRaw } from 'vue-router'
import { computed, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'

export function useAlarmPage() {
  const router = useRouter()
  const route = useRoute()
  const auth = useAuthStore()
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
  const MAX_PAGE_SIZE = 500

  function queryView(): AlarmView {
    return VIEW_TABS.find((v) => v.value === route.query.view)?.value ?? 'all'
  }
  function queryInt(key: string, fallback: number): number {
    const raw = route.query[key]
    const n = typeof raw === 'string' ? Number.parseInt(raw, 10) : Number.NaN
    return Number.isSafeInteger(n) && n > 0 ? n : fallback
  }

  // View + pagination are the controlled source of truth and round-trip through the URL
  // (§5.3 双向同步:view / page / pageSize).
  const activeView = ref<AlarmView>(queryView())
  const page = ref(queryInt('page', 1))
  const pageSize = ref(Math.min(queryInt('pageSize', DEFAULT_PAGE_SIZE), MAX_PAGE_SIZE))

  const {
    acknowledgeAlarm,
    alarms,
    alarmsError,
    alarmsPending,
    alarmsTotal,
    refreshAlarms,
    shelveAlarm,
    unshelveAlarm,
  } = useBusinessEquipmentAlarms(() => ({
    status:
      activeView.value === 'all'
        ? 'active'
        : activeView.value === 'acknowledged'
          ? 'active-acknowledged'
          : activeView.value,
    skip: Math.min((page.value - 1) * pageSize.value, 2147483647),
    take: pageSize.value,
  }))

  const errorMessage = computed(() => inlineErrorMessage(alarmsError.value))
  const permissionCodes = computed(() => auth.principal?.permissionCodes ?? [])
  const canManageAlarms = computed(() => permissionCodes.value.includes(P.iiotAlarmsWrite))
  const canCreateMaintenanceWorkOrder = computed(() =>
    permissionCodes.value.includes(P.maintenanceWorkOrdersManage),
  )
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

  function historyQuery(row: Alarm): LocationQueryRaw {
    const raisedAt = row.raisedAtUtc ? new Date(row.raisedAtUtc) : undefined
    if (!raisedAt || Number.isNaN(raisedAt.getTime())) {
      return { deviceAssetId: row.deviceAssetId }
    }

    const windowStart = new Date(raisedAt)
    windowStart.setHours(windowStart.getHours() - 4)
    const clearedAt = row.clearedAtUtc ? new Date(row.clearedAtUtc) : undefined
    const windowEnd =
      clearedAt && !Number.isNaN(clearedAt.getTime()) ? clearedAt : new Date(raisedAt)
    if (!clearedAt || Number.isNaN(clearedAt.getTime()))
      windowEnd.setHours(windowEnd.getHours() + 4)

    return {
      deviceAssetId: row.deviceAssetId,
      windowEndUtc: windowEnd.toISOString(),
      windowStartUtc: windowStart.toISOString(),
    }
  }

  // 报警读面只回设备编号（DEV-CNC-01），设备名在主数据里，按编号 join 出中文名。
  const { resolveDevice } = useMasterDataDisplayNames({ devices: true })
  /** 设备展示串：名称优先，名录查不到就只显编号，不编名字。 */
  function deviceLabel(code?: string | null, fallback = '无设备') {
    if (!code) return fallback
    return resolveDevice(code) ?? code
  }
  /** 报警单号：alarmEventId 是系统 GUID，屏上用现场认的外部报警号。 */
  function alarmNo(row: Alarm) {
    return row.externalAlarmId ?? '无报警号'
  }

  const columns: NvDataTableColumn<Alarm>[] = [
    {
      key: 'alarmEventId',
      header: '报警',
      cellClass: 'font-medium',
      accessor: (r) => alarmNo(r),
    },
    {
      key: 'deviceAssetId',
      header: '设备',
      accessor: (r) =>
        resolveDevice(r.deviceAssetId)
          ? `${resolveDevice(r.deviceAssetId)} ${r.deviceAssetId}`
          : (r.deviceAssetId ?? '无设备'),
    },
    { key: 'alarmCode', header: '报警代码', accessor: (r) => r.alarmCode ?? '未设报警代码' },
    { key: 'severity', header: '级别', width: 'w-24' },
    { key: 'status', header: '状态', width: 'w-24' },
    { key: 'lifecycle', header: '处置', width: 'w-64' },
    { key: 'raisedAtUtc', header: '发生时间', width: 'w-44' },
    { key: 'actions', header: '操作', align: 'end', width: 'w-12' },
  ]

  const totalItems = computed(() => alarmsTotal.value ?? 0)

  function selectView(view: AlarmView) {
    activeView.value = view
    page.value = 1
  }
  watch([activeView, page, pageSize], () => {
    selectedKeys.value = []
  })

  function updatePageSize(size: number) {
    pageSize.value = size
    page.value = 1
  }
  // Clamp the page whenever page / pageSize / total change — including the initial load and an
  // out-of-range page/pageSize arriving from the URL or browser back-forward — so an oversized
  // page can never render an empty table. The clamped value is normalized back into the query by
  // the state→URL watcher below.
  watch(
    [alarmsTotal, alarmsPending],
    () => {
      if (alarmsPending.value || alarmsTotal.value === undefined) return
      const maxPage = Math.max(1, Math.ceil(totalItems.value / pageSize.value))
      if (page.value > maxPage) page.value = maxPage
    },
    { immediate: true },
  )

  // State → URL (default values omitted). Runs on load too, so a clamped/normalized page or a
  // pageSize read from the query is written back and the URL stays canonical.
  watch(
    [activeView, page, pageSize],
    () => {
      const query: LocationQueryRaw = { ...route.query }
      if (activeView.value === 'all') delete query.view
      else query.view = activeView.value
      if (page.value > 1) query.page = String(page.value)
      else delete query.page
      if (pageSize.value !== DEFAULT_PAGE_SIZE) query.pageSize = String(pageSize.value)
      else delete query.pageSize
      if (JSON.stringify(query) !== JSON.stringify(route.query)) void router.replace({ query })
    },
    { immediate: true },
  )
  // URL → State (external navigation / browser back-forward reflect back into the view).
  watch(
    () => [route.query.view, route.query.page, route.query.pageSize],
    () => {
      const nextView = queryView()
      if (nextView !== activeView.value) activeView.value = nextView
      const nextPage = queryInt('page', 1)
      if (nextPage !== page.value) page.value = nextPage
      const nextSize = Math.min(queryInt('pageSize', DEFAULT_PAGE_SIZE), MAX_PAGE_SIZE)
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
      major: '重要',
      minor: '次要',
    }
    // 词表漏了就说「未知级别」，绝不把后端英文码回吐到界面上。
    return value ? (labels[value.toLowerCase()] ?? '未知级别') : '未知'
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
      escalated: '已升级',
    }
    // 词表漏了就说「未知状态」，绝不把后端英文码回吐到界面上。
    return value ? (labels[value.toLowerCase()] ?? '未知状态') : '未知'
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
  function alarmActionAllowed(row: Alarm, action: 'acknowledge' | 'shelve' | 'unshelve') {
    return statusActionGate({
      domain: 'iiot-alarm',
      action,
      facts: {
        status: row.status,
        acknowledgedAtUtc: row.acknowledgedAtUtc,
        shelvedAtUtc: row.shelvedAtUtc,
        shelvedUntilUtc: row.shelvedUntilUtc,
      },
    }).executable
  }
  function canAcknowledge(row: Alarm) {
    return (
      canManageAlarms.value && Boolean(row.alarmEventId) && alarmActionAllowed(row, 'acknowledge')
    )
  }
  function canShelve(row: Alarm) {
    return canManageAlarms.value && Boolean(row.alarmEventId) && alarmActionAllowed(row, 'shelve')
  }
  function canUnshelve(row: Alarm) {
    return canManageAlarms.value && Boolean(row.alarmEventId) && alarmActionAllowed(row, 'unshelve')
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
  function isLifecycleConflictResult(
    result: PromiseSettledResult<unknown>,
  ): result is PromiseRejectedResult {
    return result.status === 'rejected' && result.reason instanceof LifecycleStateChangedError
  }
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
      if (
        await recoverLifecycleAction(error, {
          reset: clearSelection,
          refresh: refreshAlarms,
          notify: (message) => notifyError(message),
        })
      ) {
        return
      }
      notifyOperationFailure('报警确认失败', error, '报警确认失败，请稍后重试。')
    }
  }
  async function handleUnshelve(row: Alarm) {
    if (!row.alarmEventId) return notifyError('报警编号缺失，无法解除搁置。')
    try {
      await unshelveAlarm(row.alarmEventId)
      notifySuccess('报警搁置已解除。')
      await refreshAlarms().catch(() => {})
    } catch (error) {
      if (
        await recoverLifecycleAction(error, {
          reset: clearSelection,
          refresh: refreshAlarms,
          notify: (message) => notifyError(message),
        })
      ) {
        return
      }
      notifyOperationFailure('解除搁置失败', error, '解除搁置失败，请稍后重试。')
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
      const stale = results.filter(isLifecycleConflictResult)
      const failed = targets.filter(
        (_, i) => results[i].status === 'rejected' && !isLifecycleConflictResult(results[i]),
      )
      selectedKeys.value = failed.length ? failed.map(alarmKey) : []
      batchAck.open = false
      const ordinaryResults = results.filter((result) => !isLifecycleConflictResult(result))
      if (ordinaryResults.length) summarizeBatch('确认', ordinaryResults)
      if (stale.length) {
        await recoverLifecycleAction(stale[0]!.reason, {
          reset: () => {
            batchAck.open = false
          },
          refresh: refreshAlarms,
          notify: (message) => notifyError(message),
        })
      } else {
        await refreshAlarms().catch(() => {})
      }
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
    editableFailures: [] as Alarm[],
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
    shelve.duration === 'custom'
      ? Math.trunc(Number(shelve.customMinutes))
      : Number(shelve.duration),
  )
  const shelveIsBatch = computed(() => shelve.targets.length > 1)
  // 单条搁置时，报警的既有事实一律只读带出，操作员只决定时长与原因。
  const shelveContextItems = computed(() => {
    const target = shelve.targets[0]
    if (!target) return []
    return [
      { label: '报警', value: alarmNo(target) },
      {
        label: '设备',
        value: resolveDevice(target.deviceAssetId)
          ? `${resolveDevice(target.deviceAssetId)}（${target.deviceAssetId}）`
          : target.deviceAssetId,
      },
      { label: '报警代码', value: target.alarmCode },
      { label: '级别', value: severityLabel(target.severity) },
      { label: '发生时间', value: formatDateTime(target.raisedAtUtc) },
    ]
  })
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
    shelve.editableFailures = []
    // Freeze the shelve instant for the whole batch intent: the backend derives the shelve
    // window from ShelvedAtUtc and de-dupes on the idempotency key (first-write-wins), so a
    // retry of the failed rows with the SAME frozen key is a no-op, not a re-shelve.
    shelve.batchAtUtc = new Date().toISOString()
    shelve.open = true
  }

  function errorStatus(error: unknown): number | undefined {
    if (!error || typeof error !== 'object') return undefined
    const candidate = error as {
      code?: unknown
      status?: unknown
      statusCode?: unknown
      response?: { status?: unknown }
    }
    return [
      candidate.code,
      candidate.status,
      candidate.statusCode,
      candidate.response?.status,
    ].find((value): value is number => typeof value === 'number')
  }

  function isDefinitiveClientFailure(result: PromiseSettledResult<unknown>) {
    if (result.status !== 'rejected') return false
    const status = errorStatus(result.reason)
    return status !== undefined && status >= 400 && status < 500 && status !== 409
  }

  function nextShelveIntentTimestamp(previous: string) {
    const previousAt = Date.parse(previous)
    return new Date(
      Math.max(Date.now(), Number.isFinite(previousAt) ? previousAt + 1 : 0),
    ).toISOString()
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
            attempt: shelve.locked ? 'retry' : 'initial',
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
      const stale = results.filter(isLifecycleConflictResult)
      const uncertainFailures = targets.filter(
        (_, i) =>
          results[i].status === 'rejected' &&
          !isLifecycleConflictResult(results[i]) &&
          !isDefinitiveClientFailure(results[i]),
      )
      const definitiveFailures = targets.filter((_, i) => isDefinitiveClientFailure(results[i]))
      const editableFailures = [...shelve.editableFailures, ...definitiveFailures].filter(
        (row, index, rows) =>
          rows.findIndex((candidate) => alarmKey(candidate) === alarmKey(row)) === index,
      )
      if (uncertainFailures.length) {
        shelve.targets = uncertainFailures
        shelve.editableFailures = editableFailures
        selectedKeys.value = uncertainFailures.map(alarmKey)
        shelve.locked = true
      } else if (editableFailures.length) {
        // A definitive 4xx did not enter the ambiguous response-loss path. Keep the form
        // editable, but rotate the frozen instant so a corrected payload is a new intent.
        shelve.targets = editableFailures
        shelve.editableFailures = []
        selectedKeys.value = editableFailures.map(alarmKey)
        shelve.locked = false
        shelve.batchAtUtc = nextShelveIntentTimestamp(shelvedAtUtc)
      } else {
        clearSelection()
        shelve.locked = false
        shelve.editableFailures = []
        shelve.open = false
      }
      const ordinaryResults = results.filter((result) => !isLifecycleConflictResult(result))
      if (ordinaryResults.length) summarizeBatch('搁置', ordinaryResults)
      if (stale.length) {
        await recoverLifecycleAction(stale[0]!.reason, {
          reset: () => {
            if (!uncertainFailures.length && !editableFailures.length) {
              shelve.open = false
              shelve.targets = []
              shelve.locked = false
              shelve.editableFailures = []
              clearSelection()
            }
          },
          refresh: refreshAlarms,
          notify: (message) => notifyError(message),
        })
      } else {
        await refreshAlarms().catch(() => {})
      }
    } finally {
      shelve.submitting = false
    }
  }
  // Give up the frozen retry intent explicitly (the only way out of a locked dialog).
  function abandonShelve() {
    shelve.locked = false
    shelve.batchAtUtc = ''
    shelve.targets = []
    shelve.editableFailures = []
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
    void router.push({
      path: '/mes/downtime',
      query: { deviceAssetId: deviceAssetId ?? undefined },
    })
  }
  function formatDateTime(value?: string | null) {
    if (!value) return '无'
    const date = new Date(value)
    return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
  }

  return {
    DURATION_PRESETS,
    MAX_SHELVE_MINUTES,
    VIEW_TABS,
    abandonShelve,
    ackTargets,
    activeView,
    alarmKey,
    alarmNo,
    alarms,
    alarmsPending,
    batchAck,
    canAcknowledge,
    canCreateMaintenanceWorkOrder,
    canManageAlarms,
    canShelve,
    canUnshelve,
    columns,
    confirmBatchAck,
    confirmShelve,
    customMinutesError,
    deviceLabel,
    errorMessage,
    escalationTooltip,
    formatDateTime,
    handleAcknowledge,
    handleUnshelve,
    historyQuery,
    lifecycleLabel,
    onShelveOpenChange,
    openBatchAck,
    openShelve,
    page,
    pageSize,
    reasonError,
    reasonRequired,
    recordDowntime,
    refreshAlarms,
    resolveDevice,
    rowClass,
    selectView,
    selectedKeys,
    severityLabel,
    severityVariant,
    shelve,
    shelveContextItems,
    shelveInvalid,
    shelveIsBatch,
    shelveTargets,
    statusLabel,
    statusVariant,
    totalItems,
    updatePageSize,
  }
}
