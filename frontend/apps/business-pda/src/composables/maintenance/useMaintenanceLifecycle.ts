import {
  confirmBusinessConsoleOperation,
  transitionBusinessConsoleMaintenanceWorkOrder,
  type BusinessConsoleTransitionMaintenanceWorkOrderRequest,
} from '@nerv-iip/api-client'
import { computed, reactive, ref, watch } from 'vue'
import { describeRequestError } from '@/api/request-timeout'
import type { useMaintenanceSelfWorkOrderDetail } from './useMaintenanceSelfWorkOrderDetail'

const actions = ['accept', 'start', 'complete'] as const
type Action = (typeof actions)[number]

export function useMaintenanceLifecycle(
  detail: ReturnType<typeof useMaintenanceSelfWorkOrderDetail>,
) {
  const action = ref<Action>()
  const busy = ref(false)
  const message = ref('')
  const pending = ref<BusinessConsoleTransitionMaintenanceWorkOrderRequest>()
  const form = reactive({ reason: '', result: '', downtimeReasonCode: '', downtime: '', labor: '' })
  const available = computed(() =>
    actions.filter((value) => detail.authoritativeWorkOrder.value?.allowedActions.includes(value)),
  )

  function reset() {
    action.value = undefined
    pending.value = undefined
    Object.assign(form, { reason: '', result: '', downtimeReasonCode: '', downtime: '', labor: '' })
  }

  watch(
    [detail.workOrderId, detail.scopeKey],
    () => {
      reset()
      message.value = ''
    },
    { flush: 'sync' },
  )

  function select(value: Action) {
    if (busy.value || pending.value || !available.value.includes(value)) return
    reset()
    action.value = value
    message.value = ''
  }

  async function submit() {
    const selected = action.value
    const shown = detail.authoritativeWorkOrder.value
    if (!selected || busy.value || (!shown && !pending.value)) return
    const workOrderId = detail.workOrderId.value
    const scopeKey = detail.scopeKey.value
    const stillCurrent = () =>
      workOrderId === detail.workOrderId.value && scopeKey === detail.scopeKey.value
    const validMinutes = (value: string) =>
      /^\d+$/.test(value) && Number.isSafeInteger(Number(value)) && Number(value) <= 2147483647
    if (
      !pending.value &&
      (!form.reason.trim() ||
        (selected === 'complete' &&
          (!form.result.trim() ||
            !form.downtimeReasonCode.trim() ||
            !validMinutes(form.downtime) ||
            !validMinutes(form.labor))))
    ) {
      message.value =
        selected === 'complete'
          ? '请填写操作原因、处理结果、停机原因和有效的分钟数。'
          : '请填写操作原因。'
      return
    }

    busy.value = true
    message.value = ''
    try {
      await detail.refresh()
      if (!stillCurrent()) return
      const latest = detail.authoritativeWorkOrder.value
      if (!latest) {
        message.value = '工单详情读取失败，请重试。'
        return
      }
      if (!pending.value) {
        if (latest.version !== shown?.version || !latest.allowedActions.includes(selected)) {
          reset()
          message.value = '工单状态已更新，请重新选择操作。'
          return
        }
        pending.value = {
          ...detail.queryScope(),
          action: selected,
          expectedVersion: latest.version,
          idempotencyKey: globalThis.crypto.randomUUID(),
          reason: form.reason.trim(),
          ...(selected === 'complete'
            ? {
                result: form.result.trim(),
                downtimeReasonCode: form.downtimeReasonCode.trim(),
                downtimeMinutes: Number(form.downtime),
                actualLaborMinutes: Number(form.labor),
              }
            : {}),
        }
      }
      const body = pending.value
      const response = await transitionBusinessConsoleMaintenanceWorkOrder({
        path: { workOrderId },
        body,
        throwOnError: false,
      })
      if (!stillCurrent()) return
      if (response.response?.status === 409) {
        reset()
        await detail.refresh()
        if (stillCurrent()) message.value = '工单状态已更新，请重新选择操作。'
        return
      }
      if (response.error !== undefined)
        throw { status: response.response?.status, data: response.error }
      if (!response.data) throw new Error('暂未确认操作结果，请重试。')
      await confirmBusinessConsoleOperation(response.data, {
        expectedOperationType: `maintenance.work-order.${selected}`,
        expectedIdempotencyKey: body.idempotencyKey,
        expectedResourceId: workOrderId,
      })
      await detail.refresh()
      if (!stillCurrent()) return
      const confirmedOrder = detail.authoritativeWorkOrder.value
      const changed = confirmedOrder?.lifecycle.some(
        (entry) =>
          entry.action?.toLowerCase() === selected &&
          entry.resultingVersion === (body.expectedVersion ?? -1) + 1,
      )
      if (!changed) {
        message.value = '暂未确认操作结果，请重试。'
        return
      }
      reset()
      message.value = '操作已确认。'
    } catch (error) {
      if (!stillCurrent()) return
      const info = describeRequestError(error, '暂未确认操作结果，请重试。')
      if (info.status && info.status < 500) pending.value = undefined
      message.value = info.message
    } finally {
      busy.value = false
    }
  }

  return { action, available, busy, form, message, pending, select, submit }
}
