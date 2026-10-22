import {
  acceptBusinessConsoleSchedulingInsertionPreviewJobMutationOptions,
  getBusinessConsoleSchedulingInsertionPreviewJobQueryOptions,
  type BusinessConsoleSchedulingInsertionPreviewJob,
  type BusinessConsoleSchedulingInsertionPreviewRequest,
} from '@nerv-iip/api-client'
import { useMutation, useQuery } from '@pinia/colada'
import { computed, onScopeDispose, shallowRef, watch, type Ref } from 'vue'
import { assertEnvelopeSuccess } from './serviceEnvelope'

/** 插单只读预览：终态结果随任务返回，不查询或保存排程方案。 */
export function useSchedulingInsertionPreview(enabled: Ref<boolean>) {
  const job = shallowRef<BusinessConsoleSchedulingInsertionPreviewJob>()
  const error = shallowRef<unknown>()
  let generation = 0
  const active = computed(
    () =>
      enabled.value &&
      !error.value &&
      (job.value?.status === 'created' || job.value?.status === 'running'),
  )
  const acceptance = useMutation(
    acceptBusinessConsoleSchedulingInsertionPreviewJobMutationOptions(),
  )
  const statusQuery = useQuery(() => ({
    ...getBusinessConsoleSchedulingInsertionPreviewJobQueryOptions({
      path: { jobId: job.value?.jobId ?? '' },
      query: {
        organizationId: job.value?.input?.organizationId ?? '',
        environmentId: job.value?.input?.environmentId ?? '',
      },
    }),
    enabled: active.value,
    autoRefetch: () => (active.value ? 1000 : false),
  }))
  function receive(next: BusinessConsoleSchedulingInsertionPreviewJob) {
    job.value = next
    if (next.status === 'failed')
      error.value = new Error(next.failureReason || '插单预览计算失败，请稍后重试。')
  }
  function reset() {
    generation++
    job.value = undefined
    error.value = undefined
  }
  watch(enabled, (value) => {
    if (!value) reset()
  })
  onScopeDispose(reset)
  watch(statusQuery.data, (response) => {
    if (!active.value || !response || response.data?.jobId !== job.value?.jobId) return
    try {
      assertEnvelopeSuccess(response, '插单预览进度读取失败')
      receive(response.data!)
    } catch (failure) {
      error.value = failure
    }
  })
  watch(statusQuery.error, (failure) => {
    if (active.value && failure) error.value = failure
  })
  return {
    job,
    error,
    reset,
    result: computed(() => job.value?.result ?? undefined),
    preview: computed(() => job.value?.preview ?? undefined),
    pending: computed(() => acceptance.isLoading.value || active.value),
    async start(body: BusinessConsoleSchedulingInsertionPreviewRequest) {
      reset()
      const current = generation
      const response = await acceptance.mutateAsync({ body })
      if (current !== generation || !enabled.value) return
      assertEnvelopeSuccess(response, '插单预览受理失败')
      receive(response.data!)
    },
  }
}
