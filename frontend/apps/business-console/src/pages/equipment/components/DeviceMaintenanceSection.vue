<script setup lang="ts">
import {
  describeAvailabilityStatus,
  describeEquipmentReason,
} from '@/composables/equipment/useBusinessEquipment'
import { NvBadge, NvButton, NvMetricCard, NvMetricStrip, NvSectionCards } from '@nerv-iip/ui'
import { CalendarRangeIcon, PackageSearchIcon, WrenchIcon } from '@lucide/vue'
import { RouterLink } from 'vue-router'
import { formatDateTime } from '@/utils/format'
import type { ShallowUnwrapRef } from 'vue'
import type { useDeviceDetailPage } from '../useDeviceDetailPage'

type View = ShallowUnwrapRef<ReturnType<typeof useDeviceDetailPage>>

defineProps<{
  MAINTENANCE_DETAIL_TAKE: View['MAINTENANCE_DETAIL_TAKE']
  alarmLabel: View['alarmLabel']
  currentDeviceInspections: View['currentDeviceInspections']
  currentDevicePlans: View['currentDevicePlans']
  currentDeviceSpareParts: View['currentDeviceSpareParts']
  currentDeviceWorkOrders: View['currentDeviceWorkOrders']
  filters: View['filters']
  inspectionResultLabel: View['inspectionResultLabel']
  intervalLabel: View['intervalLabel']
  maintenanceAvailabilityWindows: View['maintenanceAvailabilityWindows']
  maintenanceErrorMessage: View['maintenanceErrorMessage']
  maintenanceEventSegments: View['maintenanceEventSegments']
  maintenanceEventTotal: View['maintenanceEventTotal']
  maintenancePending: View['maintenancePending']
  maintenanceStatusLabel: View['maintenanceStatusLabel']
  quantityLabel: View['quantityLabel']
  reliabilityCells: View['reliabilityCells']
  reliabilityRangeLabel: View['reliabilityRangeLabel']
  sparePartWorkOrderLabel: View['sparePartWorkOrderLabel']
  workOrderLabel: View['workOrderLabel']
}>()
</script>

<template>
  <section class="grid gap-4">
    <div class="flex flex-wrap items-start justify-between gap-3">
      <h2 class="text-base font-semibold text-foreground">维护与可靠性</h2>
      <div class="flex flex-wrap gap-2">
        <NvButton size="sm" type="button" variant="outline" as-child>
          <RouterLink
            :to="{
              path: '/maintenance/work-orders',
              query: { deviceAssetId: filters.deviceAssetId },
            }"
          >
            <WrenchIcon aria-hidden="true" />
            维护工单
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
            保养计划
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
            备件需求
          </RouterLink>
        </NvButton>
      </div>
    </div>

    <p v-if="maintenanceErrorMessage" class="text-sm text-destructive" role="alert">
      {{ maintenanceErrorMessage }}
    </p>

    <NvSectionCards :columns="2">
      <NvMetricStrip :cells="reliabilityCells" />
      <NvMetricCard
        variant="breakdown"
        :label="`${reliabilityRangeLabel}维护事件`"
        :value="maintenanceEventTotal"
        unit=" 次"
        :segments="maintenanceEventSegments"
      />
    </NvSectionCards>

    <div class="grid gap-4 xl:grid-cols-2">
      <div class="rounded-lg border bg-card">
        <div class="border-b px-4 py-3">
          <h3 class="text-sm font-semibold text-foreground">维修工单</h3>
        </div>
        <div class="grid gap-3 p-4">
          <div v-if="maintenancePending" class="text-sm text-muted-foreground">
            正在读取维护记录。
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
            <p v-if="row.sourceAlarmId || row.relatedAlarmId" class="text-xs text-muted-foreground">
              关联报警 {{ alarmLabel(row.sourceAlarmId ?? row.relatedAlarmId) }}
            </p>
          </div>
          <div
            v-if="!maintenancePending && !currentDeviceWorkOrders.length"
            class="rounded-lg border border-dashed p-4 text-sm text-muted-foreground"
          >
            最近
            {{ MAINTENANCE_DETAIL_TAKE }}
            张维修工单中没有该设备的记录。可前往维护工单查看完整记录或新建工单。
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
            <span class="text-sm font-medium text-foreground">{{ row.planCode ?? '—' }}</span>
            <span class="text-xs text-muted-foreground"
              >周期 {{ intervalLabel(row.interval) }} · 起始 {{ row.startsOn ?? '未设置' }}</span
            >
          </div>
          <div
            v-for="row in currentDeviceInspections"
            :key="row.inspectionId"
            class="grid gap-1 rounded-lg border p-3"
          >
            <span class="text-sm font-medium text-foreground"
              >点检 · {{ formatDateTime(row.inspectedAtUtc) }}</span
            >
            <span class="text-xs text-muted-foreground"
              >结果 {{ inspectionResultLabel(row.result) }}</span
            >
          </div>
          <div
            v-if="
              !maintenancePending && !currentDevicePlans.length && !currentDeviceInspections.length
            "
            class="rounded-lg border border-dashed p-4 text-sm text-muted-foreground"
          >
            暂无与该设备关联的保养计划或点检记录。点检通过维护工单或保养计划关联到设备，未关联设备的点检不在此显示。
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
            <span class="text-sm font-medium text-foreground">{{ row.skuCode ?? '备件物料' }}</span>
            <span class="text-xs text-muted-foreground"
              >数量 {{ quantityLabel(row) }} · 工单 {{ sparePartWorkOrderLabel(row) }}</span
            >
          </div>
          <div
            v-if="!maintenancePending && !currentDeviceSpareParts.length"
            class="rounded-lg border border-dashed p-4 text-sm text-muted-foreground"
          >
            当前设备没有备件需求。可前往库存可用量核对备件库存。
          </div>
        </div>
      </div>

      <div class="rounded-lg border bg-card">
        <div class="border-b px-4 py-3">
          <h3 class="text-sm font-semibold text-foreground">维护占用时段</h3>
          <p class="mt-1 text-xs text-muted-foreground">
            来自保养计划的占用时段，与上方设备占用时段分开统计。
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
                :variant="describeAvailabilityStatus(row.availabilityStatus).tone"
                >{{ describeAvailabilityStatus(row.availabilityStatus).label }}</NvBadge
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
            当前设备没有维护占用时段。
          </div>
        </div>
      </div>
    </div>
  </section>
</template>
