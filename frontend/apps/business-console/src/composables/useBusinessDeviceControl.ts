import {
  createBusinessConsoleTelemetryDeviceControlCommandMutationOptions,
  getBusinessConsoleTelemetryDeviceControlCommandQueryOptions,
  listBusinessConsoleTelemetryDeviceControlCommandsQueryOptions,
  type BusinessConsoleTelemetryDeviceControlCommandDetail,
  type BusinessConsoleTelemetryDeviceControlCommandDetailEnvelope,
  type BusinessConsoleTelemetryDeviceControlCommandEnvelope,
  type BusinessConsoleTelemetryDeviceControlCommandListEnvelope,
  type BusinessConsoleTelemetryDeviceControlCommandListItem,
} from '@nerv-iip/api-client'
import { useMutation, useQuery, useQueryCache } from '@pinia/colada'
import { computed, onScopeDispose, reactive, ref, watch, type Ref } from 'vue'
import { useBusinessContextStore } from '@/stores/businessContext'
import { hasBusinessContext } from './businessContextBinding'

const POLL_INTERVAL_MS = 2500

export type DeviceControlCommandType = 'write-tag' | 'start-stop' | 'parameter-set'

export interface DeviceControlHistoryFilters {
  deviceAssetId: string
  status: string
  skip: number
  take: number
}

export interface DispatchDeviceControlInput {
  commandType: DeviceControlCommandType
  tagKey?: string
  value?: string
  parameters?: Record<string, string>
  reason: string
}

const TERMINAL_STATUSES = new Set(['completed', 'failed', 'rejected', 'abandoned'])

export function isTerminalDeviceControlStatus(status: string | null | undefined): boolean {
  return status ? TERMINAL_STATUSES.has(status.trim().toLowerCase()) : false
}

export function deviceControlCommandTypeLabel(value?: string | null): string {
  const labels: Record<string, string> = {
    'write-tag': '写值',
    'start-stop': '启停',
    'parameter-set': '参数下发',
  }
  return value ? (labels[value.toLowerCase()] ?? value) : '未知命令'
}

export function deviceControlStatusLabel(value?: string | null): string {
  const labels: Record<string, string> = {
    queued: '排队中',
    'approval-pending': '待审批',
    dispatched: '执行中',
    completed: '成功',
    failed: '失败',
    rejected: '已驳回',
    abandoned: '已放弃',
  }
  return value ? (labels[value.toLowerCase()] ?? value) : '未知'
}

export function deviceControlStatusTone(
  value?: string | null,
): 'success' | 'danger' | 'warning' | 'neutral' {
  const status = value?.trim().toLowerCase()
  if (status === 'completed') return 'success'
  if (status === 'failed' || status === 'rejected' || status === 'abandoned') return 'danger'
  if (status === 'approval-pending' || status === 'queued' || status === 'dispatched')
    return 'warning'
  return 'neutral'
}

export function deviceControlApprovalLabel(value?: string | null): string {
  const labels: Record<string, string> = {
    pending: '待批',
    approved: '已批准',
    rejected: '已驳回',
    'not-required': '无需审批',
  }
  return value ? (labels[value.toLowerCase()] ?? value) : '未知'
}

// 写操作幂等键：避免同一次下发在网络抖动/重试时重复建单。浏览器原生 UUID，测试环境亦可用。
function makeDeviceControlKey(prefix: string): string {
  const c = globalThis.crypto
  if (c && typeof c.randomUUID === 'function') return `${prefix}-${c.randomUUID()}`
  return `${prefix}-${Date.now()}-${Math.round(Math.random() * 1e9)}`
}

function toContextQuery(businessContext: ReturnType<typeof useBusinessContextStore>) {
  return {
    organizationId: businessContext.organizationId,
    environmentId: businessContext.environmentId,
  }
}

function unwrapData<TData, TEnvelope extends { success?: boolean; data?: TData | null }>(
  envelope: TEnvelope | undefined,
) {
  return envelope?.success ? (envelope.data ?? undefined) : undefined
}

/**
 * 设备控制命令域组合式：承载「控制命令历史」倒序分页读面、命令下发（连接器主机/实例由后端按设备绑定解析，
 * 前端不传）、以及提交后按 commandId 轮询命令结果（待批 → 执行中 → 成功/失败）。
 */
export function useBusinessDeviceControlCommands(deviceAssetId: Ref<string>) {
  const businessContext = useBusinessContextStore()
  const queryCache = useQueryCache()

  const historyFilters = reactive<DeviceControlHistoryFilters>({
    deviceAssetId: deviceAssetId.value,
    status: '',
    skip: 0,
    take: 20,
  })
  watch(deviceAssetId, (value) => {
    historyFilters.deviceAssetId = value
  })

  const historyEnabled = computed(
    () => hasBusinessContext(businessContext) && historyFilters.deviceAssetId.trim().length > 0,
  )
  const historyQuery = useQuery(() => ({
    ...listBusinessConsoleTelemetryDeviceControlCommandsQueryOptions({
      query: {
        ...toContextQuery(businessContext),
        deviceAssetId: historyFilters.deviceAssetId,
        status: historyFilters.status.trim() ? historyFilters.status.trim() : undefined,
        skip: historyFilters.skip,
        take: historyFilters.take,
      },
    }),
    enabled: historyEnabled.value,
  }))
  const historyEnvelope = computed(
    () =>
      historyQuery.data.value as
        | BusinessConsoleTelemetryDeviceControlCommandListEnvelope
        | undefined,
  )

  function invalidateHistory() {
    queryCache.invalidateQueries({
      predicate: (entry) => {
        const keyParts = Array.isArray(entry.key) ? entry.key : [entry.key]
        return keyParts.some(
          (part) =>
            typeof part === 'object' &&
            part !== null &&
            '_id' in part &&
            (part._id === 'listBusinessConsoleTelemetryDeviceControlCommands' ||
              part._id === 'getBusinessConsoleTelemetryDeviceControlCommand'),
        )
      },
    })
  }

  const dispatchMutation = useMutation({
    ...createBusinessConsoleTelemetryDeviceControlCommandMutationOptions(),
    onSuccess() {
      invalidateHistory()
    },
  })

  async function dispatchCommand(input: DispatchDeviceControlInput): Promise<string | undefined> {
    const response = await dispatchMutation.mutateAsync({
      body: {
        ...toContextQuery(businessContext),
        deviceAssetId: deviceAssetId.value,
        commandType: input.commandType,
        tagKey: input.tagKey ?? '',
        value: input.value ?? '',
        parameters: input.parameters ?? {},
        reason: input.reason,
        idempotencyKey: makeDeviceControlKey('devctl'),
        correlationId: makeDeviceControlKey('corr'),
      },
    })
    const envelope = response as BusinessConsoleTelemetryDeviceControlCommandEnvelope | undefined
    return envelope?.success ? (envelope.data?.operationTaskId ?? undefined) : undefined
  }

  // --- 命令结果轮询（提交后跟踪单命令状态直至终态） ---
  const trackedCommandId = ref<string | null>(null)
  const resultQuery = useQuery(() => ({
    ...getBusinessConsoleTelemetryDeviceControlCommandQueryOptions({
      path: { commandId: trackedCommandId.value ?? '' },
      query: {
        ...toContextQuery(businessContext),
        deviceAssetId: deviceAssetId.value,
      },
    }),
    enabled: Boolean(trackedCommandId.value) && hasBusinessContext(businessContext),
  }))
  const trackedResult = computed<BusinessConsoleTelemetryDeviceControlCommandDetail | undefined>(
    () =>
      unwrapData<
        BusinessConsoleTelemetryDeviceControlCommandDetail,
        BusinessConsoleTelemetryDeviceControlCommandDetailEnvelope
      >(
        resultQuery.data.value as
          | BusinessConsoleTelemetryDeviceControlCommandDetailEnvelope
          | undefined,
      ),
  )

  let pollTimer: ReturnType<typeof setInterval> | null = null
  function stopPolling() {
    if (pollTimer) {
      clearInterval(pollTimer)
      pollTimer = null
    }
  }
  function startTracking(commandId: string) {
    trackedCommandId.value = commandId
    stopPolling()
    pollTimer = setInterval(() => {
      if (!trackedCommandId.value) {
        stopPolling()
        return
      }
      void resultQuery.refetch()
    }, POLL_INTERVAL_MS)
  }
  function resetTracking() {
    stopPolling()
    trackedCommandId.value = null
  }
  // 命中终态即停止轮询，避免无谓刷新。
  watch(
    () => trackedResult.value?.status,
    (status) => {
      if (isTerminalDeviceControlStatus(status)) stopPolling()
    },
  )
  onScopeDispose(stopPolling)

  return {
    // history
    commands: computed<BusinessConsoleTelemetryDeviceControlCommandListItem[]>(
      () => historyEnvelope.value?.data?.items ?? [],
    ),
    commandsTotal: computed(() => historyEnvelope.value?.data?.total ?? 0),
    commandsError: historyQuery.error,
    commandsPending: historyQuery.isLoading,
    historyFilters,
    refreshCommands: () => (historyEnabled.value ? historyQuery.refetch() : Promise.resolve()),
    // dispatch
    dispatchCommand,
    dispatchError: dispatchMutation.error,
    dispatchPending: dispatchMutation.isLoading,
    // result tracking
    trackedCommandId,
    trackedResult,
    trackedPending: resultQuery.isLoading,
    startTracking,
    resetTracking,
  }
}
