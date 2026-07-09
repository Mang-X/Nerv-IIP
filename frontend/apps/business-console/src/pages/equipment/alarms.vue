<script setup lang="ts">
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { useBusinessEquipmentAlarms } from '@/composables/useBusinessEquipment'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { useAuthStore } from '@/stores/auth'
import { notifyError, notifySuccess } from '@/utils/notify'
import {
  NvBadge,
  NvButton,
  NvDataTable,
  NvDropdownMenuItem,
  NvPageHeader,
  NvRowActions,
  NvSectionCard,
  NvSectionCards,
} from '@nerv-iip/ui'
import {
  BellOffIcon,
  CheckCircle2Icon,
  EyeIcon,
  LineChartIcon,
  RefreshCwIcon,
  Settings2Icon,
  Undo2Icon,
  WrenchIcon,
} from 'lucide-vue-next'
import { computed } from 'vue'
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
  { key: 'lifecycle', header: '处置', width: 'w-56' },
  { key: 'raisedAtUtc', header: '发生时间', width: 'w-44' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-12' },
]

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
  if (isShelved(row)) return `搁置至 ${formatDateTime(row.shelvedUntilUtc)}`
  if (row.acknowledgedAtUtc) return `确认于 ${formatDateTime(row.acknowledgedAtUtc)}`
  return '待确认'
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
async function handleShelve(row: Alarm) {
  if (!row.alarmEventId) return notifyError('报警编号缺失，无法搁置。')
  try {
    await shelveAlarm(row.alarmEventId, currentActor.value, 30, 'operator-shelved')
    notifySuccess('报警已搁置 30 分钟。')
    await refreshAlarms()
  } catch (error) {
    notifyError(error, '报警搁置失败，请稍后重试。')
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
      :columns="columns"
      :rows="alarms"
      :row-key="(r) => r.alarmEventId ?? `${r.deviceAssetId}-${r.alarmCode}`"
      :loading="alarmsPending"
      :searchable="false"
      :column-settings="false"
      empty-message="当前没有未解除设备报警。"
    >
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
          <NvDropdownMenuItem v-if="canShelve(row)" @click="handleShelve(row)">
            <BellOffIcon aria-hidden="true" />
            搁置 30 分钟
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
  </BusinessLayout>
</template>
