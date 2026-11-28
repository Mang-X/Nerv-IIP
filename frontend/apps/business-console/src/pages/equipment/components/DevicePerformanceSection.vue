<script setup lang="ts">
import {
  describeTelemetryOeeLimitations,
  formatOeeQuantity,
} from '@/composables/equipment/useBusinessTelemetry'
import { NvButton, NvMetricCard, NvSectionCards } from '@nerv-iip/ui'
import { GaugeIcon, LineChartIcon, Settings2Icon } from '@lucide/vue'
import { RouterLink } from 'vue-router'
import { formatDateTime } from '@/utils/format'
import type { ShallowUnwrapRef } from 'vue'
import type { useDeviceDetailPage } from '../useDeviceDetailPage'

type View = ShallowUnwrapRef<ReturnType<typeof useDeviceDetailPage>>

defineProps<{
  cumulativeRuntimeCardValue: View['cumulativeRuntimeCardValue']
  currentDeviceRuntimePlan: View['currentDeviceRuntimePlan']
  filters: View['filters']
  historyCount: View['historyCount']
  historyFilters: View['historyFilters']
  mostUrgentOkRuntimeCandidate: View['mostUrgentOkRuntimeCandidate']
  oee: View['oee']
  oeeDegradedReasons: View['oeeDegradedReasons']
  oeeFacets: View['oeeFacets']
  oeeFilters: View['oeeFilters']
  rateFoot: View['rateFoot']
  rateProgress: View['rateProgress']
  rateValue: View['rateValue']
  resolveUom: View['resolveUom']
  runtimeHoursCardHint: View['runtimeHoursCardHint']
  runtimeUntilNextCardDescription: View['runtimeUntilNextCardDescription']
  runtimeUntilNextCardHint: View['runtimeUntilNextCardHint']
  runtimeUntilNextCardValue: View['runtimeUntilNextCardValue']
  runtimeUntilNextProgress: View['runtimeUntilNextProgress']
  telemetryErrorMessage: View['telemetryErrorMessage']
  telemetryPending: View['telemetryPending']
}>()
</script>

<template>
  <section class="grid gap-4">
    <div class="flex flex-wrap items-start justify-between gap-3">
      <div>
        <h2 class="text-base font-semibold text-foreground">设备运行指标</h2>
        <p class="mt-1 text-sm text-muted-foreground">
          {{ describeTelemetryOeeLimitations() }}
        </p>
      </div>
      <div class="flex flex-wrap gap-2">
        <NvButton size="sm" type="button" variant="outline" as-child>
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
        </NvButton>
        <NvButton size="sm" type="button" variant="outline" as-child>
          <RouterLink
            :to="{
              path: '/equipment/telemetry/oee',
              query: { deviceAssetId: filters.deviceAssetId },
            }"
          >
            <GaugeIcon aria-hidden="true" />
            OEE 趋势与横比
          </RouterLink>
        </NvButton>
        <NvButton size="sm" type="button" variant="outline" as-child>
          <RouterLink
            :to="{ path: '/equipment/alarms', query: { deviceAssetId: filters.deviceAssetId } }"
          >
            <Settings2Icon aria-hidden="true" />
            设备报警
          </RouterLink>
        </NvButton>
      </div>
    </div>

    <p v-if="telemetryErrorMessage" class="text-sm text-destructive" role="alert">
      {{ telemetryErrorMessage }}
    </p>

    <NvSectionCards :columns="3">
      <NvMetricCard
        variant="target"
        label="可用率"
        :value="rateValue(oee?.availabilityRate)"
        :progress="rateProgress(oee?.availabilityRate)"
        target-label="目标 100%"
        :foot-start="rateFoot(oee?.availabilityRate, '按设备运行状态时长计算')"
      />
      <NvMetricCard
        variant="target"
        label="加载率"
        :value="rateValue(oee?.loadingRate)"
        :progress="rateProgress(oee?.loadingRate)"
        target-label="目标 100%"
        :foot-start="rateFoot(oee?.loadingRate, '已扣除计划停机时间')"
      />
      <NvMetricCard
        variant="target"
        label="性能率"
        :value="rateValue(oee?.performanceRate)"
        :progress="rateProgress(oee?.performanceRate)"
        target-label="目标 100%"
        :foot-start="rateFoot(oee?.performanceRate, '实际产出 ÷ 理论产出')"
      />
      <NvMetricCard
        variant="target"
        label="质量率"
        :value="rateValue(oee?.qualityRate)"
        :progress="rateProgress(oee?.qualityRate)"
        target-label="目标 100%"
        :foot-start="rateFoot(oee?.qualityRate, '良品 ÷ 总产出')"
      />
      <!-- OEE 是三项因子相乘的结果，不是各因子之和：用 facets 并列三个因子，禁止画成环。 -->
      <NvMetricCard
        variant="facets"
        label="OEE"
        :value="rateValue(oee?.oeeRate)"
        :facets="oeeFacets"
      />
      <NvMetricCard
        variant="sparkline"
        label="历史事件"
        :value="historyCount"
        foot-start="所选时间范围内的历史事件数"
      />
    </NvSectionCards>

    <NvSectionCards :columns="2">
      <NvMetricCard
        label="累计运行小时"
        :value="cumulativeRuntimeCardValue"
        :foot-start="runtimeHoursCardHint"
        variant="sparkline"
      />
      <NvMetricCard
        v-if="currentDeviceRuntimePlan"
        variant="target"
        :label="runtimeUntilNextCardDescription"
        :value="runtimeUntilNextCardValue"
        :progress="runtimeUntilNextProgress"
        :target-label="
          runtimeUntilNextProgress === undefined
            ? undefined
            : `阈值 ${mostUrgentOkRuntimeCandidate?.plan.nextDueRuntimeHours ?? '—'} 小时`
        "
        :foot-start="runtimeUntilNextCardHint"
      />
      <NvMetricCard
        v-else
        variant="alert"
        tone="neutral"
        label="距下次保养（运行小时）"
        value="未设运行小时计划"
        foot-start="该设备没有运行小时型保养计划；到期以日历周期为准。"
      />
    </NvSectionCards>

    <div>
      <div class="rounded-lg border bg-card">
        <div class="border-b px-4 py-3">
          <h3 class="text-sm font-semibold text-foreground">OEE 计算依据</h3>
          <p class="mt-1 text-xs text-muted-foreground">
            算法与「OEE 趋势与横比」页一致，此处不另行计算。
          </p>
        </div>
        <div class="grid gap-3 p-4 text-sm">
          <div class="grid grid-cols-[120px_minmax(0,1fr)] gap-2">
            <span class="text-muted-foreground">统计时段</span>
            <span
              >{{ formatDateTime(oee?.windowStartUtc ?? oeeFilters.windowStartUtc) }} -
              {{ formatDateTime(oee?.windowEndUtc ?? oeeFilters.windowEndUtc) }}</span
            >
          </div>
          <div class="grid grid-cols-[120px_minmax(0,1fr)] gap-2">
            <span class="text-muted-foreground">设备状态记录</span>
            <span>{{ oee?.stateSampleCount ?? 0 }} 条</span>
          </div>
          <div class="grid grid-cols-[120px_minmax(0,1fr)] gap-2">
            <span class="text-muted-foreground">报工记录</span>
            <span>{{ oee?.productionFactCount ?? 0 }} 条</span>
          </div>
          <div class="grid grid-cols-[120px_minmax(0,1fr)] gap-2">
            <span class="text-muted-foreground">理论产出</span>
            <span>{{
              formatOeeQuantity(
                oee?.expectedOutputQuantity,
                resolveUom(oee?.outputUomCode) ?? oee?.outputUomCode,
              )
            }}</span>
          </div>
          <div v-if="oee?.isDegraded" class="rounded-md bg-muted p-3 text-xs text-muted-foreground">
            <p class="font-medium text-foreground">当前 OEE 数据不完整</p>
            <ul class="mt-1 list-disc pl-4">
              <li v-for="reason in oeeDegradedReasons" :key="reason">{{ reason }}</li>
            </ul>
          </div>
        </div>
      </div>
    </div>
  </section>
</template>
