import {
  acknowledgeBusinessConsoleEquipmentAlarm,
  confirmBusinessConsoleOperation,
  listBusinessConsoleEquipmentAlarmsQueryOptions,
  listBusinessConsoleEquipmentAlarms,
  shelveBusinessConsoleEquipmentAlarm,
  unshelveBusinessConsoleEquipmentAlarm,
  type BusinessConsoleEquipmentAlarmListEnvelope,
  type BusinessConsoleTelemetryAlarmEventItem,
} from '@nerv-iip/api-client'
import {
  acquirePendingBusinessIntent,
  completePendingBusinessIntent,
  peekPendingBusinessIntent,
  useScopeBoundListResponse,
} from '@nerv-iip/business-core'
import { useAuthStore } from '@/stores/auth'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useQuery } from '@pinia/colada'
import { refDebounced } from '@vueuse/core'
import { computed, toValue, type MaybeRefOrGetter } from 'vue'
import { hasBusinessContext, refetchWithBusinessContext } from '../shared/businessContextBinding'
import { executeLifecycleAction } from '../shared/lifecycleAction'
import { listItems, toContextQuery } from './equipmentReadScope'

function requirePendingPayloadSnapshot<T extends object>(snapshot: unknown, operation: string): T {
  if (!snapshot || typeof snapshot !== 'object') {
    throw new Error(`${operation}缺少上次未完成提交的内容，请保留当前页面并人工核实。`)
  }
  return snapshot as T
}

/**
 * 一台设备的活动报警，**由服务端按设备过滤**：报警是全组织共用的大列表，
 * 拉一页再在前端按设备筛，排在后面的设备会被误报成「没有报警」。没选设备时不发请求。
 */
export function useEquipmentDeviceAlarms(deviceAssetId: MaybeRefOrGetter<string>) {
  const businessContext = useBusinessContextStore()
  const current = computed(() => toValue(deviceAssetId).trim())
  // 设备框允许直接敲编号，逐键发请求没有意义：停手 300ms 再查。
  const device = refDebounced(current, 300)
  const alarmsQuery = useQuery(() => ({
    ...listBusinessConsoleEquipmentAlarmsQueryOptions({
      query: { ...toContextQuery(businessContext), deviceAssetId: device.value },
    }),
    enabled: hasBusinessContext(businessContext) && Boolean(device.value),
  }))
  return {
    // 还在等去抖时，结果属于上一台设备，不能拿来当这台设备的报警。
    alarms: computed<BusinessConsoleTelemetryAlarmEventItem[]>(() =>
      device.value && device.value === current.value
        ? listItems<
            BusinessConsoleTelemetryAlarmEventItem,
            BusinessConsoleEquipmentAlarmListEnvelope
          >(alarmsQuery.data.value)
        : [],
    ),
    alarmsPending: alarmsQuery.isLoading,
  }
}

export function useBusinessEquipmentAlarms() {
  const auth = useAuthStore()
  const businessContext = useBusinessContextStore()
  const alarmsQuery = useQuery(() => ({
    ...listBusinessConsoleEquipmentAlarmsQueryOptions({
      query: toContextQuery(businessContext),
    }),
    enabled: hasBusinessContext(businessContext),
  }))
  async function readAlarm(
    alarmEventId: string,
    action: 'acknowledge' | 'shelve' | 'unshelve',
    evaluatedAtUtc?: string,
    idempotentReplay = false,
  ) {
    const response = await listBusinessConsoleEquipmentAlarms({
      query: {
        ...toContextQuery(businessContext),
        alarmEventId,
        skip: 0,
        take: 1,
      },
      throwOnError: false,
    })
    if (response.error !== undefined) throw response.error
    if (!response.data?.success) throw response.data ?? new Error('读取报警最新状态失败')
    const item = response.data.data?.items?.find(
      (candidate) => candidate.alarmEventId === alarmEventId,
    )
    return item
      ? {
          domain: 'iiot-alarm' as const,
          action,
          facts: {
            status: item.status,
            acknowledgedAtUtc: item.acknowledgedAtUtc,
            shelvedAtUtc: item.shelvedAtUtc,
            shelvedUntilUtc: item.shelvedUntilUtc,
            evaluatedAtUtc,
            idempotentReplay,
          },
        }
      : undefined
  }

  const alarmsScopeReady = computed(() => hasBusinessContext(businessContext))
  const alarmsResponse = useScopeBoundListResponse(
    () => alarmsQuery.data.value,
    () => `${businessContext.organizationId.trim()}:${businessContext.environmentId.trim()}`,
    alarmsScopeReady,
  )

  async function acknowledgeAlarm(alarmEventId: string, acknowledgedBy: string) {
    const scope = {
      principalId: auth.principal?.principalId ?? auth.sessionId ?? 'unrestored-session',
      organizationId: businessContext.organizationId,
      environmentId: businessContext.environmentId,
      operationType: 'iiot.alarm.acknowledge',
      payloadFingerprint: JSON.stringify({ alarmEventId, acknowledgedBy }),
    }
    const restored = peekPendingBusinessIntent(scope)
    const pending = acquirePendingBusinessIntent(
      scope,
      () => globalThis.crypto?.randomUUID?.() ?? `alarm-acknowledge-${Date.now()}-${Math.random()}`,
      { acknowledgedAtUtc: new Date().toISOString() },
    )
    const stablePayload = requirePendingPayloadSnapshot<{ acknowledgedAtUtc: string }>(
      pending.payloadSnapshot,
      '报警确认',
    )
    const result = await completePendingBusinessIntent(scope, async () => {
      const envelope = await executeLifecycleAction({
        readLatest: () => readAlarm(alarmEventId, 'acknowledge', undefined, Boolean(restored)),
        command: () =>
          acknowledgeBusinessConsoleEquipmentAlarm({
            path: { alarmEventId },
            body: {
              ...toContextQuery(businessContext),
              acknowledgedAtUtc: stablePayload.acknowledgedAtUtc,
              acknowledgedBy,
              idempotencyKey: pending.idempotencyKey,
            },
            throwOnError: false,
          }),
      })
      if (!envelope) throw new Error('报警确认未返回业务信封')
      await confirmBusinessConsoleOperation(envelope, {
        expectedOperationType: 'iiot.alarm.acknowledge',
        expectedIdempotencyKey: pending.idempotencyKey,
        expectedResourceId: alarmEventId,
      })
      return envelope
    })
    await refetchWithBusinessContext(businessContext, alarmsQuery)
    return result
  }

  async function shelveAlarm(
    alarmEventId: string,
    shelvedBy: string,
    durationMinutes = 30,
    reason?: string,
    options?: {
      attempt?: 'initial' | 'retry'
      shelvedAtUtc?: string
      idempotencyKey?: string
    },
  ) {
    // Freeze the shelve instant so a retried batch reuses the same window; the backend
    // derives the shelve window from shelvedAtUtc + idempotencyKey (first-write-wins),
    // so a stable key makes re-submitting the same batch a no-op instead of extending it.
    const scope = {
      principalId: auth.principal?.principalId ?? auth.sessionId ?? 'unrestored-session',
      organizationId: businessContext.organizationId,
      environmentId: businessContext.environmentId,
      operationType: 'iiot.alarm.shelve',
      payloadFingerprint: JSON.stringify({
        alarmEventId,
        shelvedBy,
        durationMinutes,
        reason: reason?.trim() ?? '',
      }),
    }
    const restored = peekPendingBusinessIntent(scope)
    const pending = acquirePendingBusinessIntent(
      scope,
      () =>
        options?.idempotencyKey ??
        globalThis.crypto?.randomUUID?.() ??
        `alarm-shelve-${Date.now()}-${Math.random()}`,
      { shelvedAtUtc: options?.shelvedAtUtc ?? new Date().toISOString() },
    )
    const stablePayload = requirePendingPayloadSnapshot<{ shelvedAtUtc: string }>(
      pending.payloadSnapshot,
      '报警搁置',
    )
    const result = await completePendingBusinessIntent(scope, async () => {
      const envelope = await executeLifecycleAction({
        readLatest: () =>
          readAlarm(
            alarmEventId,
            'shelve',
            restored ? stablePayload.shelvedAtUtc : undefined,
            Boolean(restored),
          ),
        command: () =>
          shelveBusinessConsoleEquipmentAlarm({
            path: { alarmEventId },
            body: {
              ...toContextQuery(businessContext),
              durationMinutes,
              idempotencyKey: pending.idempotencyKey,
              reason,
              shelvedAtUtc: stablePayload.shelvedAtUtc,
              shelvedBy,
            },
            throwOnError: false,
          }),
      })
      if (!envelope) throw new Error('报警搁置未返回业务信封')
      await confirmBusinessConsoleOperation(envelope, {
        expectedOperationType: 'iiot.alarm.shelve',
        expectedIdempotencyKey: pending.idempotencyKey,
        expectedResourceId: alarmEventId,
      })
      return envelope
    })
    await refetchWithBusinessContext(businessContext, alarmsQuery)
    return result
  }

  async function unshelveAlarm(alarmEventId: string) {
    const scope = {
      principalId: auth.principal?.principalId ?? auth.sessionId ?? 'unrestored-session',
      organizationId: businessContext.organizationId,
      environmentId: businessContext.environmentId,
      operationType: 'iiot.alarm.unshelve',
      payloadFingerprint: JSON.stringify({ alarmEventId }),
    }
    const restored = peekPendingBusinessIntent(scope)
    const pending = acquirePendingBusinessIntent(
      scope,
      () => globalThis.crypto?.randomUUID?.() ?? `alarm-unshelve-${Date.now()}-${Math.random()}`,
      { unshelvedAtUtc: new Date().toISOString() },
    )
    const stablePayload = requirePendingPayloadSnapshot<{ unshelvedAtUtc: string }>(
      pending.payloadSnapshot,
      '报警取消搁置',
    )
    const result = await completePendingBusinessIntent(scope, async () => {
      const envelope = await executeLifecycleAction({
        readLatest: () => readAlarm(alarmEventId, 'unshelve', undefined, Boolean(restored)),
        command: () =>
          unshelveBusinessConsoleEquipmentAlarm({
            path: { alarmEventId },
            body: {
              ...toContextQuery(businessContext),
              idempotencyKey: pending.idempotencyKey,
              unshelvedAtUtc: stablePayload.unshelvedAtUtc,
            },
            throwOnError: false,
          }),
      })
      if (!envelope) throw new Error('报警取消搁置未返回业务信封')
      await confirmBusinessConsoleOperation(envelope, {
        expectedOperationType: 'iiot.alarm.unshelve',
        expectedIdempotencyKey: pending.idempotencyKey,
        expectedResourceId: alarmEventId,
      })
      return envelope
    })
    await refetchWithBusinessContext(businessContext, alarmsQuery)
    return result
  }

  return {
    acknowledgeAlarm,
    alarms: computed<BusinessConsoleTelemetryAlarmEventItem[]>(() =>
      listItems<BusinessConsoleTelemetryAlarmEventItem, BusinessConsoleEquipmentAlarmListEnvelope>(
        alarmsResponse.value,
      ),
    ),
    alarmsError: alarmsQuery.error,
    alarmsPending: alarmsQuery.isLoading,
    refreshAlarms: () => refetchWithBusinessContext(businessContext, alarmsQuery),
    shelveAlarm,
    unshelveAlarm,
  }
}
