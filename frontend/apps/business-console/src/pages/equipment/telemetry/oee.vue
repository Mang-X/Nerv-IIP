<script setup lang="ts">
import type { EquipmentRuntimeAvailabilityWindow } from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { describeEquipmentReason } from '@/composables/useBusinessEquipment'
import {
  describeTelemetryOeeDegradation,
  describeTelemetryOeeLimitations,
  formatOeeQuantity,
  formatOeeRate,
  useBusinessTelemetryOee,
} from '@/composables/useBusinessTelemetry'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvBadge,
  NvButton,
  NvDataTable,
  NvInput,
  NvPageHeader,
  NvSectionCard,
  NvSectionCards,
  NvToolbar,
} from '@nerv-iip/ui'
import { LineChartIcon, RefreshCwIcon, Settings2Icon } from '@lucide/vue'
import { computed } from 'vue'
import { RouterLink, useRoute } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: 'OEE 与可用性',
    requiredPermissions: ['business.iiot.telemetry.read'],
  },
})

const route = useRoute()
const {
  availabilityWindows,
  filters,
  oee,
  oeeError,
  oeePending,
  refreshOee,
  runtimeAvailabilityError,
} = useBusinessTelemetryOee({
  deviceAssetId: routeQuery('deviceAssetId'),
  windowEndUtc: routeQuery('windowEndUtc') || undefined,
  windowStartUtc: routeQuery('windowStartUtc') || undefined,
})

const errorMessage = computed(() => formatError(oeeError.value || runtimeAvailabilityError.value))
const limitation = describeTelemetryOeeLimitations()
const oeeDegradedReasons = computed(() =>
  (oee.value?.degradedReasons ?? []).map(describeTelemetryOeeDegradation),
)
const blockedWindowCount = computed(
  () =>
    availabilityWindows.value.filter((w) => w.availabilityStatus?.toLowerCase() === 'unavailable')
      .length,
)

const columns: NvDataTableColumn<EquipmentRuntimeAvailabilityWindow>[] = [
  { key: 'availabilityStatus', header: '状态', width: 'w-24' },
  {
    key: 'reason',
    header: '原因',
    accessor: (r) => describeEquipmentReason(r.reasonCode ?? '').label,
  },
  { key: 'severity', header: '级别', width: 'w-24' },
  { key: 'startUtc', header: '开始', width: 'w-44' },
  { key: 'endUtc', header: '结束', width: 'w-44' },
  { key: 'sourceReferenceId', header: '关联业务', accessor: (r) => r.sourceReferenceId ?? '无' },
]

function routeQuery(key: string) {
  const value = route.query[key]
  return Array.isArray(value) ? (value[0] ?? '') : (value?.toString() ?? '')
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
function factorVariant(value?: number | null) {
  return value === null || value === undefined ? 'warning' : 'success'
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
      title="OEE 与可用性"
      :breadcrumbs="[{ label: '设备监控（IoT）' }]"
      :count="filters.deviceAssetId || '选择设备'"
    >
      <template #actions>
        <NvButton size="sm" type="button" variant="outline" as-child>
          <RouterLink
            :to="{
              path: '/equipment/telemetry/history',
              query: {
                deviceAssetId: filters.deviceAssetId,
                windowEndUtc: filters.windowEndUtc,
                windowStartUtc: filters.windowStartUtc,
              },
            }"
          >
            <LineChartIcon aria-hidden="true" />
            历史趋势
          </RouterLink>
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
          :disabled="oeePending || !filters.deviceAssetId.trim()"
          @click="refreshOee"
        >
          <RefreshCwIcon aria-hidden="true" />
          查询
        </NvButton>
      </template>
    </NvPageHeader>

    <NvToolbar :show-search="false">
      <template #filters>
        <NvInput
          v-model="filters.deviceAssetId"
          class="h-9 w-56"
          placeholder="设备编号"
          aria-label="设备编号"
        />
        <NvInput
          v-model="filters.windowStartUtc"
          class="h-9 w-64"
          placeholder="开始时间 ISO"
          aria-label="开始时间"
        />
        <NvInput
          v-model="filters.windowEndUtc"
          class="h-9 w-64"
          placeholder="结束时间 ISO"
          aria-label="结束时间"
        />
      </template>
    </NvToolbar>

    <p class="rounded-lg border bg-muted/30 p-3 text-sm leading-6 text-muted-foreground">
      {{ limitation }}
    </p>
    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <NvSectionCards :columns="4">
      <NvSectionCard
        description="可用率"
        :value="formatOeeRate(oee?.availabilityRate)"
        hint="按运行状态持续时间计算"
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
      <NvSectionCard description="OEE" :value="formatOeeRate(oee?.oeeRate)" hint="三项因子的乘积" />
      <NvSectionCard
        description="状态样本"
        :value="oee?.stateSampleCount ?? 0"
        hint="当前窗口内状态事实"
      />
    </NvSectionCards>

    <div class="grid gap-4 lg:grid-cols-[320px_minmax(0,1fr)]">
      <div class="rounded-lg border bg-card p-4">
        <h2 class="text-sm font-semibold text-foreground">计算依据</h2>
        <div class="mt-4 grid gap-3 text-sm">
          <div class="flex items-center justify-between gap-3">
            <span class="text-muted-foreground">性能系数</span>
            <NvBadge class="rounded-sm" :variant="factorVariant(oee?.performanceRate)">{{
              formatOeeRate(oee?.performanceRate)
            }}</NvBadge>
          </div>
          <div class="flex items-center justify-between gap-3">
            <span class="text-muted-foreground">质量系数</span>
            <NvBadge class="rounded-sm" :variant="factorVariant(oee?.qualityRate)">{{
              formatOeeRate(oee?.qualityRate)
            }}</NvBadge>
          </div>
          <div class="flex items-center justify-between gap-3">
            <span class="text-muted-foreground">MES 报工</span>
            <span class="font-medium text-foreground">{{ oee?.productionFactCount ?? 0 }} 条</span>
          </div>
          <div class="flex items-center justify-between gap-3">
            <span class="text-muted-foreground">理论产出</span>
            <span class="font-medium text-foreground">{{
              formatOeeQuantity(oee?.expectedOutputQuantity, oee?.outputUomCode)
            }}</span>
          </div>
          <div class="flex items-center justify-between gap-3">
            <span class="text-muted-foreground">不可用窗口</span>
            <span class="font-medium text-foreground">{{ blockedWindowCount }}</span>
          </div>
        </div>
        <div
          v-if="oee?.isDegraded"
          class="mt-4 rounded-md bg-muted p-3 text-xs text-muted-foreground"
        >
          <p class="font-medium text-foreground">当前 OEE 数据不完整</p>
          <ul class="mt-1 list-disc pl-4">
            <li v-for="reason in oeeDegradedReasons" :key="reason">{{ reason }}</li>
          </ul>
        </div>
      </div>

      <NvDataTable
        :columns="columns"
        :rows="availabilityWindows"
        :row-key="(r) => `${r.deviceAssetId}-${r.reasonCode}-${r.startUtc}`"
        :loading="oeePending"
        :searchable="false"
        :column-settings="false"
        empty-message="请输入设备编号和时间范围后查询设备可用性窗口。"
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
        <template #cell-severity="{ row }">
          <NvBadge class="rounded-sm" :variant="severityVariant(row.severity)">{{
            severityLabel(row.severity)
          }}</NvBadge>
        </template>
        <template #cell-startUtc="{ row }">{{ formatDateTime(row.startUtc) }}</template>
        <template #cell-endUtc="{ row }">{{ formatDateTime(row.endUtc) }}</template>
      </NvDataTable>
    </div>
  </BusinessLayout>
</template>
