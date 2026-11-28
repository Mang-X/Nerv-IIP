<script setup lang="ts">
import DeviceControlSheet from '@/components/equipment/DeviceControlSheet.vue'
import DeviceTelemetryPointsSheet from '@/components/equipment/DeviceTelemetryPointsSheet.vue'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvButton,
  NvDropdownMenu,
  NvDropdownMenuContent,
  NvDropdownMenuItem,
  NvDropdownMenuTrigger,
  NvPageHeader,
} from '@nerv-iip/ui'
import {
  ArrowLeftIcon,
  CalendarRangeIcon,
  ClipboardCheckIcon,
  GaugeIcon,
  HashIcon,
  LineChartIcon,
  PackageSearchIcon,
  RefreshCwIcon,
  SlidersHorizontalIcon,
  TrendingUpIcon,
  WrenchIcon,
} from '@lucide/vue'
import { RouterLink } from 'vue-router'
import DeviceLiveTelemetrySection from './components/DeviceLiveTelemetrySection.vue'
import DeviceDetailMetrics from './components/DeviceDetailMetrics.vue'
import DeviceStateAndAvailability from './components/DeviceStateAndAvailability.vue'
import DevicePerformanceSection from './components/DevicePerformanceSection.vue'
import DeviceMaintenanceSection from './components/DeviceMaintenanceSection.vue'
import DeviceControlHistory from './components/DeviceControlHistory.vue'
import { useDeviceDetailPage } from './useDeviceDetailPage'

definePage({
  meta: {
    requiresAuth: true,
    title: '设备详情',
    requiredPermissions: ['business.iiot.telemetry.read'],
  },
})

const {
  MAINTENANCE_DETAIL_TAKE,
  activeAlarms,
  alarmLabel,
  availabilityWindows,
  badgeVariant,
  blockCount,
  canControlDevice,
  canCreateMaintenanceWorkOrder,
  canManageTelemetryPoints,
  columns,
  controlColumns,
  controlCommandRowKey,
  controlCommands,
  controlCommandsErrorMessage,
  refreshControlCommands,
  controlCommandsPending,
  controlCommandsTotal,
  controlPage,
  controlPageSize,
  controlSheetOpen,
  cumulativeRuntimeCardValue,
  currentDeviceInspections,
  currentDevicePlans,
  currentDeviceRuntimePlan,
  currentDeviceSpareParts,
  currentDeviceWorkOrders,
  currentState,
  deviceCode,
  deviceName,
  devicePending,
  deviceTitle,
  errorMessage,
  filters,
  health,
  healthErrorMessage,
  healthPending,
  historyCount,
  historyFilters,
  inspectionResultLabel,
  intervalLabel,
  liveTelemetryDeviceAssetId,
  maintenanceAvailabilityWindows,
  maintenanceErrorMessage,
  maintenanceEventSegments,
  maintenanceEventTotal,
  maintenancePending,
  maintenanceStatusLabel,
  mostUrgentOkRuntimeCandidate,
  oee,
  oeeDegradedReasons,
  oeeFacets,
  oeeFilters,
  pointsSheetOpen,
  quantityLabel,
  rateFoot,
  rateProgress,
  rateValue,
  recordDowntime,
  refreshAll,
  reliabilityCells,
  reliabilityRangeLabel,
  resolveUom,
  runtimeHoursCardHint,
  runtimeUntilNextCardDescription,
  runtimeUntilNextCardHint,
  runtimeUntilNextCardValue,
  runtimeUntilNextProgress,
  severityLabel,
  severityVariant,
  sourceText,
  sourceTone,
  sparePartWorkOrderLabel,
  stateText,
  telemetryErrorMessage,
  telemetryPending,
  workOrderLabel,
} = useDeviceDetailPage()
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      :title="deviceTitle ? `设备详情：${deviceTitle}` : '设备详情'"
      :breadcrumbs="[{ label: '设备监控' }]"
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
        <NvButton
          v-if="filters.deviceAssetId"
          size="sm"
          type="button"
          variant="outline"
          @click="pointsSheetOpen = true"
        >
          <HashIcon aria-hidden="true" />
          采集点位
        </NvButton>
        <NvButton size="sm" type="button" variant="outline" @click="recordDowntime">
          <WrenchIcon aria-hidden="true" />
          记录停机
        </NvButton>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="devicePending"
          @click="refreshAll"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
        <NvDropdownMenu>
          <NvDropdownMenuTrigger as-child
            ><NvButton size="sm" variant="outline">更多</NvButton></NvDropdownMenuTrigger
          >
          <NvDropdownMenuContent align="end">
            <NvDropdownMenuItem as-child>
              <RouterLink to="/equipment"><ArrowLeftIcon aria-hidden="true" />返回看板</RouterLink>
            </NvDropdownMenuItem>
            <NvDropdownMenuItem as-child>
              <RouterLink
                :to="{
                  path: '/equipment/telemetry/history',
                  query: {
                    deviceAssetId: filters.deviceAssetId,
                    windowEndUtc: historyFilters.windowEndUtc,
                    windowStartUtc: historyFilters.windowStartUtc,
                  },
                }"
              >
                <LineChartIcon aria-hidden="true" />
                历史趋势
              </RouterLink>
            </NvDropdownMenuItem>
            <NvDropdownMenuItem as-child>
              <RouterLink
                :to="{
                  path: '/equipment/telemetry/oee',
                  query: { deviceAssetId: filters.deviceAssetId },
                }"
              >
                <GaugeIcon aria-hidden="true" />
                OEE 趋势与横比
              </RouterLink>
            </NvDropdownMenuItem>
            <NvDropdownMenuItem v-if="canCreateMaintenanceWorkOrder" as-child>
              <RouterLink
                :to="{
                  path: '/maintenance/work-orders',
                  query: { deviceAssetId: filters.deviceAssetId },
                }"
              >
                <WrenchIcon aria-hidden="true" />
                创建维修工单
              </RouterLink>
            </NvDropdownMenuItem>
            <NvDropdownMenuItem as-child>
              <RouterLink
                :to="{
                  path: '/maintenance/reliability',
                  query: { deviceAssetId: filters.deviceAssetId },
                }"
              >
                <TrendingUpIcon aria-hidden="true" />
                可靠性
              </RouterLink>
            </NvDropdownMenuItem>
            <NvDropdownMenuItem as-child>
              <RouterLink
                :to="{
                  path: '/maintenance/availability',
                  query: { deviceAssetId: filters.deviceAssetId },
                }"
              >
                <CalendarRangeIcon aria-hidden="true" />
                可用窗口
              </RouterLink>
            </NvDropdownMenuItem>
            <NvDropdownMenuItem as-child>
              <RouterLink
                :to="{
                  path: '/maintenance/inspections',
                  query: { deviceAssetId: filters.deviceAssetId },
                }"
              >
                <ClipboardCheckIcon aria-hidden="true" />
                点检
              </RouterLink>
            </NvDropdownMenuItem>
            <NvDropdownMenuItem as-child>
              <RouterLink
                :to="{
                  path: '/maintenance/spare-parts',
                  query: { deviceAssetId: filters.deviceAssetId },
                }"
              >
                <PackageSearchIcon aria-hidden="true" />
                备件
              </RouterLink>
            </NvDropdownMenuItem>
          </NvDropdownMenuContent>
        </NvDropdownMenu>
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
      <DeviceDetailMetrics
        :active-alarms="activeAlarms"
        :block-count="blockCount"
        :current-state="currentState"
        :source-text="sourceText"
        :source-tone="sourceTone"
        :state-text="stateText"
      />

      <DeviceLiveTelemetrySection
        v-if="liveTelemetryDeviceAssetId"
        :device-asset-id="liveTelemetryDeviceAssetId"
      />

      <DeviceStateAndAvailability
        :device-asset-id="filters.deviceAssetId"
        :active-alarms="activeAlarms"
        :availability-windows="availabilityWindows"
        :badge-variant="badgeVariant"
        :columns="columns"
        :current-state="currentState"
        :device-code="deviceCode"
        :device-name="deviceName"
        :device-pending="devicePending"
        :health="health"
        :health-error-message="healthErrorMessage"
        :health-pending="healthPending"
        :severity-label="severityLabel"
        :severity-variant="severityVariant"
        :source-text="sourceText"
        :source-tone="sourceTone"
        :state-text="stateText"
      />

      <DevicePerformanceSection
        :cumulative-runtime-card-value="cumulativeRuntimeCardValue"
        :current-device-runtime-plan="currentDeviceRuntimePlan"
        :filters="filters"
        :history-count="historyCount"
        :history-filters="historyFilters"
        :most-urgent-ok-runtime-candidate="mostUrgentOkRuntimeCandidate"
        :oee="oee"
        :oee-degraded-reasons="oeeDegradedReasons"
        :oee-facets="oeeFacets"
        :oee-filters="oeeFilters"
        :rate-foot="rateFoot"
        :rate-progress="rateProgress"
        :rate-value="rateValue"
        :resolve-uom="resolveUom"
        :runtime-hours-card-hint="runtimeHoursCardHint"
        :runtime-until-next-card-description="runtimeUntilNextCardDescription"
        :runtime-until-next-card-hint="runtimeUntilNextCardHint"
        :runtime-until-next-card-value="runtimeUntilNextCardValue"
        :runtime-until-next-progress="runtimeUntilNextProgress"
        :telemetry-error-message="telemetryErrorMessage"
        :telemetry-pending="telemetryPending"
      />

      <DeviceMaintenanceSection
        :-m-a-i-n-t-e-n-a-n-c-e_-d-e-t-a-i-l_-t-a-k-e="MAINTENANCE_DETAIL_TAKE"
        :alarm-label="alarmLabel"
        :current-device-inspections="currentDeviceInspections"
        :current-device-plans="currentDevicePlans"
        :current-device-spare-parts="currentDeviceSpareParts"
        :current-device-work-orders="currentDeviceWorkOrders"
        :filters="filters"
        :inspection-result-label="inspectionResultLabel"
        :interval-label="intervalLabel"
        :maintenance-availability-windows="maintenanceAvailabilityWindows"
        :maintenance-error-message="maintenanceErrorMessage"
        :maintenance-event-segments="maintenanceEventSegments"
        :maintenance-event-total="maintenanceEventTotal"
        :maintenance-pending="maintenancePending"
        :maintenance-status-label="maintenanceStatusLabel"
        :quantity-label="quantityLabel"
        :reliability-cells="reliabilityCells"
        :reliability-range-label="reliabilityRangeLabel"
        :spare-part-work-order-label="sparePartWorkOrderLabel"
        :work-order-label="workOrderLabel"
      />

      <DeviceControlHistory
        v-model:control-sheet-open="controlSheetOpen"
        v-model:control-page="controlPage"
        v-model:control-page-size="controlPageSize"
        :can-control-device="canControlDevice"
        :control-columns="controlColumns"
        :control-command-row-key="controlCommandRowKey"
        :control-commands="controlCommands"
        :control-commands-error-message="controlCommandsErrorMessage"
        :control-commands-pending="controlCommandsPending"
        :control-commands-total="controlCommandsTotal"
        @retry="refreshControlCommands"
      />

      <DeviceControlSheet
        v-if="filters.deviceAssetId"
        v-model:open="controlSheetOpen"
        :device-asset-id="filters.deviceAssetId"
      />
      <DeviceTelemetryPointsSheet
        v-if="filters.deviceAssetId"
        v-model:open="pointsSheetOpen"
        :device-asset-id="filters.deviceAssetId"
        :device-title="deviceTitle || '设备'"
        :can-manage="canManageTelemetryPoints"
      />
    </template>
  </BusinessLayout>
</template>
