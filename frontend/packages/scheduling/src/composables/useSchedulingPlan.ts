import { getBusinessConsoleSchedulingPlan, type SchedulePlanContract } from '@nerv-iip/api-client'
import { ref, watch, type Ref } from 'vue'
import { toModel } from '../model/aps-mapper'
import type { ScheduleModel } from '../model/types'

export interface SchedulingContext {
  organizationId: string
  environmentId: string
}

/** 读取一份排程计划并归一化为 ScheduleModel。需要组织/环境上下文(来自当前登录主体)。 */
export function useSchedulingPlan(planId: Ref<string | undefined>, context: Ref<SchedulingContext>) {
  const model = ref<ScheduleModel>()
  const loading = ref(false)
  const error = ref<unknown>()

  async function reload() {
    const id = planId.value
    const { organizationId, environmentId } = context.value
    if (!id || !organizationId || !environmentId) {
      model.value = undefined
      return
    }
    loading.value = true
    error.value = undefined
    try {
      const res = await getBusinessConsoleSchedulingPlan({
        path: { planId: id },
        query: { organizationId, environmentId },
      })
      const envelope = res.data as { data?: SchedulePlanContract | null } | undefined
      const plan = envelope?.data
      model.value = plan ? toModel(plan) : undefined
    } catch (e) {
      error.value = e
    } finally {
      loading.value = false
    }
  }

  watch([planId, context], () => void reload(), { immediate: true, deep: true })
  return { model, loading, error, reload }
}
