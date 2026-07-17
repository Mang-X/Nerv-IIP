<script setup lang="ts">
import type { BusinessConsoleConnectorCollectionHealthListItem } from '@nerv-iip/api-client'
import {
  connectorHealthStatusLabel,
  connectorSourceSystemLabel,
  formatSampleRate,
  isConnectorFault,
} from '@/composables/useBusinessTelemetry'
import { NvBadge } from '@nerv-iip/ui'
import { ActivityIcon, ChevronDownIcon, TriangleAlertIcon, TimerIcon } from '@lucide/vue'
import { computed } from 'vue'
import { RouterLink } from 'vue-router'

const props = defineProps<{
  connector: BusinessConsoleConnectorCollectionHealthListItem
  sampleRate: number | null
  expanded: boolean
}>()

defineEmits<{ toggle: [] }>()

const fieldConnectionLost = computed(
  () =>
    props.connector.offlineReason === 'field-connection' ||
    props.connector.connection?.status === 'lost',
)
const hostOffline = computed(() => props.connector.offlineReason === 'host-liveness')
const offline = computed(() => fieldConnectionLost.value || hostOffline.value)
const fault = computed(() => isConnectorFault(props.connector.status, props.connector.staleReason))
const connectionUnknown = computed(
  () => props.connector.connection == null || props.connector.connection.status === 'unknown',
)
const statusLabel = computed(() => {
  if (fieldConnectionLost.value) return '现场连接断开'
  if (hostOffline.value) return '采集主机离线'
  if (connectionUnknown.value) return '连接状态未知'
  return connectorHealthStatusLabel(props.connector.status, props.connector.staleReason)
})
const detailId = computed(
  () =>
    `connector-detail-${props.connector.connectorId ?? props.connector.connectorName ?? 'connector'}`,
)
const statusVariant = computed(() => {
  if (offline.value) return 'danger'
  if (fault.value) return 'warning'
  if (props.connector.status === 'current') return 'success'
  return 'neutral'
})
const cardTone = computed(() => {
  if (offline.value) return 'border-destructive/60 ring-1 ring-destructive/30'
  if (fault.value) return 'border-warning/60 ring-1 ring-warning/30'
  return ''
})

function formatCount(value?: number | null) {
  return value === null || value === undefined ? '无数据' : value.toLocaleString()
}
function formatDateTime(value?: string | null) {
  if (!value) return '无'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}
function formatDurationSince(value?: string | null) {
  if (!value) return '未知'
  const then = new Date(value).getTime()
  if (Number.isNaN(then)) return '未知'
  const seconds = Math.max(0, Math.floor((Date.now() - then) / 1000))
  if (seconds < 60) return `${seconds} 秒`
  const minutes = Math.floor(seconds / 60)
  if (minutes < 60) return `${minutes} 分钟`
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return `${hours} 小时 ${minutes % 60} 分钟`
  return `${Math.floor(hours / 24)} 天 ${hours % 24} 小时`
}

const offlineDuration = computed(() => {
  if (fieldConnectionLost.value) {
    const connection = props.connector.connection
    return `现场断开约 ${formatDurationSince(
      connection?.disconnectedSinceUtc ?? connection?.observedAtUtc,
    )}`
  }
  if (hostOffline.value) {
    return `主机离线约 ${formatDurationSince(props.connector.lastHeartbeatAtUtc)}`
  }
  return null
})
</script>

<template>
  <div class="rounded-lg border bg-card" :class="cardTone">
    <button
      type="button"
      class="flex w-full items-start justify-between gap-3 px-4 py-3 text-left"
      :aria-expanded="expanded"
      :aria-controls="detailId"
      @click="$emit('toggle')"
    >
      <div class="min-w-0">
        <p class="truncate text-sm font-semibold text-foreground">
          {{ connector.connectorName || connector.connectorId || '未命名连接器' }}
        </p>
        <p class="mt-0.5 flex items-center gap-1.5 text-xs text-muted-foreground">
          <NvBadge class="rounded-sm" variant="neutral">{{
            connectorSourceSystemLabel(connector.sourceSystem)
          }}</NvBadge>
          <span class="truncate">{{ connector.connectorId }}</span>
        </p>
      </div>
      <div class="flex shrink-0 items-center gap-1.5">
        <NvBadge class="rounded-sm" :variant="statusVariant">{{ statusLabel }}</NvBadge>
        <ChevronDownIcon
          class="size-4 text-muted-foreground transition-transform"
          :class="expanded ? 'rotate-180' : ''"
          aria-hidden="true"
        />
      </div>
    </button>

    <div class="grid grid-cols-3 gap-2 border-t px-4 py-3 text-center">
      <div>
        <p class="text-xs text-muted-foreground">采样速率</p>
        <p class="text-sm font-semibold tabular-nums text-foreground">
          {{ formatSampleRate(sampleRate) }}
        </p>
        <p class="text-[11px] text-muted-foreground">
          累计接收 {{ formatCount(connector.receivedCount) }}
        </p>
      </div>
      <div>
        <p class="text-xs text-muted-foreground">丢样数</p>
        <p
          class="text-sm font-semibold tabular-nums"
          :class="(connector.droppedCount ?? 0) > 0 ? 'text-destructive' : 'text-foreground'"
        >
          {{ formatCount(connector.droppedCount) }}
        </p>
      </div>
      <div>
        <p class="text-xs text-muted-foreground">错误数</p>
        <p
          class="text-sm font-semibold tabular-nums"
          :class="(connector.errorCount ?? 0) > 0 ? 'text-destructive' : 'text-foreground'"
        >
          {{ formatCount(connector.errorCount) }}
        </p>
      </div>
    </div>

    <div class="flex flex-wrap gap-x-4 gap-y-1 border-t px-4 py-3 text-xs text-muted-foreground">
      <span class="inline-flex items-center gap-1">
        <ActivityIcon class="size-3" aria-hidden="true" />最后心跳
        {{ formatDateTime(connector.lastHeartbeatAtUtc) }}
      </span>
      <span>最后采样 {{ formatDateTime(connector.lastSampleAtUtc) }}</span>
      <span v-if="offlineDuration" class="inline-flex items-center gap-1 text-destructive">
        <TimerIcon class="size-3" aria-hidden="true" />{{ offlineDuration }}
      </span>
      <span v-else-if="fault" class="inline-flex items-center gap-1 text-warning-strong">
        <TriangleAlertIcon class="size-3" aria-hidden="true" />连接器上报异常停止
      </span>
    </div>

    <div v-if="expanded" :id="detailId" class="grid gap-2 border-t bg-muted/30 px-4 py-3 text-xs">
      <div class="grid grid-cols-2 gap-2">
        <span class="text-muted-foreground">连接器编号</span>
        <span class="text-right font-medium text-foreground">{{ connector.connectorId }}</span>
        <span class="text-muted-foreground">采集协议</span>
        <span class="text-right font-medium text-foreground">{{
          connectorSourceSystemLabel(connector.sourceSystem)
        }}</span>
        <span class="text-muted-foreground">指标上报时间</span>
        <span class="text-right font-medium text-foreground">{{
          formatDateTime(connector.metricsReportedAtUtc)
        }}</span>
        <span class="text-muted-foreground">最近采样时间</span>
        <span class="text-right font-medium text-foreground">{{
          formatDateTime(connector.lastSampleAtUtc)
        }}</span>
      </div>
      <p class="text-muted-foreground">
        本卡片按连接器汇总心跳与采样吞吐。该连接器覆盖的逐条采集标签与实时数值，可在
        <RouterLink
          to="/equipment/telemetry/tags"
          class="text-brand underline-offset-4 hover:underline"
          >采集标签</RouterLink
        >
        中按设备查看。
      </p>
    </div>
  </div>
</template>
