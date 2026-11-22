import {
  mesWorkOrderReleaseBlocker,
  mesWorkOrderRetroactiveReleaseNotice,
} from '@/composables/mes/workOrderRelease'
import { notifyOperationFailure, notifySuccess } from '@/utils/notify'
import { computed, shallowRef } from 'vue'
import type { Ref } from 'vue'
import type { useMesWorkOrders } from '@/composables/mes'
import type { BusinessConsoleMesWorkOrderItem } from '@nerv-iip/api-client'

type Row = BusinessConsoleMesWorkOrderItem

export function useWorkOrderReleaseDialog(
  {
    filters,
    workOrders,
    releaseWorkOrder,
    releaseWorkOrderPending,
    readWorkOrderForRelease,
    workOrderManageScopePending,
    workOrderManageScopeReady,
    workOrderManageScopeMessage,
    refreshWorkOrders,
  }: Pick<
    ReturnType<typeof useMesWorkOrders>,
    | 'filters'
    | 'workOrders'
    | 'releaseWorkOrder'
    | 'releaseWorkOrderPending'
    | 'readWorkOrderForRelease'
    | 'workOrderManageScopePending'
    | 'workOrderManageScopeReady'
    | 'workOrderManageScopeMessage'
    | 'refreshWorkOrders'
  >,
  canManageWorkOrders: Readonly<Ref<boolean>>,
  refreshOperationTasks: () => Promise<unknown>,
) {
  type ReleaseIntent = {
    workOrderId: string
    workOrderLabel: string
  }

  const releaseIntent = shallowRef<ReleaseIntent | null>(null)
  const releaseWarningsConfirmed = shallowRef(false)
  const releasePreflightPending = shallowRef(false)
  const releaseDialogOpen = computed({
    get: () => releaseIntent.value !== null,
    set: (open: boolean) => {
      if (!open && !releaseWorkOrderPending.value) clearReleaseIntent()
    },
  })
  const releaseIntentOrder = computed(() => {
    const intent = releaseIntent.value
    return intent
      ? (workOrders.value.find((order) => order.workOrderId === intent.workOrderId) ?? null)
      : null
  })
  const releaseValidationMessage = computed(() => {
    if (!releaseIntent.value) return ''
    if (!releaseIntentOrder.value) return '工单已不在当前主体授权工单范围，请刷新后重试。'
    return releaseBlocker(releaseIntentOrder.value) ?? ''
  })
  const releaseRetroactiveNotice = computed(() =>
    releaseIntentOrder.value
      ? mesWorkOrderRetroactiveReleaseNotice(releaseIntentOrder.value)
      : null,
  )
  const canSubmitRelease = computed(
    () =>
      releaseIntent.value !== null &&
      releaseWarningsConfirmed.value &&
      !releaseValidationMessage.value &&
      !releaseWorkOrderPending.value,
  )

  function releaseBlocker(order: Parameters<typeof mesWorkOrderReleaseBlocker>[0]) {
    if (!canManageWorkOrders.value) return '没有工单下达权限'
    if (workOrderManageScopePending.value) return '正在确认主体授权工单范围'
    if (!workOrderManageScopeReady.value) {
      return workOrderManageScopeMessage.value || '主体授权工单范围未就绪'
    }
    return mesWorkOrderReleaseBlocker(order)
  }

  async function openReleaseDialog(order: Row) {
    if (releaseBlocker(order) || !order.workOrderId) return
    releasePreflightPending.value = true
    try {
      const latest = await readWorkOrderForRelease(order.workOrderId)
      const blocker = releaseBlocker(latest)
      if (blocker) throw new Error(blocker)
      releaseIntent.value = {
        workOrderId: order.workOrderId,
        workOrderLabel: order.workOrderNo || order.workOrderId,
      }
      releaseWarningsConfirmed.value = false
    } catch (error) {
      notifyOperationFailure(
        '工单下达前置检查失败',
        error,
        '未能在当前管理范围内确认工单下达条件，请检查授权范围和就绪状态后重试。',
      )
    } finally {
      releasePreflightPending.value = false
    }
  }

  function clearReleaseIntent() {
    releaseIntent.value = null
    releaseWarningsConfirmed.value = false
  }

  async function submitReleaseWorkOrder() {
    const intent = releaseIntent.value
    const order = releaseIntentOrder.value
    if (!intent || !order || !canSubmitRelease.value) return

    try {
      const response = await releaseWorkOrder(intent.workOrderId, {
        organizationId: filters.organizationId.trim(),
        environmentId: filters.environmentId.trim(),
        confirmWarnings: true,
      })
      if (response?.data?.accepted !== true) {
        throw new Error('工单下达结果未确认，请刷新列表核实后再重试。')
      }

      // 服务端已确认受理后不再保留可重试入口，避免刷新失败导致重复提交；两个权威读面都显式刷新。
      clearReleaseIntent()
      const refreshResults = await Promise.allSettled([
        refreshWorkOrders(),
        refreshOperationTasks(),
      ])
      notifySuccess(`工单 ${intent.workOrderLabel} 已下达。`)
      const refreshFailure = refreshResults.find(
        (result): result is PromiseRejectedResult => result.status === 'rejected',
      )
      if (refreshFailure) {
        notifyOperationFailure(
          '工单已下达，但状态刷新失败',
          refreshFailure.reason,
          '工单已下达，但最新状态刷新失败，请手动刷新列表。',
        )
      }
    } catch (error) {
      notifyOperationFailure('工单下达失败', error, '工单下达失败，请检查就绪条件后重试。')
    }
  }

  return {
    releaseIntent,
    releaseWarningsConfirmed,
    releasePreflightPending,
    clearReleaseIntent,
    releaseIntentOrder,
    releaseDialogOpen,
    releaseValidationMessage,
    releaseRetroactiveNotice,
    canSubmitRelease,
    releaseBlocker,
    openReleaseDialog,
    submitReleaseWorkOrder,
  }
}
