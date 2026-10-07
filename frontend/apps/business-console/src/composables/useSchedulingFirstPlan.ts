import {
  acceptBusinessConsoleSchedulingFirstPlanJobMutationOptions,
  getBusinessConsoleSchedulingFirstPlanJobQueryOptions,
  getBusinessConsoleSchedulingPlan,
  type BusinessConsoleSchedulingFirstPlanInput,
  type BusinessConsoleSchedulingFirstPlanJob,
  type BusinessConsoleSchedulePlan,
} from '@nerv-iip/api-client'
import { useMutation, useQuery } from '@pinia/colada'
import { computed, shallowRef, watch } from 'vue'
import { assertEnvelopeSuccess } from './serviceEnvelope'

/** 首版受理与状态查询分开；终态只加载一次持久化方案。 */
export function useSchedulingFirstPlan(onCompleted: () => void) {
  const job = shallowRef<BusinessConsoleSchedulingFirstPlanJob>()
  const plan = shallowRef<BusinessConsoleSchedulePlan>()
  const error = shallowRef<unknown>()
  const loadingPlan = shallowRef(false)
  const active = computed(() => job.value?.status === 'created' || job.value?.status === 'running')
  const acceptance = useMutation(acceptBusinessConsoleSchedulingFirstPlanJobMutationOptions())
  const statusQuery = useQuery(() => ({
    ...getBusinessConsoleSchedulingFirstPlanJobQueryOptions({
      path: { jobId: job.value?.jobId ?? '' },
      query: {
        organizationId: job.value?.input?.organizationId ?? '',
        environmentId: job.value?.input?.environmentId ?? '',
      },
    }),
    enabled: active.value && !error.value,
    autoRefetch: () => (active.value && !error.value ? 1000 : false),
  }))

  async function receive(next: BusinessConsoleSchedulingFirstPlanJob) {
    job.value = next
    if (next.status === 'failed') {
      error.value = new Error(next.failureReason || '首版排程生成失败')
    } else if (next.status === 'completed') {
      loadingPlan.value = true
      try {
        const response = await getBusinessConsoleSchedulingPlan({
          path: { planId: next.planId! },
          query: {
            organizationId: next.input!.organizationId!,
            environmentId: next.input!.environmentId!,
          },
          throwOnError: true,
        })
        assertEnvelopeSuccess(response.data, '方案加载失败')
        plan.value = response.data!.data!
        onCompleted()
      } catch (failure) {
        error.value = failure
      } finally {
        loadingPlan.value = false
      }
    }
  }

  watch(statusQuery.data, (response) => {
    if (!active.value || !response) return
    try {
      assertEnvelopeSuccess(response, '首版排程进度读取失败')
      void receive(response.data!)
    } catch (failure) {
      error.value = failure
    }
  })
  watch(statusQuery.error, (failure) => {
    if (active.value && failure) error.value = failure
  })

  return {
    job,
    plan,
    error,
    pending: computed(
      () => acceptance.isLoading.value || (active.value && !error.value) || loadingPlan.value,
    ),
    async generatePlan(body: BusinessConsoleSchedulingFirstPlanInput) {
      error.value = undefined
      plan.value = undefined
      job.value = undefined
      const response = await acceptance.mutateAsync({ body })
      assertEnvelopeSuccess(response, '首版排程受理失败')
      await receive(response.data!)
    },
  }
}
