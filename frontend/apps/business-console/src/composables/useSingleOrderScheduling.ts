import {
  createBusinessConsoleSchedulingWorkbenchPlanMutationOptions,
  type BusinessConsoleSchedulePlan,
} from '@nerv-iip/api-client'
import { useMutation, useQueryCache } from '@pinia/colada'
import { computed, reactive } from 'vue'
import { BUSINESS_PERMISSION_CODES } from '@/permissions'
import { useAuthStore } from '@/stores/auth'
import { bindBusinessContext, hasBusinessContext } from './businessContextBinding'
import { isSchedulingWorkbenchQuery } from './useSchedulingWorkbench'

const SCHEDULING_IDS = ['listBusinessConsoleSchedulingPlans', 'getBusinessConsoleSchedulingPlan']

/** 单单排产入口缺权限时的统一说明（三处入口 + 弹窗共用一句话）。 */
export const SINGLE_ORDER_SCHEDULING_DENIED_REASON =
  '当前账号没有排产管理权限（business.scheduling.plans.manage）。'

/**
 * 能否发起单单排产。三处入口（销售订单 / MES 工单详情 / 计划建议行）与弹窗共用同一判定，
 * 避免各写一份 `permissionCodes.includes(...)` 而在权限码调整时漂移。
 */
export function useCanScheduleSingleOrder() {
  const auth = useAuthStore()
  return computed(() =>
    (auth.principal?.permissionCodes ?? []).includes(
      BUSINESS_PERMISSION_CODES.schedulingPlansManage,
    ),
  )
}

export interface SingleOrderSchedulingRequest {
  workOrderId: string
  priority: number
  isRush: boolean
  horizonStartUtc: string
  horizonEndUtc: string
}

/**
 * 单单排产（MAN-694 / #1262）。
 *
 * 复用排产工作台既有的 workbench 生成端点，只是 `orders` 里**只放这一张工单**、
 * 窗口由调用方给定——没有新增后端端点，也没有第二条生成路径。
 *
 * 语义：这里生成的是**一个只含该单的新方案**，不会把该单插进已有方案。
 * TODO(MAN-674 / #1241)：插入现有方案需要后端的插单预览（dry-run）能力，
 * 目前 `POST /scheduling/plans/preview` 要求前端提交完整 SchedulingProblemContract，
 * 而 problem 只能由后端 SchedulingWorkbenchSourceProvider 从工单选择组装，前端无法自造。
 */
export function useSingleOrderScheduling() {
  const context = bindBusinessContext(reactive({ organizationId: '', environmentId: '' }))
  const queryCache = useQueryCache()
  const mutation = useMutation({
    ...createBusinessConsoleSchedulingWorkbenchPlanMutationOptions(),
    onSuccess() {
      void Promise.all(
        SCHEDULING_IDS.map((id) =>
          queryCache.invalidateQueries({ predicate: isSchedulingWorkbenchQuery([id]) }),
        ),
      )
    },
  })

  async function scheduleSingleOrder(request: SingleOrderSchedulingRequest) {
    const workOrderId = request.workOrderId.trim()
    // 空业务范围 / 空工单一律不发请求：宁可留在弹窗里报错，也不打一串必失败的请求。
    if (!hasBusinessContext(context)) {
      throw new Error('请先选择组织与环境后再排产。')
    }
    if (!workOrderId) {
      throw new Error('请先选择要排产的工单。')
    }

    const envelope = (await mutation.mutateAsync({
      body: {
        organizationId: context.organizationId,
        environmentId: context.environmentId,
        horizonStartUtc: request.horizonStartUtc,
        horizonEndUtc: request.horizonEndUtc,
        orders: [{ workOrderId, priority: request.priority, isRush: request.isRush }],
      },
    })) as { success?: boolean; data?: BusinessConsoleSchedulePlan | null; message?: string }

    if (!envelope.success || !envelope.data) {
      throw new Error(envelope.message || '排程服务未返回方案。')
    }
    return envelope.data
  }

  return {
    context,
    hasScope: computed(() => hasBusinessContext(context)),
    pending: mutation.isLoading,
    scheduleSingleOrder,
  }
}

/** 单单排产成功后的落点：排产工作台的方案明细，并高亮这张工单。 */
export function singleOrderSchedulingResultRoute(planId: string, workOrderId: string) {
  return { path: '/scheduling', query: { planId, orderReference: workOrderId } }
}
