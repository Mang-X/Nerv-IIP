<script setup lang="ts">
import {
  isConnectorFault,
  isConnectorOffline,
  useBusinessTelemetryConnectors,
} from '@/composables/useBusinessTelemetry'
import ConnectorHealthCard from '@/components/equipment/ConnectorHealthCard.vue'
import { notifyError } from '@/utils/notify'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import type { NvMetricSegment } from '@nerv-iip/ui'
import { NvButton, NvMetricRing, NvPageHeader } from '@nerv-iip/ui'
import { HashIcon, RefreshCwIcon } from '@lucide/vue'
import { computed, reactive, ref, watch } from 'vue'
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

// 一次性 toast，且按错误"进入"边界去重：10 秒自动轮询持续失败时每次重试都会写入新的 error 对象，
// 这里只在从"无错误"跨入"有错误"时通知一次，恢复成功后重置，避免长期故障下每 10 秒轰炸用户。
const errorNotified = ref(false)
watch(connectorsError, (error) => {
  if (error && !errorNotified.value) {
    notifyError(error, '采集健康加载失败，请稍后重试。')
    errorNotified.value = true
  } else if (!error) {
    errorNotified.value = false
  }
})

const onlineCount = computed(
  () =>
    connectors.value.filter(
      (connector) => connector.status === 'current' && connector.connection?.status === 'alive',
    ).length,
)
const offlineCount = computed(
  () => connectors.value.filter((c) => isConnectorOffline(c.status, c.staleReason)).length,
)
const faultCount = computed(
  () => connectors.value.filter((c) => isConnectorFault(c.status, c.staleReason)).length,
)

// 采集状态构成：四段互斥且相加等于当前已加载的连接器数（Ring 的构成前提）。分母用本页已加载的
// 连接器数而非 connectorsTotal —— 后者可能包含尚未加载的分页外记录，混用会让各段占比失真。
const otherCount = computed(() =>
  Math.max(0, connectors.value.length - onlineCount.value - offlineCount.value - faultCount.value),
)
const connectorSegments = computed<NvMetricSegment[]>(() => [
  { key: 'online', label: '在线', value: onlineCount.value, tone: 'success' },
  { key: 'offline', label: '断线', value: offlineCount.value, tone: 'danger' },
  { key: 'fault', label: '异常停止', value: faultCount.value, tone: 'warning' },
  { key: 'other', label: '待采集', value: otherCount.value, tone: 'neutral' },
])

const expanded = reactive(new Set<string>())
function rowKey(connectorId?: string | null, connectorName?: string | null) {
  return connectorId ?? connectorName ?? '未知连接器'
}
function toggle(key: string) {
  if (expanded.has(key)) expanded.delete(key)
  else expanded.add(key)
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

    <NvMetricRing
      label="采集状态构成"
      :value="connectors.length"
      center-caption="个连接器"
      :segments="connectorSegments"
      class="max-w-md"
    />

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
      暂无现场采集连接。完成现场采集配置并开始上报后，设备数据会出现在这里。
    </div>

    <div v-else class="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
      <ConnectorHealthCard
        v-for="connector in connectors"
        :key="rowKey(connector.connectorId, connector.connectorName)"
        :connector="connector"
        :sample-rate="sampleRateByConnector[connector.connectorId ?? ''] ?? null"
        :expanded="expanded.has(rowKey(connector.connectorId, connector.connectorName))"
        @toggle="toggle(rowKey(connector.connectorId, connector.connectorName))"
      />
    </div>
  </BusinessLayout>
</template>
