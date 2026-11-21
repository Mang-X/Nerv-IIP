import type { BusinessConsoleSchedulingPlanSummaryResponse } from '@nerv-iip/api-client'
import type { useBusinessScheduling } from '@/composables/scheduling/useBusinessScheduling'
import { describeScheduleInvalidationReason } from '@/composables/scheduling/useScheduleInvalidation'
import { schedulingPlanTerminalReleaseReason } from '@/utils/schedulingPlanPresentation'
import { notifyOperationFailure, notifySuccess } from '@/utils/notify'
import { shallowRef } from 'vue'
import type { Ref } from 'vue'

export function useSchedulingPlanPublication(
  {
    releasePlan,
    revokePlan,
  }: Pick<ReturnType<typeof useBusinessScheduling>, 'releasePlan' | 'revokePlan'>,
  canPublish: Readonly<Ref<boolean>>,
) {
  async function publish(plan: BusinessConsoleSchedulingPlanSummaryResponse | undefined) {
    if (!plan?.planId || !canRelease(plan)) return

    try {
      await releasePlan(plan.planId)
      notifySuccess('排程方案已发布')
    } catch (error) {
      notifyOperationFailure('发布失败', error, '发布失败，请稍后重试')
    }
  }

  // 撤销发布：只对已发布方案开放，二次确认说明后果（MES 侧回流撤销对应工序排程）。
  // 权限沿用发布权限——后端 revoke 端点同样挂在 PlansRelease 权限码下。
  const revokeTargetPlanId = shallowRef('')
  const revokeConfirmOpen = shallowRef(false)

  function canRevoke(
    row: Pick<BusinessConsoleSchedulingPlanSummaryResponse, 'status'> | undefined,
  ) {
    return Boolean(row && canPublish.value && row.status === 'released')
  }

  function requestRevoke(
    plan: Pick<BusinessConsoleSchedulingPlanSummaryResponse, 'planId' | 'status'> | undefined,
  ) {
    if (!plan?.planId || !canRevoke(plan)) return
    revokeTargetPlanId.value = plan.planId
    revokeConfirmOpen.value = true
  }

  async function confirmRevoke() {
    const planId = revokeTargetPlanId.value
    if (!planId) return
    try {
      await revokePlan(planId)
      revokeConfirmOpen.value = false
      notifySuccess('排程方案已撤销发布，MES 侧将回流撤销对应工序排程')
    } catch (error) {
      notifyOperationFailure('撤销失败', error, '撤销失败，请稍后重试')
    }
  }

  // 已终止或失效的方案禁止发布，避免重复下达或下达一份过期计划。
  function canRelease(row: BusinessConsoleSchedulingPlanSummaryResponse) {
    return (
      canPublish.value && !schedulingPlanTerminalReleaseReason(row.status) && !row.isInvalidated
    )
  }

  function releaseDisabledReason(row: BusinessConsoleSchedulingPlanSummaryResponse) {
    if (!canPublish.value) return '当前账号没有排程发布权限'
    const terminalReason = schedulingPlanTerminalReleaseReason(row.status)
    if (terminalReason) return terminalReason
    if (row.isInvalidated)
      return `方案已失效（${describeScheduleInvalidationReason(row.latestInvalidationReasonCode)}），请重排后再发布`
    return '发布该排程方案'
  }

  return {
    publish,
    revokeTargetPlanId,
    revokeConfirmOpen,
    canRevoke,
    requestRevoke,
    confirmRevoke,
    canRelease,
    releaseDisabledReason,
  }
}
