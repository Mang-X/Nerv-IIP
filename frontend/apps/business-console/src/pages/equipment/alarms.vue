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
import { computed, reactive, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '设备报警',
    requiredPermissions: ['business.iiot.alarms.read'],
  },
})

const router = useRouter()
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
  // Hidden view-state column drives the quick-filter tabs (含「已升级」快捷项).
  {
    key: 'viewState',
    header: '视图',
    accessor: (r) => viewState(r),
    filter: 'enum',
    defaultHidden: true,
    hideable: false,
  },
  { key: 'actions', header: '操作', align: 'end', width: 'w-12' },
]

const viewTabs = [
  { label: '全部', value: '' },
  { label: '已升级', value: 'escalated' },
  { label: '待确认', value: 'raised' },
  { label: '已搁置', value: 'shelved' },
  { label: '已确认', value: 'acknowledged' },
]

function viewState(row: Alarm): string {
  if (row.escalatedAtUtc) return 'escalated'
  if (isShelved(row)) return 'shelved'
  if (row.acknowledgedAtUtc) return 'acknowledged'
  return 'raised'
}
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
function lifecycleLabel(row: Alarm) {
  if (row.escalatedAtUtc) return `已升级 ${formatDateTime(row.escalatedAtUtc)}`
  if (isShelved(row)) {
    const by = row.shelvedBy ? ` · ${row.shelvedBy}` : ''
    return `搁置至 ${formatDateTime(row.shelvedUntilUtc)}${by}`
  }
  if (row.acknowledgedAtUtc) {
    const by = row.acknowledgedBy ? ` · ${row.acknowledgedBy}` : ''
    return `确认于 ${formatDateTime(row.acknowledgedAtUtc)}${by}`
  }
  return '待确认'
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
    await refreshAlarms()
  } catch (error) {
    notifyError(error, '报警确认失败，请稍后重试。')
  }
}
async function handleUnshelve(row: Alarm) {
  if (!row.alarmEventId) return notifyError('报警编号缺失，无法解除搁置。')
  try {
    await unshelveAlarm(row.alarmEventId)
    notifySuccess('报警搁置已解除。')
    await refreshAlarms()
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
    summarizeBatch('确认', results)
    await refreshAlarms()
    clearSelection()
    batchAck.open = false
  } finally {
    batchAck.submitting = false
  }
}

// ── Shelve dialog (shared by single-row + batch; duration option group) ──
const shelve = reactive({
  open: false,
  targets: [] as Alarm[],
  duration: '30' as string,
  customMinutes: 60,
  reason: '',
  submitting: false,
})
const resolvedMinutes = computed(() =>
  shelve.duration === 'custom' ? Math.trunc(Number(shelve.customMinutes)) : Number(shelve.duration),
)
const shelveIsBatch = computed(() => shelve.targets.length > 1)
// 批量搁置是「对多行做同一破坏性动作」→ A1 §5.2 要求原因必填;单条搁置保持可选。
const reasonRequired = computed(() => shelveIsBatch.value)

function openShelve(rows: Alarm[]) {
  const targets = rows.filter(canShelve)
  if (!targets.length) return
  shelve.targets = targets
  shelve.duration = '30'
  shelve.customMinutes = 60
  shelve.reason = ''
  shelve.open = true
}
async function confirmShelve() {
  const targets = shelve.targets
  if (!targets.length) return
  const minutes = resolvedMinutes.value
  if (!Number.isFinite(minutes) || minutes < 1) {
    return notifyError('请填写有效的搁置时长（≥ 1 分钟）。')
  }
  const reason = shelve.reason.trim()
  if (reasonRequired.value && !reason) {
    return notifyError('批量搁置需填写搁置原因。')
  }
  shelve.submitting = true
  try {
    // Freeze the shelve instant so retrying the same batch reuses one window; per-alarm
    // idempotency key keeps re-submits a no-op instead of extending the shelve.
    const shelvedAtUtc = new Date().toISOString()
    const effectiveReason = reason || 'operator-shelved'
    const results = await Promise.allSettled(
      targets.map((row) =>
        shelveAlarm(row.alarmEventId!, currentActor.value, minutes, effectiveReason, {
          shelvedAtUtc,
          idempotencyKey: `shelve:${row.alarmEventId}:${shelvedAtUtc}:${minutes}`,
        }),
      ),
    )
    summarizeBatch('搁置', results)
    await refreshAlarms()
    clearSelection()
    shelve.open = false
  } finally {
    shelve.submitting = false
  }
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

    <NvDataTable
      v-model:selected="selectedKeys"
      :columns="columns"
      :rows="alarms"
      :row-key="alarmKey"
      :row-class="rowClass"
      :loading="alarmsPending"
      :searchable="false"
      :column-settings="false"
      :selectable="canManageAlarms"
      :tabs="viewTabs"
      tab-key="viewState"
      empty-message="当前没有未解除设备报警。"
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

    <!-- 搁置对话框:时长选项组(30m/2h/8h/自定义) + 原因(批量必填) -->
    <NvDialog v-model:open="shelve.open">
      <NvDialogContent class="sm:max-w-lg">
        <NvDialogHeader>
          <NvDialogTitle>{{
            shelveIsBatch ? `批量搁置 ${shelve.targets.length} 条报警` : '搁置报警'
          }}</NvDialogTitle>
          <NvDialogDescription>
            搁置期间该报警暂不再提醒；到期后自动恢复。请选择搁置时长{{
              reasonRequired ? '并填写原因' : ''
            }}。
          </NvDialogDescription>
        </NvDialogHeader>

        <div class="grid gap-4 py-1">
          <NvField>
            <NvFieldLabel>搁置时长</NvFieldLabel>
            <NvRadioGroup v-model="shelve.duration" class="grid grid-cols-2 gap-2.5">
              <NvRadioGroupItem
                v-for="preset in DURATION_PRESETS"
                :key="preset.value"
                :value="preset.value"
                >{{ preset.label }}</NvRadioGroupItem
              >
            </NvRadioGroup>
          </NvField>

          <NvField v-if="shelve.duration === 'custom'">
            <NvFieldLabel for="shelve-custom-minutes">自定义分钟数</NvFieldLabel>
            <NvInput
              id="shelve-custom-minutes"
              v-model="shelve.customMinutes"
              type="number"
              min="1"
              step="1"
              class="w-40"
              placeholder="分钟"
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
            />
          </NvField>
        </div>

        <NvDialogFooter>
          <NvButton type="button" variant="outline" @click="shelve.open = false">取消</NvButton>
          <NvButton type="button" :disabled="shelve.submitting" @click="confirmShelve">
            {{ shelveIsBatch ? `搁置 ${shelve.targets.length} 条` : '确认搁置' }}
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
