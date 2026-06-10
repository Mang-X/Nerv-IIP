import {
  listBusinessConsoleSchedulingPlans,
  releaseBusinessConsoleSchedulingPlan,
  type SchedulePlanSummary,
} from '@nerv-iip/api-client'
import { useSchedulingPlan, type SchedulingContext } from '@nerv-iip/scheduling'
import { computed, ref, watch } from 'vue'
import { useBusinessContextStore } from '@/stores/businessContext'

/**
 * 业务前端排程数据接线:组织/环境来自当前业务上下文;列出计划并默认选中最近一份;
 * 读取并归一化计划模型;发布走 BusinessGateway facade。
 * 重预览(preview)依赖完整排程问题定义,当前后端缺口 → 由工作台客户端保持锁定项,
 * 见 docs/architecture/scheduling-workbench-module-product-design.md「后端缺口」。
 */
export function useScheduling() {
  const ctxStore = useBusinessContextStore()
  const context = computed<SchedulingContext>(() => ({
    organizationId: ctxStore.organizationId,
    environmentId: ctxStore.environmentId,
  }))

  const plans = ref<SchedulePlanSummary[]>([])
  const planId = ref<string>()
  const listLoading = ref(false)
  const listError = ref<unknown>()

  async function loadPlans() {
    const { organizationId, environmentId } = context.value
    if (!organizationId || !environmentId) return
    listLoading.value = true
    listError.value = undefined
    try {
      const res = await listBusinessConsoleSchedulingPlans({
        query: { organizationId, environmentId, pageIndex: 1, pageSize: 20 },
      })
      const data = (res.data as { data?: SchedulePlanSummary[] | null } | undefined)?.data ?? []
      plans.value = data
      if ((!planId.value || !data.some((p) => p.planId === planId.value)) && data.length) {
        planId.value = data[0].planId ?? undefined
      }
    } catch (e) {
      listError.value = e
    } finally {
      listLoading.value = false
    }
  }

  const { model, loading, error, reload } = useSchedulingPlan(planId, context)

  async function release(id: string) {
    const { organizationId, environmentId } = context.value
    await releaseBusinessConsoleSchedulingPlan({
      path: { planId: id },
      query: { organizationId, environmentId },
    })
    await loadPlans()
    await reload()
  }

  watch(context, () => void loadPlans(), { immediate: true, deep: true })

  return { context, plans, planId, model, loading, error, listLoading, listError, loadPlans, reload, release }
}
