<script setup lang="ts">
import type { NvDataTableColumn } from '@nerv-iip/ui'
import {
  describeEquipmentReason,
  equipmentStatusTone,
  useBusinessEquipmentDevice,
  type EquipmentTone,
} from '@/composables/useBusinessEquipment'
import {
  describeTelemetryOeeDegradation,
  describeTelemetryOeeLimitations,
  formatOeeQuantity,
  formatOeeRate,
  useBusinessTelemetryHistory,
  useBusinessTelemetryOee,
} from '@/composables/useBusinessTelemetry'
import {
  deviceControlApprovalLabel,
  deviceControlCommandTypeLabel,
  deviceControlStatusLabel,
  deviceControlStatusTone,
  useBusinessDeviceControlCommands,
} from '@/composables/useBusinessDeviceControl'
import { usePagedList } from '@/composables/usePagedList'
import DeviceControlSheet from '@/components/equipment/DeviceControlSheet.vue'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { useAuthStore } from '@/stores/auth'
import {
  useMaintenanceAvailabilityWindows,
  useMaintenanceInspections,
  useMaintenancePlans,
  useMaintenanceReliability,
  useMaintenanceSpareParts,
  useMaintenanceWorkOrders,
} from '@/composables/useBusinessMaintenance'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvBadge,
  NvButton,
  NvDataTable,
  NvPageHeader,
  NvSectionCard,
  NvSectionCards,
} from '@nerv-iip/ui'
import {
  ArrowLeftIcon,
  CalendarRangeIcon,
  ClipboardCheckIcon,
  GaugeIcon,
  LineChartIcon,
  PackageSearchIcon,
  RefreshCwIcon,
  Settings2Icon,
  SlidersHorizontalIcon,
  TrendingUpIcon,
  WrenchIcon,
} from 'lucide-vue-next'
import { computed, ref, watch } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '设备详情',
    requiredPermissions: ['business.iiot.telemetry.read'],
  },
})

const MAINTENANCE_DETAIL_TAKE = 250

const route = useRoute()
const router = useRouter()
const routeDeviceAssetId = computed(() => {
  const params = route.params as Record<string, string | string[] | undefined>
  const value = params.deviceAssetId
  return Array.isArray(value) ? value[0] : value
})

const {
  activeAlarms,
  availabilityWindows,
  device,
  deviceError,
  devicePending,
  filters,
  refreshDevice,
} = useBusinessEquipmentDevice()
const {
  filters: historyFilters,
  historyError,
  historyPending,
  visibleHistoryItems,
} = useBusinessTelemetryHistory()
const {
  filters: oeeFilters,
  oee,
  oeeError,
  oeePending,
  runtimeAvailabilityError,
} = useBusinessTelemetryOee()
const {
  availabilityError: maintenanceAvailabilityError,
  availabilityPending: maintenanceAvailabilityPending,
  availabilityWindows: maintenanceAvailabilityWindows,
  filters: maintenanceAvailabilityFilters,
} = useMaintenanceAvailabilityWindows()
const {
  filters: reliabilityFilters,
  reliability,
  reliabilityError,
  reliabilityPending,
} = useMaintenanceReliability()
const { workOrders, workOrdersError, workOrdersPending } = useMaintenanceWorkOrders({
  take: MAINTENANCE_DETAIL_TAKE,
})
const { plans, plansError, plansPending } = useMaintenancePlans({ take: MAINTENANCE_DETAIL_TAKE })
const { inspections, inspectionsError, inspectionsPending } = useMaintenanceInspections({
  take: MAINTENANCE_DETAIL_TAKE,
})
const { spareParts, sparePartsError, sparePartsPending } = useMaintenanceSpareParts({
  take: MAINTENANCE_DETAIL_TAKE,
})

const auth = useAuthStore()
const permissionCodes = computed(() => auth.principal?.permissionCodes ?? [])
const canControlDevice = computed(() => permissionCodes.value.includes(P.iiotDeviceControlWrite))
const controlSheetOpen = ref(false)
const deviceAssetIdRef = computed(() => filters.deviceAssetId)
const {
  commands: controlCommands,
  commandsError: controlCommandsError,
  commandsPending: controlCommandsPending,
  commandsTotal: controlCommandsTotal,
  historyFilters: controlHistoryFilters,
} = useBusinessDeviceControlCommands(deviceAssetIdRef)
const { page: controlPage, pageSize: controlPageSize } = usePagedList(controlHistoryFilters)
const controlCommandsErrorMessage = computed(() => formatError(controlCommandsError.value))

type ControlCommandRow = (typeof controlCommands)['value'][number]
const controlColumns: NvDataTableColumn<ControlCommandRow>[] = [
  {
    key: 'requestedAtUtc',
    header: '时间',
    width: 'w-44',
    accessor: (r) => formatDateTime(r.requestedAtUtc),
  },
  { key: 'requestedBy', header: '操作人', accessor: (r) => r.requestedBy ?? '未知' },
  { key: 'commandType', header: '命令' },
  { key: 'value', header: '值', accessor: (r) => r.value ?? (r.tagKey ? '—' : '参数集') },
  { key: 'approvalStatus', header: '审批状态', width: 'w-28' },
  { key: 'status', header: '回执', width: 'w-24' },
]
function controlCommandRowKey(row: ControlCommandRow) {
  return row.commandId ?? row.operationTaskId ?? row.correlationId ?? ''
}

const currentState = computed(() => device.value?.currentState)
const errorMessage = computed(() => formatError(deviceError.value))
const telemetryErrorMessage = computed(() =>
  formatError(historyError.value || oeeError.value || runtimeAvailabilityError.value),
)
const oeeDegradedReasons = computed(() =>
  (oee.value?.degradedReasons ?? []).map(describeTelemetryOeeDegradation),
)
const maintenanceErrorMessage = computed(() =>
  formatError(
    maintenanceAvailabilityError.value ||
      reliabilityError.value ||
      workOrdersError.value ||
      plansError.value ||
      inspectionsError.value ||
      sparePartsError.value,
  ),
)
const blockCount = computed(
  () => availabilityWindows.value.filter((w) => w.availabilityStatus !== 'available').length,
)
const historyPreview = computed(() => visibleHistoryItems.value.slice(0, 5))
const historyCount = computed(() => visibleHistoryItems.value.length)
const currentDeviceId = computed(() => filters.deviceAssetId.trim())
const currentDeviceWorkOrderMatches = computed(() =>
  workOrders.value.filter((row) => row.deviceAssetId === currentDeviceId.value),
)
const currentDeviceWorkOrders = computed(() => currentDeviceWorkOrderMatches.value.slice(0, 5))
const currentDevicePlanMatches = computed(() =>
  plans.value.filter((row) => row.deviceAssetId === currentDeviceId.value),
)
const currentDevicePlans = computed(() => currentDevicePlanMatches.value.slice(0, 5))
const currentDeviceSpareParts = computed(() =>
  spareParts.value.filter((row) => row.deviceAssetId === currentDeviceId.value).slice(0, 5),
)
const currentDeviceWorkOrderIds = computed(
  () => new Set(currentDeviceWorkOrderMatches.value.map((row) => row.workOrderId).filter(Boolean)),
)
const currentDevicePlanIds = computed(
  () => new Set(currentDevicePlanMatches.value.map((row) => row.planId).filter(Boolean)),
)
const currentDeviceInspections = computed(() =>
  inspections.value
    .filter(
      (row) =>
        (row.workOrderId ? currentDeviceWorkOrderIds.value.has(row.workOrderId) : false) ||
        (row.planId ? currentDevicePlanIds.value.has(row.planId) : false),
    )
    .slice(0, 5),
)
const telemetryPending = computed(() => historyPending.value || oeePending.value)
const maintenancePending = computed(
  () =>
    maintenanceAvailabilityPending.value ||
    reliabilityPending.value ||
    workOrdersPending.value ||
    plansPending.value ||
    inspectionsPending.value ||
    sparePartsPending.value,
)

watch(
  routeDeviceAssetId,
  (deviceAssetId) => {
    if (!deviceAssetId || deviceAssetId === filters.deviceAssetId) return
    filters.deviceAssetId = deviceAssetId
    historyFilters.deviceAssetId = deviceAssetId
    oeeFilters.deviceAssetId = deviceAssetId
    maintenanceAvailabilityFilters.deviceAssetIds = deviceAssetId
    reliabilityFilters.deviceAssetId = deviceAssetId
    void refreshDevice()
  },
  { immediate: true },
)

type Window = (typeof availabilityWindows)['value'][number]
const columns: NvDataTableColumn<Window>[] = [
  { key: 'availabilityStatus', header: '状态', width: 'w-24' },
  {
    key: 'reason',
    header: '原因',
    accessor: (r) => describeEquipmentReason(r.reasonCode ?? '').label,
  },
  { key: 'workCenterId', header: '工作中心', accessor: (r) => r.workCenterId ?? '未绑定' },
  { key: 'startUtc', header: '开始', width: 'w-44' },
  { key: 'endUtc', header: '结束', width: 'w-44' },
  { key: 'sourceReferenceId', header: '关联业务', accessor: (r) => r.sourceReferenceId ?? '无' },
  {
    key: 'substituteDeviceAssetIds',
    header: '替代设备',
    accessor: (r) =>
      r.substituteDeviceAssetIds?.length ? r.substituteDeviceAssetIds.join(', ') : '无',
  },
]

function badgeVariant(tone: EquipmentTone) {
  if (tone === 'success') return 'success'
  if (tone === 'danger') return 'danger'
  return 'neutral'
}
function statusLabel(status?: string | null) {
  const labels: Record<string, string> = {
    down: '停机',
    faulted: '故障',
    idle: '空闲',
    offline: '离线',
    ready: '就绪',
    running: '运行中',
    stopped: '停止',
  }
  return status ? (labels[status.toLowerCase()] ?? status) : '未知'
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
function availabilityLabel(value?: string | null) {
  const labels: Record<string, string> = {
    available: '可用',
    unavailable: '不可用',
    unknown: '未知',
  }
  return value ? (labels[value.toLowerCase()] ?? value) : '未知'
}
function availabilityVariant(value?: string | null) {
  if (value === 'available') return 'success'
  if (value === 'unavailable') return 'danger'
  return 'neutral'
}
function metricLabel(value?: number | null, suffix = '') {
  if (value === null || value === undefined) return '无样本'
  return `${Number(value).toFixed(1)}${suffix}`
}
function historyTypeLabel(value?: string | null) {
  const labels: Record<string, string> = {
    alarm: '报警',
    daily: '日汇总',
    hourly: '小时汇总',
    sample: '采样',
    state: '状态',
  }
  return value ? (labels[value.toLowerCase()] ?? value) : '事件'
}
function historyType(row: { itemType?: string | null }) {
  return row.itemType
}
function historyValue(row: { value?: string | null }) {
  return row.value ?? '无数值'
}
function maintenanceStatusLabel(value?: string | null) {
  const labels: Record<string, string> = {
    open: '待处理',
    opened: '待处理',
    scheduled: '已排程',
    inprogress: '处理中',
    'in-progress': '处理中',
    completed: '已完成',
    closed: '已关闭',
  }
  return value ? (labels[value.toLowerCase()] ?? value) : '未知'
}
function workOrderLabel(row: { workOrderId?: string }) {
  return row.workOrderId ?? '维护工单'
}
function intervalLabel(value?: string | null) {
  const labels: Record<string, string> = {
    P7D: '每周',
    P14D: '每两周',
    P30D: '每月',
    P90D: '每季度',
  }
  return value ? (labels[value] ?? value) : '未设置'
}
function quantityLabel(row: { quantity?: number | null; uomCode?: string | null }) {
  if (row.quantity === null || row.quantity === undefined) return '未记录'
  return `${row.quantity} ${row.uomCode ?? ''}`.trim()
}
function recordDowntime() {
  void router.push({ path: '/mes/downtime', query: { deviceAssetId: filters.deviceAssetId } })
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
      :title="filters.deviceAssetId ? `设备详情：${filters.deviceAssetId}` : '设备详情'"
      :breadcrumbs="[{ label: '设备监控（IoT）' }]"
    >
      <template #actions>
        <NvButton
          v-if="canControlDevice && filters.deviceAssetId"
          size="sm"
          type="button"
          @click="controlSheetOpen = true"
        >
          <SlidersHorizontalIcon aria-hidden="true" />
          设备控制
        </NvButton>
        <NvButton size="sm" type="button" variant="outline" as-child>
          <RouterLink to="/equipment"><ArrowLeftIcon aria-hidden="true" />返回看板</RouterLink>
        </NvButton>
        <NvButton size="sm" type="button" variant="outline" as-child>
          <RouterLink
            :to="{
              path: '/equipment/telemetry/history',
              query: { deviceAssetId: filters.deviceAssetId },
            }"
          >
            <LineChartIcon aria-hidden="true" />
            历史趋势
          </RouterLink>
        </NvButton>
        <NvButton size="sm" type="button" variant="outline" as-child>
          <RouterLink
            :to="{
              path: '/equipment/telemetry/oee',
              query: { deviceAssetId: filters.deviceAssetId },
            }"
          >
            <GaugeIcon aria-hidden="true" />
            OEE 与可用性
          </RouterLink>
        </NvButton>
        <NvButton size="sm" type="button" variant="outline" @click="recordDowntime">
          <WrenchIcon aria-hidden="true" />
          记录停机
        </NvButton>
        <NvButton size="sm" type="button" variant="outline" as-child>
          <RouterLink
            :to="{
              path: '/maintenance/work-orders',
              query: { deviceAssetId: filters.deviceAssetId },
            }"
          >
            <WrenchIcon aria-hidden="true" />
            创建维修工单
          </RouterLink>
        </NvButton>
        <NvButton size="sm" type="button" variant="outline" as-child>
          <RouterLink
            :to="{
              path: '/maintenance/reliability',
              query: { deviceAssetId: filters.deviceAssetId },
            }"
          >
            <TrendingUpIcon aria-hidden="true" />
            可靠性
          </RouterLink>
        </NvButton>
        <NvButton size="sm" type="button" variant="outline" as-child>
          <RouterLink
            :to="{
              path: '/maintenance/availability',
              query: { deviceAssetId: filters.deviceAssetId },
            }"
          >
            <CalendarRangeIcon aria-hidden="true" />
            可用窗口
          </RouterLink>
        </NvButton>
        <NvButton size="sm" type="button" variant="outline" as-child>
          <RouterLink
            :to="{
              path: '/maintenance/inspections',
              query: { deviceAssetId: filters.deviceAssetId },
            }"
          >
            <ClipboardCheckIcon aria-hidden="true" />
            点检
          </RouterLink>
        </NvButton>
        <NvButton size="sm" type="button" variant="outline" as-child>
          <RouterLink
            :to="{
              path: '/maintenance/spare-parts',
              query: { deviceAssetId: filters.deviceAssetId },
            }"
          >
            <PackageSearchIcon aria-hidden="true" />
            备件
          </RouterLink>
        </NvButton>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="devicePending"
          @click="refreshDevice"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <div
      v-if="!filters.deviceAssetId"
      class="rounded-lg border border-dashed p-6 text-sm text-muted-foreground"
    >
      未指定设备。请从设备运行看板选择具体设备查看详情。
    </div>

    <template v-else>
      <NvSectionCards :columns="4">
        <NvSectionCard
          description="当前状态"
          :value="statusLabel(currentState?.currentState)"
          hint="设备运行事实"
        />
        <NvSectionCard
          description="数据状态"
          :value="currentState?.isSourceFresh ? '正常' : '过期'"
          :hint="formatDateTime(currentState?.stateOccurredAtUtc)"
        />
        <NvSectionCard description="未解除报警" :value="activeAlarms.length" hint="当前设备报警" />
        <NvSectionCard description="占用窗口" :value="blockCount" hint="影响排程或执行" />
      </NvSectionCards>

      <div class="grid gap-4 lg:grid-cols-[360px_minmax(0,1fr)]">
        <div class="grid gap-4">
          <div class="rounded-lg border bg-card p-4">
            <p class="text-xs font-bold uppercase text-primary">当前状态</p>
            <div class="mt-3 flex items-center justify-between gap-3">
              <div class="min-w-0">
                <p class="truncate text-lg font-semibold text-foreground">
                  {{ currentState?.deviceAssetId ?? filters.deviceAssetId }}
                </p>
                <p class="mt-1 text-sm text-muted-foreground">
                  状态时间 {{ formatDateTime(currentState?.stateOccurredAtUtc) }}
                </p>
              </div>
              <NvBadge
                class="rounded-sm"
                :variant="badgeVariant(equipmentStatusTone(currentState?.currentState))"
                >{{ statusLabel(currentState?.currentState) }}</NvBadge
              >
            </div>
            <div class="mt-3">
              <NvBadge
                class="rounded-sm"
                :variant="currentState?.isSourceFresh ? 'success' : 'warning'"
                >{{ currentState?.isSourceFresh ? '采集正常' : '采集过期' }}</NvBadge
              >
            </div>
          </div>

          <div class="rounded-lg border bg-card">
            <div class="border-b px-4 py-3">
              <h2 class="text-sm font-semibold text-foreground">当前报警</h2>
            </div>
            <div class="grid gap-3 p-4">
              <div
                v-for="alarm in activeAlarms"
                :key="alarm.alarmEventId ?? alarm.alarmCode"
                class="grid gap-2 rounded-lg border p-3"
              >
                <div class="flex items-center justify-between gap-2">
                  <p class="truncate text-sm font-semibold text-foreground">
                    {{ alarm.alarmCode ?? '无代码' }}
                  </p>
                  <NvBadge class="rounded-sm" :variant="severityVariant(alarm.severity)">{{
                    severityLabel(alarm.severity)
                  }}</NvBadge>
                </div>
                <p class="text-xs text-muted-foreground">{{ formatDateTime(alarm.raisedAtUtc) }}</p>
                <NvButton
                  size="sm"
                  type="button"
                  variant="outline"
                  class="justify-self-start"
                  as-child
                >
                  <RouterLink to="/equipment/telemetry/alarm-rules"
                    ><Settings2Icon aria-hidden="true" />维护规则</RouterLink
                  >
                </NvButton>
              </div>
              <div
                v-if="!activeAlarms.length"
                class="rounded-lg border border-dashed p-4 text-sm text-muted-foreground"
              >
                当前设备没有未解除报警。
              </div>
            </div>
          </div>
        </div>

        <div class="grid gap-2">
          <span class="text-sm font-semibold text-foreground">可用性窗口（排程与维修占用）</span>
          <NvDataTable
            :columns="columns"
            :rows="availabilityWindows"
            :row-key="(r) => `${r.deviceAssetId}-${r.reasonCode}-${r.startUtc}`"
            :loading="devicePending"
            :searchable="false"
            :column-settings="false"
            empty-message="当前设备没有可用性窗口。"
          >
            <template #cell-availabilityStatus="{ row }">
              <NvBadge class="rounded-sm" :variant="availabilityVariant(row.availabilityStatus)">{{
                availabilityLabel(row.availabilityStatus)
              }}</NvBadge>
            </template>
            <template #cell-reason="{ row }">
              <div class="grid gap-1">
                <span class="font-medium text-foreground">{{
                  describeEquipmentReason(row.reasonCode ?? '').label
                }}</span>
                <span class="text-xs text-muted-foreground">{{
                  describeEquipmentReason(row.reasonCode ?? '').nextStep
                }}</span>
              </div>
            </template>
            <template #cell-startUtc="{ row }">{{ formatDateTime(row.startUtc) }}</template>
            <template #cell-endUtc="{ row }">{{ formatDateTime(row.endUtc) }}</template>
          </NvDataTable>
        </div>
      </div>

      <section class="grid gap-4">
        <div class="flex flex-wrap items-start justify-between gap-3">
          <div>
            <h2 class="text-base font-semibold text-foreground">遥测深层上下文</h2>
            <p class="mt-1 text-sm text-muted-foreground">
              {{ describeTelemetryOeeLimitations() }}
            </p>
          </div>
          <div class="flex flex-wrap gap-2">
            <NvButton size="sm" type="button" variant="outline" as-child>
              <RouterLink
                :to="{
                  path: '/equipment/telemetry/history',
                  query: { deviceAssetId: filters.deviceAssetId },
                }"
              >
                <LineChartIcon aria-hidden="true" />
                历史趋势正式页面
              </RouterLink>
            </NvButton>
            <NvButton size="sm" type="button" variant="outline" as-child>
              <RouterLink
                :to="{
                  path: '/equipment/telemetry/oee',
                  query: { deviceAssetId: filters.deviceAssetId },
                }"
              >
                <GaugeIcon aria-hidden="true" />
                OEE 正式页面
              </RouterLink>
            </NvButton>
            <NvButton size="sm" type="button" variant="outline" as-child>
              <RouterLink
                :to="{ path: '/equipment/alarms', query: { deviceAssetId: filters.deviceAssetId } }"
              >
                <Settings2Icon aria-hidden="true" />
                报警列表正式页面
              </RouterLink>
            </NvButton>
          </div>
        </div>

        <p v-if="telemetryErrorMessage" class="text-sm text-destructive" role="alert">
          {{ telemetryErrorMessage }}
        </p>

        <NvSectionCards :columns="4">
          <NvSectionCard
            description="可用率"
            :value="formatOeeRate(oee?.availabilityRate)"
            hint="IndustrialTelemetry OEE facade"
          />
          <NvSectionCard
            description="加载率"
            :value="formatOeeRate(oee?.loadingRate)"
            hint="排除计划停机窗口"
          />
          <NvSectionCard
            description="性能率"
            :value="formatOeeRate(oee?.performanceRate)"
            hint="实际产出 ÷ 理论产出"
          />
          <NvSectionCard
            description="质量率"
            :value="formatOeeRate(oee?.qualityRate)"
            hint="良品 ÷ 总产出"
          />
          <NvSectionCard
            description="OEE"
            :value="formatOeeRate(oee?.oeeRate)"
            hint="三项因子的乘积"
          />
          <NvSectionCard
            description="历史事件"
            :value="historyCount"
            hint="设备历史趋势 facade 返回数量"
          />
        </NvSectionCards>

        <div class="grid gap-4 lg:grid-cols-2">
          <div class="rounded-lg border bg-card">
            <div class="border-b px-4 py-3">
              <h3 class="text-sm font-semibold text-foreground">历史趋势摘录</h3>
              <p class="mt-1 text-xs text-muted-foreground">
                来源：设备历史趋势 facade；详情页只展示最近事件，完整曲线进入正式页面。
              </p>
            </div>
            <div class="grid gap-3 p-4">
              <div v-if="telemetryPending" class="text-sm text-muted-foreground">
                正在读取遥测历史。
              </div>
              <div
                v-for="item in historyPreview"
                :key="`${item.occurredAtUtc}-${item.tagKey}-${historyValue(item)}`"
                class="grid gap-1 rounded-lg border p-3"
              >
                <div class="flex items-center justify-between gap-2">
                  <span class="text-sm font-medium text-foreground">{{
                    item.tagKey ?? '未命名采集点'
                  }}</span>
                  <NvBadge class="rounded-sm" variant="neutral">{{
                    historyTypeLabel(historyType(item))
                  }}</NvBadge>
                </div>
                <p class="text-sm text-muted-foreground">{{ historyValue(item) }}</p>
                <p class="text-xs text-muted-foreground">
                  {{ formatDateTime(item.occurredAtUtc) }}
                </p>
              </div>
              <div
                v-if="!telemetryPending && !historyPreview.length"
                class="rounded-lg border border-dashed p-4 text-sm text-muted-foreground"
              >
                当前窗口没有历史趋势事件；这表示当前 facade 未返回样本，不等于设备未接入。
              </div>
            </div>
          </div>

          <div class="rounded-lg border bg-card">
            <div class="border-b px-4 py-3">
              <h3 class="text-sm font-semibold text-foreground">OEE / runtime availability 口径</h3>
              <p class="mt-1 text-xs text-muted-foreground">
                来源：IndustrialTelemetry OEE 与 runtime availability facade；详情页不重新计算 OEE。
              </p>
            </div>
            <div class="grid gap-3 p-4 text-sm">
              <div class="grid grid-cols-[120px_minmax(0,1fr)] gap-2">
                <span class="text-muted-foreground">统计窗口</span>
                <span
                  >{{ formatDateTime(oee?.windowStartUtc ?? oeeFilters.windowStartUtc) }} -
                  {{ formatDateTime(oee?.windowEndUtc ?? oeeFilters.windowEndUtc) }}</span
                >
              </div>
              <div class="grid grid-cols-[120px_minmax(0,1fr)] gap-2">
                <span class="text-muted-foreground">状态样本</span>
                <span>{{ oee?.stateSampleCount ?? 0 }} 条</span>
              </div>
              <div class="grid grid-cols-[120px_minmax(0,1fr)] gap-2">
                <span class="text-muted-foreground">性能因子</span>
                <span>{{ formatOeeRate(oee?.performanceRate) }}</span>
              </div>
              <div class="grid grid-cols-[120px_minmax(0,1fr)] gap-2">
                <span class="text-muted-foreground">质量因子</span>
                <span>{{ formatOeeRate(oee?.qualityRate) }}</span>
              </div>
              <div class="grid grid-cols-[120px_minmax(0,1fr)] gap-2">
                <span class="text-muted-foreground">MES 报工</span>
                <span>{{ oee?.productionFactCount ?? 0 }} 条</span>
              </div>
              <div class="grid grid-cols-[120px_minmax(0,1fr)] gap-2">
                <span class="text-muted-foreground">理论产出</span>
                <span>{{
                  formatOeeQuantity(oee?.expectedOutputQuantity, oee?.outputUomCode)
                }}</span>
              </div>
              <div
                v-if="oee?.isDegraded"
                class="rounded-md bg-muted p-3 text-xs text-muted-foreground"
              >
                <p class="font-medium text-foreground">当前 OEE 数据不完整</p>
                <ul class="mt-1 list-disc pl-4">
                  <li v-for="reason in oeeDegradedReasons" :key="reason">{{ reason }}</li>
                </ul>
              </div>
            </div>
          </div>
        </div>
      </section>

      <section class="grid gap-4">
        <div class="flex flex-wrap items-start justify-between gap-3">
          <div>
            <h2 class="text-base font-semibold text-foreground">维护与可靠性上下文</h2>
            <p class="mt-1 text-sm text-muted-foreground">
              来源：Maintenance 工单、保养计划、点检、备件、可用窗口和可靠性
              facade。列表读面按返回设备字段收敛当前设备；缺少设备字段时不伪造完成能力。
            </p>
          </div>
          <div class="flex flex-wrap gap-2">
            <NvButton size="sm" type="button" variant="outline" as-child>
              <RouterLink
                :to="{
                  path: '/maintenance/work-orders',
                  query: { deviceAssetId: filters.deviceAssetId },
                }"
              >
                <WrenchIcon aria-hidden="true" />
                维护工单正式页面
              </RouterLink>
            </NvButton>
            <NvButton size="sm" type="button" variant="outline" as-child>
              <RouterLink
                :to="{
                  path: '/maintenance/plans',
                  query: { deviceAssetId: filters.deviceAssetId },
                }"
              >
                <CalendarRangeIcon aria-hidden="true" />
                保养计划正式页面
              </RouterLink>
            </NvButton>
            <NvButton size="sm" type="button" variant="outline" as-child>
              <RouterLink
                :to="{
                  path: '/maintenance/spare-parts',
                  query: { deviceAssetId: filters.deviceAssetId },
                }"
              >
                <PackageSearchIcon aria-hidden="true" />
                备件正式页面
              </RouterLink>
            </NvButton>
          </div>
        </div>

        <p v-if="maintenanceErrorMessage" class="text-sm text-destructive" role="alert">
          {{ maintenanceErrorMessage }}
        </p>

        <NvSectionCards :columns="4">
          <NvSectionCard
            description="MTBF"
            :value="metricLabel(reliability?.mtbfHours, ' 小时')"
            :hint="
              reliability?.mtbfRuntimeHasSamples
                ? 'Maintenance reliability facade'
                : '当前窗口无运行样本'
            "
          />
          <NvSectionCard
            description="MTTR"
            :value="metricLabel(reliability?.mttrMinutes, ' 分钟')"
            hint="Maintenance 完成维修样本均值"
          />
          <NvSectionCard
            description="维护故障"
            :value="reliability?.failureCount ?? 0"
            hint="窗口内故障计数"
          />
          <NvSectionCard
            description="完成维修"
            :value="reliability?.repairCount ?? 0"
            hint="窗口内修复计数"
          />
        </NvSectionCards>

        <div class="grid gap-4 xl:grid-cols-2">
          <div class="rounded-lg border bg-card">
            <div class="border-b px-4 py-3">
              <h3 class="text-sm font-semibold text-foreground">维修工单</h3>
            </div>
            <div class="grid gap-3 p-4">
              <div v-if="maintenancePending" class="text-sm text-muted-foreground">
                正在读取维护上下文。
              </div>
              <div
                v-for="row in currentDeviceWorkOrders"
                :key="row.workOrderId"
                class="grid gap-1 rounded-lg border p-3"
              >
                <div class="flex items-center justify-between gap-2">
                  <span class="text-sm font-medium text-foreground">{{ workOrderLabel(row) }}</span>
                  <NvBadge class="rounded-sm" variant="neutral">{{
                    maintenanceStatusLabel(row.status)
                  }}</NvBadge>
                </div>
                <p class="text-xs text-muted-foreground">
                  开单时间 {{ formatDateTime(row.openedAtUtc) }}
                </p>
                <p
                  v-if="row.sourceAlarmId || row.relatedAlarmId"
                  class="text-xs text-muted-foreground"
                >
                  关联报警 {{ row.sourceAlarmId ?? row.relatedAlarmId }}
                </p>
              </div>
              <div
                v-if="!maintenancePending && !currentDeviceWorkOrders.length"
                class="rounded-lg border border-dashed p-4 text-sm text-muted-foreground"
              >
                当前返回窗口未包含该设备的 Maintenance
                工单记录；如需确认全量或开单，请进入维护工单正式页面。
              </div>
            </div>
          </div>

          <div class="rounded-lg border bg-card">
            <div class="border-b px-4 py-3">
              <h3 class="text-sm font-semibold text-foreground">保养计划与点检</h3>
            </div>
            <div class="grid gap-3 p-4">
              <div
                v-for="row in currentDevicePlans"
                :key="row.planId ?? row.planCode"
                class="grid gap-1 rounded-lg border p-3"
              >
                <span class="text-sm font-medium text-foreground">{{
                  row.planCode ?? row.planId ?? '保养计划'
                }}</span>
                <span class="text-xs text-muted-foreground"
                  >周期 {{ intervalLabel(row.interval) }} · 起始
                  {{ row.startsOn ?? '未设置' }}</span
                >
              </div>
              <div
                v-for="row in currentDeviceInspections"
                :key="row.inspectionId"
                class="grid gap-1 rounded-lg border p-3"
              >
                <span class="text-sm font-medium text-foreground">{{
                  row.inspectionId ?? '点检记录'
                }}</span>
                <span class="text-xs text-muted-foreground"
                  >结果 {{ row.result ?? '未记录' }} ·
                  {{ formatDateTime(row.inspectedAtUtc) }}</span
                >
              </div>
              <div
                v-if="
                  !maintenancePending &&
                  !currentDevicePlans.length &&
                  !currentDeviceInspections.length
                "
                class="rounded-lg border border-dashed p-4 text-sm text-muted-foreground"
              >
                当前返回窗口未包含可关联的保养计划或点检记录；点检以工单/计划关联，缺少设备字段时不在详情页冒充已关联。
              </div>
            </div>
          </div>

          <div class="rounded-lg border bg-card">
            <div class="border-b px-4 py-3">
              <h3 class="text-sm font-semibold text-foreground">备件需求</h3>
            </div>
            <div class="grid gap-3 p-4">
              <div
                v-for="row in currentDeviceSpareParts"
                :key="row.sparePartLineId ?? `${row.workOrderId}-${row.skuCode}`"
                class="grid gap-1 rounded-lg border p-3"
              >
                <span class="text-sm font-medium text-foreground">{{
                  row.skuCode ?? '备件物料'
                }}</span>
                <span class="text-xs text-muted-foreground"
                  >数量 {{ quantityLabel(row) }} · 工单 {{ row.workOrderId ?? '未关联' }}</span
                >
              </div>
              <div
                v-if="!maintenancePending && !currentDeviceSpareParts.length"
                class="rounded-lg border border-dashed p-4 text-sm text-muted-foreground"
              >
                当前设备没有备件需求；库存可用量以库存管理正式页面为准。
              </div>
            </div>
          </div>

          <div class="rounded-lg border bg-card">
            <div class="border-b px-4 py-3">
              <h3 class="text-sm font-semibold text-foreground">Maintenance 可用窗口</h3>
              <p class="mt-1 text-xs text-muted-foreground">
                来源：Maintenance availability-windows facade，和上方设备运行可用性窗口分开展示。
              </p>
            </div>
            <div class="grid gap-3 p-4">
              <div
                v-for="row in maintenanceAvailabilityWindows"
                :key="`${row.deviceAssetId}-${row.reasonCode}-${row.startUtc}`"
                class="grid gap-1 rounded-lg border p-3"
              >
                <div class="flex items-center justify-between gap-2">
                  <span class="text-sm font-medium text-foreground">{{
                    describeEquipmentReason(row.reasonCode ?? '').label
                  }}</span>
                  <NvBadge
                    class="rounded-sm"
                    :variant="availabilityVariant(row.availabilityStatus)"
                    >{{ availabilityLabel(row.availabilityStatus) }}</NvBadge
                  >
                </div>
                <span class="text-xs text-muted-foreground"
                  >{{ formatDateTime(row.startUtc) }} - {{ formatDateTime(row.endUtc) }}</span
                >
              </div>
              <div
                v-if="!maintenancePending && !maintenanceAvailabilityWindows.length"
                class="rounded-lg border border-dashed p-4 text-sm text-muted-foreground"
              >
                当前设备没有 Maintenance 可用窗口。
              </div>
            </div>
          </div>
        </div>
      </section>

      <section class="grid gap-4">
        <div class="flex flex-wrap items-start justify-between gap-3">
          <div>
            <h2 class="text-base font-semibold text-foreground">控制命令历史</h2>
            <p class="mt-1 text-sm text-muted-foreground">
              来源：设备控制命令台账（含 Ops 审批状态与执行回执）；倒序分页。命令下发需 Ops
              审批门禁·全程审计。
            </p>
          </div>
          <NvButton
            v-if="canControlDevice"
            size="sm"
            type="button"
            variant="outline"
            @click="controlSheetOpen = true"
          >
            <SlidersHorizontalIcon aria-hidden="true" />
            设备控制
          </NvButton>
        </div>

        <p v-if="controlCommandsErrorMessage" class="text-sm text-destructive" role="alert">
          {{ controlCommandsErrorMessage }}
        </p>

        <NvDataTable
          manual
          :page="controlPage"
          :page-size="controlPageSize"
          :total-items="controlCommandsTotal"
          @update:page="controlPage = $event"
          @update:page-size="(v) => (controlPageSize = String(v))"
          :columns="controlColumns"
          :rows="controlCommands"
          :row-key="controlCommandRowKey"
          :loading="controlCommandsPending"
          :searchable="false"
          :column-settings="false"
          empty-message="该设备还没有控制命令记录。点击「设备控制」下发第一条命令。"
        >
          <template #cell-commandType="{ row }">
            <div class="grid gap-0.5">
              <span class="font-medium text-foreground">{{
                deviceControlCommandTypeLabel(row.commandType)
              }}</span>
              <span v-if="row.tagKey" class="text-xs text-muted-foreground">{{ row.tagKey }}</span>
            </div>
          </template>
          <template #cell-approvalStatus="{ row }">
            <NvBadge class="rounded-sm" variant="neutral">{{
              deviceControlApprovalLabel(row.approvalStatus)
            }}</NvBadge>
          </template>
          <template #cell-status="{ row }">
            <NvBadge class="rounded-sm" :variant="deviceControlStatusTone(row.status)">{{
              deviceControlStatusLabel(row.status)
            }}</NvBadge>
          </template>
        </NvDataTable>
      </section>

      <DeviceControlSheet
        v-if="filters.deviceAssetId"
        v-model:open="controlSheetOpen"
        :device-asset-id="filters.deviceAssetId"
      />
    </template>
  </BusinessLayout>
</template>
