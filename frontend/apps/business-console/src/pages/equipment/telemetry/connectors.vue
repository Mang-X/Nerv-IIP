<script setup lang="ts">
import type { BusinessConsoleConnectorCollectionHealthListItem } from '@nerv-iip/api-client'
import {
  connectorHealthStatusLabel,
  connectorSourceSystemLabel,
  formatSampleRate,
  isConnectorHeartbeatLost,
  useBusinessTelemetryConnectors,
} from '@/composables/useBusinessTelemetry'
import { notifyError } from '@/utils/notify'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { NvBadge, NvButton, NvPageHeader, NvSectionCard, NvSectionCards } from '@nerv-iip/ui'
import {
  ActivityIcon,
  ChevronDownIcon,
  GaugeIcon,
  HashIcon,
  RefreshCwIcon,
  TimerIcon,
} from '@lucide/vue'
import { computed, reactive, watch } from 'vue'
import { RouterLink } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '采集健康',
    requiredPermissions: ['business.iiot.telemetry.read'],
  },
})

const {
  connectors,
  connectorsError,
  connectorsPending,
  connectorsTotal,
  refreshConnectors,
  sampleRateByConnector,
} = useBusinessTelemetryConnectors()

// 查询失败一次性 toast（人话映射），不在页面留常驻错误条（AGENTS §1.5-B3）。
watch(connectorsError, (error) => {
  if (error) notifyError(error, '采集健康加载失败，请稍后重试。')
})

const onlineCount = computed(() => connectors.value.filter((c) => c.status === 'current').length)
const disconnectedCount = computed(
  () => connectors.value.filter((c) => isConnectorHeartbeatLost(c.status, c.staleReason)).length,
)
const stalledCount = computed(
  () => connectors.value.filter((c) => c.status === 'stale' && c.staleReason === 'metrics').length,
)

const expanded = reactive(new Set<string>())
function toggle(connectorId: string) {
  if (expanded.has(connectorId)) expanded.delete(connectorId)
  else expanded.add(connectorId)
}

type Connector = BusinessConsoleConnectorCollectionHealthListItem
function rowKey(connector: Connector) {
  return connector.connectorId ?? connector.connectorName ?? '未知连接器'
}
function heartbeatLost(connector: Connector) {
  return isConnectorHeartbeatLost(connector.status, connector.staleReason)
}
function statusVariant(connector: Connector) {
  if (heartbeatLost(connector)) return 'danger'
  if (connector.status === 'stale') return 'warning'
  if (connector.status === 'current') return 'success'
  return 'neutral'
}
function cardTone(connector: Connector) {
  if (heartbeatLost(connector)) return 'border-destructive/60 ring-1 ring-destructive/30'
  if (connector.status === 'stale') return 'border-amber-500/50 ring-1 ring-amber-500/20'
  return ''
}
function sampleRate(connector: Connector) {
  return sampleRateByConnector.value[connector.connectorId ?? ''] ?? null
}
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
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="采集健康"
      :breadcrumbs="[{ label: '设备监控（IoT）' }]"
      :count="`${connectorsTotal} 个采集连接器`"
    >
      <template #actions>
        <span class="text-xs text-muted-foreground">每 10 秒自动刷新</span>
        <NvButton size="sm" type="button" variant="outline" as-child>
          <RouterLink to="/equipment/telemetry/tags"
            ><HashIcon aria-hidden="true" />采集标签</RouterLink
          >
        </NvButton>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="connectorsPending"
          @click="refreshConnectors"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <NvSectionCards :columns="4">
      <NvSectionCard description="采集连接器" :value="connectorsTotal" hint="已上报采集健康" />
      <NvSectionCard description="在线" :value="onlineCount" hint="心跳与指标均新鲜" />
      <NvSectionCard description="断线" :value="disconnectedCount" hint="心跳失联" />
      <NvSectionCard description="采集停滞" :value="stalledCount" hint="仍在线但指标停更" />
    </NvSectionCards>

    <div
      v-if="connectorsPending && !connectors.length"
      class="rounded-lg border border-dashed p-6 text-sm text-muted-foreground"
    >
      正在加载采集连接器…
    </div>

    <div
      v-else-if="!connectors.length"
      class="rounded-lg border border-dashed p-6 text-sm text-muted-foreground"
    >
      暂无采集连接器。请确认 Connector Host 已注册并上报采集健康（心跳 + 采样吞吐）后再查看本页。
    </div>

    <div v-else class="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
      <div
        v-for="connector in connectors"
        :key="rowKey(connector)"
        class="rounded-lg border bg-card"
        :class="cardTone(connector)"
      >
        <button
          type="button"
          class="flex w-full items-start justify-between gap-3 px-4 py-3 text-left"
          :aria-expanded="expanded.has(rowKey(connector))"
          :aria-controls="`connector-detail-${rowKey(connector)}`"
          @click="toggle(rowKey(connector))"
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
            <NvBadge class="rounded-sm" :variant="statusVariant(connector)">{{
              connectorHealthStatusLabel(connector.status, connector.staleReason)
            }}</NvBadge>
            <ChevronDownIcon
              class="size-4 text-muted-foreground transition-transform"
              :class="expanded.has(rowKey(connector)) ? 'rotate-180' : ''"
              aria-hidden="true"
            />
          </div>
        </button>

        <div class="grid grid-cols-3 gap-2 border-t px-4 py-3 text-center">
          <div>
            <p class="text-xs text-muted-foreground">采样速率</p>
            <p class="text-sm font-semibold tabular-nums text-foreground">
              {{ formatSampleRate(sampleRate(connector)) }}
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

        <div
          class="flex flex-wrap gap-x-4 gap-y-1 border-t px-4 py-3 text-xs text-muted-foreground"
        >
          <span class="inline-flex items-center gap-1">
            <ActivityIcon class="size-3" aria-hidden="true" />最后心跳
            {{ formatDateTime(connector.lastHeartbeatAtUtc) }}
          </span>
          <span>最后采样 {{ formatDateTime(connector.lastSampleAtUtc) }}</span>
          <span
            v-if="heartbeatLost(connector)"
            class="inline-flex items-center gap-1 text-destructive"
          >
            <TimerIcon class="size-3" aria-hidden="true" />断线时长约
            {{ formatDurationSince(connector.lastHeartbeatAtUtc) }}
          </span>
          <span
            v-else-if="connector.status === 'stale'"
            class="inline-flex items-center gap-1 text-amber-600 dark:text-amber-500"
          >
            <GaugeIcon class="size-3" aria-hidden="true" />心跳仍在线，指标停更约
            {{ formatDurationSince(connector.metricsReportedAtUtc) }}
          </span>
        </div>

        <div
          v-if="expanded.has(rowKey(connector))"
          :id="`connector-detail-${rowKey(connector)}`"
          class="grid gap-2 border-t bg-muted/30 px-4 py-3 text-xs"
        >
          <div class="grid grid-cols-2 gap-2">
            <span class="text-muted-foreground">连接器编号</span>
            <span class="text-right font-medium text-foreground">{{ connector.connectorId }}</span>
            <span class="text-muted-foreground">来源系统</span>
            <span class="text-right font-medium text-foreground">{{
              connector.sourceSystem ?? '未知'
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
            当前读面按连接器汇总心跳与采样吞吐。该连接器覆盖的逐条采集标签清单与每标签最近采样时间需
            connector→tag 读面（缺口，见 follow-up
            <a
              href="https://github.com/Mang-X/Nerv-IIP/issues/947"
              target="_blank"
              rel="noopener"
              class="text-brand underline-offset-4 hover:underline"
              >#947</a
            >）；在其落地前可先到
            <RouterLink
              to="/equipment/telemetry/tags"
              class="text-brand underline-offset-4 hover:underline"
              >采集标签</RouterLink
            >
            按设备查看。
          </p>
        </div>
      </div>
    </div>
  </BusinessLayout>
</template>
