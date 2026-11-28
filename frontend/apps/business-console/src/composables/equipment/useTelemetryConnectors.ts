import {
  getBusinessConsoleTelemetryConnectorTagCoverageQueryOptions,
  listBusinessConsoleTelemetryConnectorCollectionHealthQueryOptions,
  type BusinessConsoleConnectorCollectionHealthListEnvelope,
  type BusinessConsoleConnectorCollectionHealthListItem,
  type BusinessConsoleConnectorTagCoverageEnvelope,
  type BusinessConsoleConnectorTagCoverageResponse,
} from '@nerv-iip/api-client'
import { useQuery } from '@pinia/colada'
import { computed, ref, watch, type Ref } from 'vue'
import { useBusinessContextStore } from '@/stores/businessContext'
import { hasBusinessContext, refetchWithBusinessContext } from '../shared/businessContextBinding'
import { listItems, listTotal, toContextQuery, unwrapData } from './telemetryShared'

/**
 * 采集连接器状态墙轮询周期。读面在心跳超窗后派生 offline，本页每个周期重取以在「1 个轮询周期内」反映。
 * 用官方 auto-refetch 表达，不手写 setInterval。
 */
export const CONNECTOR_HEALTH_POLL_INTERVAL_MS = 5_000

/**
 * stale 有两种成因，展示口径不同：`offline`=心跳超窗停报（真断线，显示断线时长并红色置顶，时长基于冻结的
 * 最后心跳单调增长）；`fault`=仍在心跳但连接器自报终态停止（异常停止，成因未必是断连，不显示断线时长）。
 */
export function connectorHealthStatusLabel(status?: string | null, staleReason?: string | null) {
  if (status === 'current') return '在线'
  if (status === 'stale') return staleReason === 'fault' ? '异常停止' : '断线'
  // heartbeating/running but no sampling evidence yet (no mapping / nothing collected) — not "collecting"
  return '待采集'
}

/** 只有 offline（心跳超窗）才是真断线，显示断线时长。 */
export function isConnectorOffline(status?: string | null, staleReason?: string | null) {
  return status === 'stale' && staleReason === 'offline'
}

/** fault=连接器自报异常停止（成因未必是断连），单列区分，不显示断线时长。 */
export function isConnectorFault(status?: string | null, staleReason?: string | null) {
  return status === 'stale' && staleReason === 'fault'
}

/** 采集连接器的协议类型来自采集健康的 sourceSystem（opcua/modbus/mqtt）。 */
export function connectorSourceSystemLabel(sourceSystem?: string | null) {
  if (!sourceSystem) return '未知类型'
  const labels: Record<string, string> = {
    opcua: 'OPC UA',
    modbus: 'Modbus TCP',
    mqtt: 'MQTT',
  }
  return labels[sourceSystem.toLowerCase()] ?? sourceSystem
}

export interface ConnectorRateSample {
  counterEpoch?: string | null
  receivedCount?: number | null
  metricsReportedAtUtc?: string | null
}

/**
 * 采样吞吐速率（samples/s）= 同一 counter epoch 内连续两次上报的 receivedCount 差 ÷ 上报时间差。
 * epoch 变更（计数器重置）、计数回退、时间未推进或缺首个样本时返回 null（页面显示「计算中」而非伪造速率）。
 */
export function computeConnectorSampleRate(
  previous: ConnectorRateSample | undefined,
  current: ConnectorRateSample,
): number | null {
  if (!previous) return null
  if ((previous.counterEpoch ?? null) !== (current.counterEpoch ?? null)) return null
  const prevReceived = previous.receivedCount
  const curReceived = current.receivedCount
  if (prevReceived == null || curReceived == null || curReceived < prevReceived) return null
  const prevAt = Date.parse(previous.metricsReportedAtUtc ?? '')
  const curAt = Date.parse(current.metricsReportedAtUtc ?? '')
  if (!Number.isFinite(prevAt) || !Number.isFinite(curAt) || curAt <= prevAt) return null
  return (curReceived - prevReceived) / ((curAt - prevAt) / 1000)
}

export function formatSampleRate(rate: number | null | undefined) {
  if (rate === null || rate === undefined) return '计算中…'
  if (rate < 1) return `${rate.toFixed(2)} /秒`
  return `${rate.toLocaleString(undefined, { maximumFractionDigits: 1 })} /秒`
}

/**
 * 采集连接器健康墙：消费连接器采集健康列表 facade，按周期轮询。断线/异常连接器由后端排序置顶。
 * 业务上下文为空时不发请求（空态由页面处理）。采样速率由相邻两次轮询在前端按 epoch 差值计算。
 */
export function useBusinessTelemetryConnectors() {
  const businessContext = useBusinessContextStore()
  const connectorsQuery = useQuery(() => ({
    ...listBusinessConsoleTelemetryConnectorCollectionHealthQueryOptions({
      query: toContextQuery(businessContext),
    }),
    enabled: hasBusinessContext(businessContext),
    autoRefetch: () => CONNECTOR_HEALTH_POLL_INTERVAL_MS,
  }))

  const connectors = computed<BusinessConsoleConnectorCollectionHealthListItem[]>(() =>
    listItems<BusinessConsoleConnectorCollectionHealthListItem>(
      connectorsQuery.data.value as
        | BusinessConsoleConnectorCollectionHealthListEnvelope
        | undefined,
    ),
  )

  const previousSamples = new Map<string, ConnectorRateSample>()
  const sampleRateByConnector = ref<Record<string, number | null>>({})
  watch(connectors, (list) => {
    const next: Record<string, number | null> = {}
    for (const connector of list) {
      const id = connector.connectorId ?? ''
      if (!id) continue
      const sample: ConnectorRateSample = {
        counterEpoch: connector.counterEpoch,
        receivedCount: connector.receivedCount,
        metricsReportedAtUtc: connector.metricsReportedAtUtc,
      }
      next[id] = computeConnectorSampleRate(previousSamples.get(id), sample)
      previousSamples.set(id, sample)
    }
    sampleRateByConnector.value = next
  })

  return {
    connectors,
    connectorsError: connectorsQuery.error,
    connectorsPending: connectorsQuery.isLoading,
    connectorsTotal: computed(() =>
      listTotal(
        connectorsQuery.data.value as
          | BusinessConsoleConnectorCollectionHealthListEnvelope
          | undefined,
      ),
    ),
    refreshConnectors: () => refetchWithBusinessContext(businessContext, connectorsQuery),
    sampleRateByConnector,
  }
}

/**
 * 连接器卡片展开后的配置标签覆盖。调用方仅在明细挂载时创建该查询；空连接器编号或空业务
 * 上下文不会发请求。覆盖事实来自连接器配置清单，不从采样、设备控制绑定或连接器名称推断。
 */
export function useBusinessTelemetryConnectorCoverage(collectionConnectorId: Ref<string>) {
  const businessContext = useBusinessContextStore()
  const connectorId = computed(() => collectionConnectorId.value.trim())
  const coverageEnabled = computed(
    () => hasBusinessContext(businessContext) && connectorId.value.length > 0,
  )
  const coverageQuery = useQuery(() => ({
    ...getBusinessConsoleTelemetryConnectorTagCoverageQueryOptions({
      path: { connectorId: connectorId.value },
      query: toContextQuery(businessContext),
    }),
    enabled: coverageEnabled.value,
  }))

  return {
    coverage: computed<BusinessConsoleConnectorTagCoverageResponse | undefined>(() =>
      unwrapData<BusinessConsoleConnectorTagCoverageResponse>(
        coverageQuery.data.value as BusinessConsoleConnectorTagCoverageEnvelope | undefined,
      ),
    ),
    coverageError: coverageQuery.error,
    coveragePending: coverageQuery.isLoading,
    refreshCoverage: () => (coverageEnabled.value ? coverageQuery.refetch() : Promise.resolve()),
  }
}
