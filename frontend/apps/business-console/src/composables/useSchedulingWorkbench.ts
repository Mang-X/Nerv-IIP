import {
  createBusinessConsoleSchedulingPlanRevisionMutationOptions,
  createBusinessConsoleSchedulingWorkbenchPlanMutationOptions,
  type BusinessConsoleCreateSchedulePlanRevisionRequest,
  type BusinessConsoleCreateSchedulingWorkbenchPlanRequest,
  type BusinessConsoleSchedulePlan,
  type BusinessConsoleSchedulingPlanRevision,
} from '@nerv-iip/api-client'
import { useMutation, useQueryCache } from '@pinia/colada'
import type { UseQueryEntry } from '@pinia/colada'
import { computed } from 'vue'
import { useMesWorkOrders } from './useBusinessMes'

const SCHEDULING_IDS = ['listBusinessConsoleSchedulingPlans', 'getBusinessConsoleSchedulingPlan']

/**
 * 工单状态 → 是否可进排程候选，**单一事实来源**。
 *
 * 之前这里维护两份清单（查询白名单 + 终态黑名单），今天恰好等价，但 MES 一旦新增状态，
 * 两份会各自漂移：白名单不带上它 → 查不回来，黑名单不认识它 → 前端以为可排，静默丢单。
 * 现在两处都由本表派生：**新增 MES 状态只改这一处**。
 *
 * UX 预过滤而已；权威判定仍在 Scheduling 服务
 * （`SchedulingWorkbenchSourceProvider.TerminalStatuses`，与本表 terminal 项保持一致）。
 */
const WORK_ORDER_STATUS_SCHEDULABILITY: Readonly<Record<string, 'schedulable' | 'terminal'>> = {
  created: 'schedulable',
  released: 'schedulable',
  started: 'schedulable',
  hold: 'schedulable',
  completed: 'terminal',
  closed: 'terminal',
  cancelled: 'terminal',
  canceled: 'terminal',
  scrapped: 'terminal',
}

/** 候选工单查询的状态过滤（后端只支持正向枚举，故由上表派生正向清单）。 */
export const SCHEDULABLE_WORK_ORDER_STATUSES = Object.entries(WORK_ORDER_STATUS_SCHEDULABILITY)
  .filter(([, kind]) => kind === 'schedulable')
  .map(([status]) => status)

export function useSchedulingWorkbench() {
  const mes = useMesWorkOrders({ initialTake: 500 })
  // 只取非终态工单:后端默认按 DueUtc 升序,交期最早的历史关单排在最前,不带状态过滤时
  // take=500 的窗口会被终态工单占满,可排候选恒为 0(真机 4759 单中前 ~3900 条全是 closed)。
  mes.filters.statuses = SCHEDULABLE_WORK_ORDER_STATUSES.join(',')
  const queryCache = useQueryCache()

  const invalidatePlans = () =>
    Promise.all(
      SCHEDULING_IDS.map((id) =>
        queryCache.invalidateQueries({
          predicate: isSchedulingWorkbenchQuery([id]),
        }),
      ),
    )
  const generateMutation = useMutation({
    ...createBusinessConsoleSchedulingWorkbenchPlanMutationOptions(),
    onSuccess() {
      void invalidatePlans()
    },
  })
  const revisionMutation = useMutation({
    ...createBusinessConsoleSchedulingPlanRevisionMutationOptions(),
    onSuccess() {
      void invalidatePlans()
    },
  })

  return {
    candidates: mes.workOrders,
    candidatesError: mes.workOrdersError,
    candidatesPending: mes.workOrdersPending,
    // 待排池的候选查询与 MES 工单同一 scope gate：范围未就绪时查询不发（enabled=false），
    // 页面必须以此区分「没查」与「查了确实没有」，否则渲染成假空态（#1288）。
    candidatesScopeMessage: mes.workOrderReadScopeMessage,
    candidatesScopeReady: mes.workOrderReadScopeReady,
    filters: mes.filters,
    generatePending: generateMutation.isLoading,
    generatePlan: async (body: BusinessConsoleCreateSchedulingWorkbenchPlanRequest) =>
      unwrap<BusinessConsoleSchedulePlan>(await generateMutation.mutateAsync({ body })),
    refreshCandidates: mes.refreshWorkOrders,
    revisionPending: revisionMutation.isLoading,
    revisePlan: async (planId: string, body: BusinessConsoleCreateSchedulePlanRevisionRequest) =>
      unwrap<BusinessConsoleSchedulingPlanRevision>(
        await revisionMutation.mutateAsync({ path: { planId }, body }),
      ),
    schedulableCandidates: computed(() =>
      mes.workOrders.value.filter(isSchedulableWorkbenchCandidate),
    ),
  }
}

export function isSchedulingWorkbenchQuery(ids: string[]) {
  return (entry: UseQueryEntry) => {
    const keyParts = Array.isArray(entry.key) ? entry.key : [entry.key]
    return keyParts.some(
      (part) =>
        typeof part === 'object' &&
        part !== null &&
        '_id' in part &&
        ids.includes(String(part._id)),
    )
  }
}

export function isSchedulableWorkbenchCandidate(order: {
  workOrderId?: string | null
  productionVersionId?: string | null
  status?: string | null
}) {
  // 未知状态不当作终态丢弃：宁可让服务端拒绝，也不让前端凭一份过期清单静默吞掉工单。
  return (
    Boolean(order.workOrderId && order.productionVersionId) &&
    WORK_ORDER_STATUS_SCHEDULABILITY[order.status?.toLowerCase() ?? ''] !== 'terminal'
  )
}

function unwrap<T>(envelope: unknown): T {
  const response = envelope as { success?: boolean; data?: T | null; message?: string }
  if (!response.success || !response.data) {
    throw new Error(response.message || 'Scheduling service returned no data.')
  }
  return response.data
}
