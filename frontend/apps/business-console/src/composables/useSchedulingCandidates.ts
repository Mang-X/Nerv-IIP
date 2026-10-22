import {
  previewBusinessConsoleSchedulingCandidates,
  selectBusinessConsoleSchedulingCandidate,
  type SchedulingCandidate,
  type SchedulingCandidateSet,
  type SchedulingCandidateSelection,
} from '@nerv-iip/api-client'
import { shallowRef, toValue, watch, type MaybeRefOrGetter } from 'vue'
import { notifyOperationFailure } from '@/utils/notify'
import { assertEnvelopeSuccess } from './serviceEnvelope'

interface Options {
  context: MaybeRefOrGetter<{ organizationId: string; environmentId: string }>
  baselinePlanId: MaybeRefOrGetter<string | undefined>
  onSelected: (selection: SchedulingCandidateSelection) => Promise<void>
}

export function useSchedulingCandidates(options: Options) {
  const candidates = shallowRef<SchedulingCandidateSet>()
  const pending = shallowRef(false)
  let identity = 0
  watch(
    () => [
      toValue(options.baselinePlanId),
      toValue(options.context).organizationId,
      toValue(options.context).environmentId,
    ],
    () => {
      identity++
      candidates.value = undefined
    },
  )

  async function preview() {
    const baselinePlanId = toValue(options.baselinePlanId)
    if (!baselinePlanId || pending.value) return
    const version = identity
    pending.value = true
    try {
      const response = await previewBusinessConsoleSchedulingCandidates({
        body: { ...toValue(options.context), baselinePlanId },
        throwOnError: true,
      })
      assertEnvelopeSuccess(response.data, '候选生成失败')
      if (version === identity) candidates.value = response.data!.data!
    } catch (error) {
      notifyOperationFailure('候选生成失败', error, '请核实当前方案与事实后重预览。')
    } finally {
      pending.value = false
    }
  }

  async function select(candidate: SchedulingCandidate) {
    const set = candidates.value
    if (!set || pending.value || set.baselinePlanId !== toValue(options.baselinePlanId)) return
    pending.value = true
    const version = identity
    try {
      const response = await selectBusinessConsoleSchedulingCandidate({
        body: {
          ...toValue(options.context),
          baselinePlanId: set.baselinePlanId!,
          asOfUtc: set.asOfUtc!,
          inputFingerprint: set.inputFingerprint!,
          strategy: candidate.strategy!,
        },
        throwOnError: true,
      })
      assertEnvelopeSuccess(response.data, '候选选定失败')
      if (version === identity) await options.onSelected(response.data!.data!)
    } catch (error) {
      notifyOperationFailure('候选选定失败', error, '请重预览后重新选择。')
    } finally {
      pending.value = false
    }
  }
  return { candidates, pending, preview, select }
}
