<script setup lang="ts">
import type {
  BusinessConsoleMesWipSummaryRow,
  BusinessConsoleTelemetryOeeAggregateBucket,
} from '@nerv-iip/api-client'
import { NvBadge } from '@nerv-iip/ui'
import type { BusinessReadState } from '@/composables/businessReadState'
import { describeTelemetryOeeDegradation, formatOeeRate } from '@/composables/useBusinessTelemetry'
import { useMasterDataDisplayNames } from '@/composables/useMasterDataDisplayNames'
import { readFaceText } from '@/utils/readFace'

defineProps<{
  canReadWip: boolean
  wipState: BusinessReadState
  wipTotal: number
  wipRows: BusinessConsoleMesWipSummaryRow[]
  canReadOee: boolean
  isSkuDimension: boolean
  oeePending: boolean
  oeeError?: unknown
  oeeBuckets: BusinessConsoleTelemetryOeeAggregateBucket[]
}>()

function wipLabel(row: BusinessConsoleMesWipSummaryRow) {
  return row.operationTaskNo ?? readFaceText(row.operationTaskId, '未编号工序')
}

function workCenterLabel(row: BusinessConsoleMesWipSummaryRow) {
  const code = row.workCenterCode ?? readFaceText(row.workCenterId, '')
  return row.workCenterName ?? resolveWorkCenter(code) ?? (code || '未标工作中心')
}

const { resolveSite, resolveShift, resolveWorkCenter } = useMasterDataDisplayNames({
  sites: true,
  shifts: true,
  workCenters: true,
})

/** 性能率行的对象：按天是「业务日 · 工厂」，按班次是「班次 · 业务日」，按工作中心是工作中心名称。 */
function bucketLabel(bucket: BusinessConsoleTelemetryOeeAggregateBucket) {
  const code = bucket.dimensionValue?.trim()
  const date = bucket.businessDate?.trim()
  switch (bucket.dimension) {
    case 'day':
      return [date, code && (resolveSite(code) ?? code)].filter(Boolean).join(' · ') || '—'
    case 'shift':
      return [code && (resolveShift(code) ?? code), date].filter(Boolean).join(' · ') || '—'
    default:
      return (code && (resolveWorkCenter(code) ?? code)) || '—'
  }
}

function rateLabel(rate: number | null | undefined) {
  return rate == null ? '—' : formatOeeRate(rate)
}

function degradationLabel(bucket: BusinessConsoleTelemetryOeeAggregateBucket) {
  const reasons = bucket.degradedReasons ?? []
  return reasons.length ? reasons.map(describeTelemetryOeeDegradation).join('；') : '数据不完整'
}
</script>

<template>
  <section class="grid gap-4 lg:grid-cols-2" aria-label="生产现场概况">
    <article class="rounded-lg border bg-card p-4 shadow-sm">
      <div class="flex items-start justify-between gap-3">
        <div>
          <h2 class="text-sm font-semibold text-foreground">当前在制</h2>
          <p class="mt-1 text-xs text-muted-foreground">按当前工作中心筛选展示，不参与产量统计。</p>
        </div>
        <NvBadge v-if="canReadWip && wipState === 'ready'" variant="neutral">
          {{ wipTotal }} 个在制工序
        </NvBadge>
      </div>

      <p v-if="!canReadWip" class="mt-4 text-sm text-muted-foreground">没有查看在制工序的权限</p>
      <p v-else-if="wipState === 'idle'" class="mt-4 text-sm text-muted-foreground">
        请选择有效业务范围
      </p>
      <p v-else-if="wipState === 'loading'" class="mt-4 text-sm text-muted-foreground">
        正在读取当前在制…
      </p>
      <p v-else-if="wipState === 'error'" class="mt-4 text-sm text-destructive">
        当前在制读取失败，无法判断现场状态
      </p>
      <p v-else-if="wipTotal === 0" class="mt-4 text-sm text-muted-foreground">当前没有在制工序</p>
      <ul v-else class="mt-4 grid gap-2">
        <li
          v-for="row in wipRows"
          :key="`${row.workOrderId}-${row.operationTaskId}`"
          class="flex items-center justify-between gap-3 rounded-md bg-muted/45 px-3 py-2"
        >
          <div class="min-w-0">
            <p class="truncate text-sm font-medium text-foreground">{{ wipLabel(row) }}</p>
            <p class="truncate text-xs text-muted-foreground">
              {{ row.workOrderNo ?? readFaceText(row.workOrderId, '未编号工单') }}
            </p>
          </div>
          <span class="shrink-0 text-xs text-muted-foreground">{{ workCenterLabel(row) }}</span>
        </li>
      </ul>
    </article>

    <article class="rounded-lg border bg-card p-4 shadow-sm">
      <div>
        <h2 class="text-sm font-semibold text-foreground">设备性能率</h2>
        <p class="mt-1 text-xs text-muted-foreground">设备实际开动时间占计划开动时间的比例。</p>
      </div>

      <p v-if="!canReadOee" class="mt-4 text-sm text-muted-foreground">没有查看设备 OEE 的权限</p>
      <p v-else-if="isSkuDimension" class="mt-4 text-sm text-muted-foreground">
        按物料统计时不提供设备性能率，请切换到按业务日、按班次或按工作中心查看。
      </p>
      <p v-else-if="oeePending" class="mt-4 text-sm text-muted-foreground">正在读取设备性能率…</p>
      <p v-else-if="oeeError" class="mt-4 text-sm text-destructive">
        设备性能率读取失败，无法判断设备效率
      </p>
      <p v-else-if="oeeBuckets.length === 0" class="mt-4 text-sm text-muted-foreground">
        当前范围暂无设备性能率
      </p>
      <ul v-else class="mt-4 grid gap-2">
        <li
          v-for="bucket in oeeBuckets"
          :key="`${bucket.dimension}-${bucket.dimensionValue}-${bucket.bucketStartUtc}`"
          class="rounded-md bg-muted/45 px-3 py-2"
        >
          <div class="flex items-center justify-between gap-3">
            <span class="truncate text-sm font-medium text-foreground">
              {{ bucketLabel(bucket) }}
            </span>
            <div class="flex items-center gap-2">
              <span class="font-mono text-sm font-semibold tabular-nums">
                {{ rateLabel(bucket.performanceRate) }}
              </span>
              <NvBadge v-if="bucket.isDegraded" variant="warning">数据不完整</NvBadge>
            </div>
          </div>
          <p v-if="bucket.isDegraded" class="mt-1 text-xs text-muted-foreground">
            {{ degradationLabel(bucket) }}
          </p>
        </li>
      </ul>
    </article>
  </section>
</template>
