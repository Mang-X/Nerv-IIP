import type {
  BusinessConsoleSchedulingAssignment,
  BusinessConsoleSchedulingPlanSummaryResponse,
} from '@nerv-iip/api-client'
import {
  type useBusinessScheduling,
  useSchedulingPlanSummary,
} from '@/composables/scheduling/useBusinessScheduling'
import { computed, shallowRef, watch } from 'vue'
import type { RouteLocationNormalizedLoaded } from 'vue-router'
import type { Ref } from 'vue'

export function useSchedulingPlanDetails(
  {
    detailSelection,
    planDetail,
    planDetailPending,
    planDetailError,
  }: Pick<
    ReturnType<typeof useBusinessScheduling>,
    'detailSelection' | 'planDetail' | 'planDetailPending' | 'planDetailError'
  >,
  route: RouteLocationNormalizedLoaded,
  actionablePlans: Readonly<
    Ref<(BusinessConsoleSchedulingPlanSummaryResponse & { planId: string })[]>
  >,
  rangeFromAssignments: (assignments: BusinessConsoleSchedulingAssignment[]) => string,
) {
  const detailOpen = shallowRef(false)
  const targetedOrderReference = computed(() => {
    const value = route.query.orderReference
    return (Array.isArray(value) ? value[0] : value)?.trim() ?? ''
  })
  const routeLookupVisited = new Set<string>()
  // 单单排产（MAN-694 / #1262）落点：带 planId 直接定位到刚生成的方案，不必在列表里翻。
  const targetedPlanId = computed(() => {
    const value = route.query.planId
    return (Array.isArray(value) ? value[0] : value)?.trim() ?? ''
  })
  const selectedPlanRange = computed(() =>
    rangeFromAssignments(planDetail.value?.assignments ?? []),
  )
  const selectedResourceCount = computed(() => {
    const resourceIds = new Set(
      (planDetail.value?.resourceLoads ?? [])
        .map((load) => load.resourceId)
        .filter((value): value is string => Boolean(value)),
    )
    return resourceIds.size
  })
  const detailFeedback = computed(() => {
    if (planDetailError.value) return '明细加载失败，请稍后重试。'
    if (detailSelection.planId) return '未返回方案明细。'
    return '请选择一个排程方案查看明细。'
  })
  // 查阅方案和草案各按自身 planId 获取摘要，历史表筛选/分页不决定发布能力。
  const { summary: selectedPlanStatus } = useSchedulingPlanSummary(() => detailSelection.planId)
  const selectedPlanSummary = computed(() =>
    planDetail.value
      ? {
          ...selectedPlanStatus.value,
          planId: planDetail.value.planId,
          status: planDetail.value.status,
        }
      : undefined,
  )
  const targetedAssignmentFound = computed(() =>
    Boolean(
      targetedOrderReference.value &&
      planDetail.value?.assignments?.some(
        (assignment) => assignment.orderId === targetedOrderReference.value,
      ),
    ),
  )

  watch(targetedOrderReference, () => routeLookupVisited.clear())
  // 已经点名了具体方案（单单排产刚生成的那份）就直接打开，不再走「逐个方案找订单」的兜底。
  watch(
    targetedPlanId,
    (planId) => {
      if (!planId) return
      detailSelection.planId = planId
      detailOpen.value = true
    },
    { immediate: true },
  )
  watch(
    [targetedOrderReference, actionablePlans, planDetail, planDetailPending],
    ([target, availablePlans, detail, pending]) => {
      if (!target || availablePlans.length === 0 || pending) return
      if (targetedPlanId.value) return
      if (!detailSelection.planId) {
        detailSelection.planId = availablePlans[0]?.planId ?? ''
        detailOpen.value = Boolean(detailSelection.planId)
        return
      }
      if (!detail || detail.planId !== detailSelection.planId) return
      if (detail.assignments?.some((assignment) => assignment.orderId === target)) {
        detailOpen.value = true
        return
      }

      routeLookupVisited.add(detailSelection.planId)
      const next = availablePlans.find((plan) => !routeLookupVisited.has(plan.planId))
      if (next?.planId) {
        detailSelection.planId = next.planId
        detailOpen.value = true
      }
    },
    { immediate: true },
  )

  function openDetail(planId: string | undefined) {
    if (!planId) return
    detailSelection.planId = planId
    detailOpen.value = true
  }

  return {
    detailOpen,
    selectedPlanRange,
    selectedResourceCount,
    detailFeedback,
    selectedPlanSummary,
    targetedAssignmentFound,
    targetedOrderReference,
    openDetail,
  }
}
