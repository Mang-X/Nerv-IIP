<script setup lang="ts">
import type { EquipmentRuntimeAvailabilityWindow } from '@nerv-iip/api-client'
import type { NvDataTableColumn, NvMetricSegment } from '@nerv-iip/ui'
import { useMaintenanceAvailabilityWindows } from '@/composables/useBusinessMaintenance'
import {
  describeAvailabilityWindowReference,
  describeEquipmentReason,
} from '@/composables/useBusinessEquipment'
import { useEquipmentWorkCenterCatalog } from '@/composables/useEquipmentPickerCatalog'
import { useEquipmentScopeSelection } from '@/composables/useEquipmentScopeSelection'
import EntityMultiPicker from '@/components/business/EntityMultiPicker.vue'
import { useMasterDataDisplayNames } from '@/composables/useMasterDataDisplayNames'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvBadge,
  NvButton,
  NvCascadePicker,
  NvDataTable,
  NvField,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvMetricCard,
  NvPageHeader,
} from '@nerv-iip/ui'
import { RefreshCwIcon, WrenchIcon } from '@lucide/vue'
import { computed, watch } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import { inlineErrorMessage } from '@/utils/notify'
import { readFaceText } from '@/utils/readFace'

definePage({
  meta: {
    requiresAuth: true,
    title: '可用窗口',
    requiredPermissions: ['business.maintenance.work-orders.read'],
  },
})

const route = useRoute()
// 深链只认单台设备（逗号列表取第一台初始化下钻）。
const initialDeviceAssetId =
  (typeof route.query.deviceAssetId === 'string' ? route.query.deviceAssetId : '')
    .split(',')[0]
    ?.trim() ?? ''
const {
  availabilityError,
  availabilityPending,
  availabilityWindows,
  filters,
  refreshAvailability,
} = useMaintenanceAvailabilityWindows()

// 车间 → 产线 → 设备 级联范围：availability-windows 接口天然吃设备编号集合，
// 未下钻到单台时直接把范围内设备编码全集喂给它做真实范围聚合（全厂 = 台账全部设备）。
const { scope, levels, devicesInScope, scopeLabel, scopePending } = useEquipmentScopeSelection({
  device: initialDeviceAssetId,
})
const { workCenterOptions, workCentersPending } = useEquipmentWorkCenterCatalog()
const MAX_SCOPE_DEVICES = 50
const scopedDeviceCodes = computed(() =>
  devicesInScope.value.map((d) => (d.code ?? '').trim()).filter((code) => code.length > 0),
)
const scopeTruncated = computed(() => scopedDeviceCodes.value.length > MAX_SCOPE_DEVICES)
watch(
  scopedDeviceCodes,
  (codes) => {
    filters.deviceAssetIds = codes.slice(0, MAX_SCOPE_DEVICES).join(',')
  },
  { immediate: true },
)

const hasDeviceScope = computed(() => scopedDeviceCodes.value.length > 0)
const unavailableCount = computed(
  () =>
    availabilityWindows.value.filter(
      (window) => (window.availabilityStatus ?? '').toLowerCase() === 'unavailable',
    ).length,
)
const inspectionCount = computed(
  () =>
    availabilityWindows.value.filter(
      (window) => (window.reasonCode ?? '').trim().toLowerCase() === 'equipment.inspectionrequired',
    ).length,
)
// 不可用 + 可用 = 查询范围内的全部窗口，是真正的构成关系。
const windowSegments = computed<NvMetricSegment[]>(() => [
  { key: 'unavailable', label: '不可用', value: unavailableCount.value, tone: 'danger' },
  {
    key: 'available',
    label: '可用',
    value: availabilityWindows.value.length - unavailableCount.value,
    tone: 'success',
  },
])
const errorMessage = computed(() => inlineErrorMessage(availabilityError.value))

const windowStartLocal = computed({
  get: () => toLocalDateTime(filters.windowStartUtc),
  set: (value: string) => {
    filters.windowStartUtc = toIsoDateTime(value)
  },
})
const windowEndLocal = computed({
  get: () => toLocalDateTime(filters.windowEndUtc),
  set: (value: string) => {
    filters.windowEndUtc = toIsoDateTime(value)
  },
})

// 可用窗口读面只回设备引用（编码或公开 ID）与工作中心编号，名称在主数据里，按引用 join 出中文名。
const { resolveDevice, resolveDeviceCode, resolveWorkCenter } = useMasterDataDisplayNames({
  devices: true,
  workCenters: true,
})
/** 设备名称；名录查不到时只留可读编码，公开 ID 不上屏。 */
function deviceName(reference?: string | null) {
  if (!reference) return '—'
  return resolveDevice(reference) ?? readFaceText(reference)
}
/** 设备编码；名录查不到时只留可读编码。 */
function deviceCode(reference?: string | null) {
  if (!reference) return ''
  return resolveDeviceCode(reference) ?? readFaceText(reference, '')
}
/** 设备「名称（编码）」。 */
function deviceLabel(reference?: string | null) {
  const name = deviceName(reference)
  const code = deviceCode(reference)
  return code && code !== name ? `${name}（${code}）` : name
}
function workCenterLabel(code?: string | null, fallback = '未绑定') {
  if (!code) return fallback
  return resolveWorkCenter(code) ?? readFaceText(code, fallback)
}
/** 关联业务：维修工单号 / 保养计划编码 / 点检来源等人读标识；解析不出来显示「—」，不显示工单主键或告警合成键。 */
function referenceLabel(row: EquipmentRuntimeAvailabilityWindow) {
  return describeAvailabilityWindowReference(row, deviceCode(row.deviceAssetId))
}

const columns: NvDataTableColumn<EquipmentRuntimeAvailabilityWindow>[] = [
  {
    key: 'deviceAssetId',
    header: '设备',
    cellClass: 'font-medium',
    accessor: (r) => deviceLabel(r.deviceAssetId),
  },
  { key: 'availabilityStatus', header: '状态', width: 'w-24' },
  { key: 'reasonCode', header: '原因' },
  {
    key: 'workCenterId',
    header: '工作中心',
    accessor: (r) => workCenterLabel(r.workCenterId),
  },
  { key: 'startUtc', header: '开始', accessor: (r) => formatDateTime(r.startUtc) },
  { key: 'endUtc', header: '结束', accessor: (r) => formatDateTime(r.endUtc) },
  { key: 'sourceReferenceId', header: '关联业务', accessor: referenceLabel },
]

function availabilityLabel(value?: string | null) {
  const labels: Record<string, string> = {
    available: '可用',
    unavailable: '不可用',
    unknown: '未知',
  }
  // 词表漏了就说「未知」，绝不把后端英文码回吐到界面上。
  return value ? (labels[value.toLowerCase()] ?? '未知') : '未知'
}
function availabilityVariant(value?: string | null) {
  if ((value ?? '').toLowerCase() === 'available') return 'success'
  if ((value ?? '').toLowerCase() === 'unavailable') return 'danger'
  return 'neutral'
}
function rowKey(row: EquipmentRuntimeAvailabilityWindow) {
  return `${row.deviceAssetId ?? ''}-${row.reasonCode ?? ''}-${row.startUtc ?? ''}-${row.endUtc ?? ''}`
}
function toLocalDateTime(value: string) {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return ''
  date.setMinutes(date.getMinutes() - date.getTimezoneOffset())
  return date.toISOString().slice(0, 16)
}
function toIsoDateTime(value: string) {
  const date = value ? new Date(value) : new Date()
  return Number.isNaN(date.getTime()) ? new Date().toISOString() : date.toISOString()
}
function formatDateTime(value?: string | null) {
  if (!value) return '无'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="可用窗口"
      :breadcrumbs="[{ label: '设备监控' }]"
      :count="hasDeviceScope ? `${scopeLabel} · ${availabilityWindows.length} 个窗口` : scopeLabel"
    >
      <template #actions>
        <NvButton size="sm" type="button" variant="outline" as-child>
          <RouterLink to="/maintenance/work-orders"
            ><WrenchIcon aria-hidden="true" />维护工单</RouterLink
          >
        </NvButton>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="!hasDeviceScope || availabilityPending"
          @click="refreshAvailability"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <div class="grid gap-3 rounded-lg border bg-card p-4">
      <NvCascadePicker v-model="scope" :levels="levels" :aria-busy="scopePending" />
      <p v-if="scopeTruncated" class="text-xs text-muted-foreground">
        当前范围内设备超过 {{ MAX_SCOPE_DEVICES }} 台，本页仅统计前
        {{ MAX_SCOPE_DEVICES }} 台；可用上方级联缩小范围。
      </p>
      <NvFieldGroup class="grid gap-3 lg:grid-cols-[220px_220px_minmax(180px,0.8fr)]">
        <NvField>
          <NvFieldLabel for="avail-start">窗口开始</NvFieldLabel>
          <NvInput id="avail-start" v-model="windowStartLocal" type="datetime-local" />
        </NvField>
        <NvField>
          <NvFieldLabel for="avail-end">窗口结束</NvFieldLabel>
          <NvInput id="avail-end" v-model="windowEndLocal" type="datetime-local" />
        </NvField>
        <NvField>
          <NvFieldLabel for="avail-work-centers">工作中心</NvFieldLabel>
          <EntityMultiPicker
            id="avail-work-centers"
            v-model="filters.workCenterIds"
            :options="workCenterOptions"
            title="选择工作中心"
            placeholder="可选，添加工作中心"
            empty-text="暂无工作中心，请先在基础数据维护工作中心"
            selection-empty-text="未限定工作中心（统计范围内全部设备）"
            :loading="workCentersPending"
            aria-label="工作中心"
          />
        </NvField>
      </NvFieldGroup>
    </div>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <div
      v-if="!hasDeviceScope"
      class="rounded-lg border border-dashed p-6 text-sm text-muted-foreground"
    >
      范围内暂无设备主数据。请调整上方范围，或先在基础数据登记设备资产，再查看维护占用、点检阻塞和其他可用性窗口。
    </div>

    <template v-else>
      <div class="grid gap-4 lg:grid-cols-2">
        <NvMetricCard
          variant="breakdown"
          label="可用性窗口"
          :value="availabilityWindows.length"
          unit="段"
          :segments="windowSegments"
        />
        <NvMetricCard
          variant="alert"
          label="点检阻塞"
          :value="inspectionCount"
          unit="段"
          :tone="inspectionCount > 0 ? 'warning' : 'neutral'"
          :status="
            inspectionCount > 0
              ? { label: '待点检放行', tone: 'warning' }
              : { label: '无阻塞', tone: 'success' }
          "
          :foot-start="
            inspectionCount > 0
              ? '点检未通过前设备不能投入排程，先完成点检再释放窗口。'
              : '当前没有因点检被卡住的可用窗口。'
          "
        />
      </div>

      <NvDataTable
        :columns="columns"
        :rows="availabilityWindows"
        :row-key="rowKey"
        :loading="availabilityPending"
        :searchable="false"
        :column-settings="false"
        empty-message="当前范围没有维护可用性窗口。"
      >
        <template #cell-deviceAssetId="{ row }">
          <RouterLink
            :to="`/equipment/${row.deviceAssetId}`"
            class="text-brand underline-offset-4 hover:underline"
          >
            {{ deviceLabel(row.deviceAssetId) }}
          </RouterLink>
        </template>
        <template #cell-availabilityStatus="{ row }">
          <NvBadge class="rounded-sm" :variant="availabilityVariant(row.availabilityStatus)">{{
            availabilityLabel(row.availabilityStatus)
          }}</NvBadge>
        </template>
        <template #cell-reasonCode="{ row }">
          <div class="grid gap-1">
            <span class="font-medium text-foreground">{{
              describeEquipmentReason(row.reasonCode ?? '').label
            }}</span>
            <span class="text-xs text-muted-foreground">{{
              describeEquipmentReason(row.reasonCode ?? '').nextStep
            }}</span>
          </div>
        </template>
      </NvDataTable>
    </template>
  </BusinessLayout>
</template>
