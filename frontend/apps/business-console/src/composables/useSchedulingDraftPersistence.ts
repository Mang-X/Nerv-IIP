import {
  clearBusinessConsoleSchedulingWorkingDraft,
  getBusinessConsoleSchedulingPlan,
  listBusinessConsoleSchedulingWorkingDrafts,
  saveBusinessConsoleSchedulingWorkingDraft,
  type BusinessConsoleSchedulePlan,
  type SchedulingWorkingDraft,
} from '@nerv-iip/api-client'
import { computed, shallowRef, toValue, watch, type MaybeRefOrGetter, type ShallowRef } from 'vue'
import { notifyOperationFailure } from '@/utils/notify'
import { assertEnvelopeSuccess } from './serviceEnvelope'
import type { useWorkingScheduleDraft } from './useWorkingScheduleDraft'

type Status = 'loading' | 'empty' | 'saving' | 'saved' | 'clearing' | 'error'
interface Options {
  draft: ReturnType<typeof useWorkingScheduleDraft>
  baseline: ShallowRef<BusinessConsoleSchedulePlan | undefined>
  context: MaybeRefOrGetter<{ organizationId: string; environmentId: string }>
  userId: MaybeRefOrGetter<string | undefined>
  canManage: MaybeRefOrGetter<boolean>
  selectedPlanId?: MaybeRefOrGetter<string | undefined>
  selectPlan?: (planId: string) => void
}

export function useSchedulingDraftPersistence(options: Options) {
  const { draft, baseline } = options
  const status = shallowRef<Status>('empty')
  const savedAtUtc = shallowRef<string>()
  const savedDrafts = shallowRef<SchedulingWorkingDraft[]>([])
  const ready = shallowRef(false)
  let queue = Promise.resolve()
  let identityVersion = 0
  let requestVersion = 0
  let retryOperation: (() => Promise<unknown>) | undefined

  async function restoreSaved(saved: SchedulingWorkingDraft, version: number) {
    const { data } = await getBusinessConsoleSchedulingPlan({
      path: { planId: saved.planId! },
      query: toValue(options.context),
      throwOnError: true,
    })
    assertEnvelopeSuccess(data, '草稿基线读取失败')
    if (version !== identityVersion) return
    options.selectPlan?.(saved.planId!)
    baseline.value = data.data!
    draft.restoreSaved(data.data!, saved.state!)
    savedAtUtc.value = saved.savedAtUtc
    status.value = 'saved'
  }

  async function discover() {
    const version = ++identityVersion
    requestVersion++
    ready.value = false
    draft.clear()
    baseline.value = undefined
    savedDrafts.value = []
    savedAtUtc.value = undefined
    const context = toValue(options.context)
    if (!toValue(options.userId) || !context.organizationId || !context.environmentId) {
      status.value = 'empty'
      ready.value = true
      return
    }
    status.value = 'loading'
    try {
      const { data } = await listBusinessConsoleSchedulingWorkingDrafts({
        query: context,
        throwOnError: true,
      })
      assertEnvelopeSuccess(data, '草稿读取失败')
      if (version !== identityVersion) return
      savedDrafts.value = data.data ?? []
      const selectedPlanId = toValue(options.selectedPlanId)
      const latest = selectedPlanId
        ? savedDrafts.value.find((item) => item.planId === selectedPlanId)
        : [...savedDrafts.value].sort((a, b) => b.savedAtUtc!.localeCompare(a.savedAtUtc!))[0]
      if (latest) await restoreSaved(latest, version)
      else status.value = 'empty'
      if (version === identityVersion) ready.value = true
    } catch (error) {
      if (version !== identityVersion) return
      status.value = 'error'
      retryOperation = discover
      notifyOperationFailure('草稿恢复失败', error, '草稿未恢复，请重试后继续编辑。')
    }
  }

  function save() {
    const planId = draft.model.value?.meta.planId
    if (!ready.value || !toValue(options.canManage) || !toValue(options.userId) || !planId)
      return queue
    options.selectPlan?.(planId)
    const state = draft.exportState()
    const context = toValue(options.context)
    const identity = identityVersion
    const request = ++requestVersion
    status.value = 'saving'
    queue = queue.then(async () => {
      if (identity !== identityVersion) return
      try {
        const { data } = await saveBusinessConsoleSchedulingWorkingDraft({
          path: { planId },
          body: { ...context, state },
          throwOnError: true,
        })
        assertEnvelopeSuccess(data, '草稿保存失败')
        if (identity !== identityVersion) return
        savedDrafts.value = [
          ...savedDrafts.value.filter((item) => item.planId !== planId),
          data.data!,
        ]
        if (request === requestVersion) {
          savedAtUtc.value = data.data!.savedAtUtc
          status.value = 'saved'
        }
      } catch (error) {
        if (identity !== identityVersion) return
        if (request === requestVersion) {
          status.value = 'error'
          retryOperation = save
        }
        notifyOperationFailure('草稿保存失败', error, '本次修改尚未保存，请重试。')
      }
    })
    return queue
  }

  async function select(planId: string) {
    ready.value = false
    status.value = 'loading'
    requestVersion++
    const identity = identityVersion
    await queue
    if (identity !== identityVersion) return
    try {
      await restoreSaved(savedDrafts.value.find((item) => item.planId === planId)!, identity)
      if (identity === identityVersion) ready.value = true
    } catch (error) {
      if (identity !== identityVersion) return
      status.value = 'error'
      retryOperation = () => select(planId)
      notifyOperationFailure('草稿恢复失败', error, '草稿未恢复，请重试。')
    }
  }

  async function clear() {
    const planId = draft.model.value?.meta.planId
    if (!planId) return
    ready.value = false
    status.value = 'clearing'
    requestVersion++
    const identity = identityVersion
    const context = toValue(options.context)
    await queue
    if (identity !== identityVersion) return
    try {
      const { data } = await clearBusinessConsoleSchedulingWorkingDraft({
        path: { planId },
        query: context,
        throwOnError: true,
      })
      assertEnvelopeSuccess(data, '草稿清空失败')
      if (identity !== identityVersion) return
      draft.clear()
      baseline.value = undefined
      savedDrafts.value = savedDrafts.value.filter((item) => item.planId !== planId)
      savedAtUtc.value = undefined
      status.value = 'empty'
    } catch (error) {
      if (identity !== identityVersion) return
      status.value = 'error'
      retryOperation = clear
      notifyOperationFailure('草稿清空失败', error, '草稿未清空，请重试。')
    } finally {
      if (identity === identityVersion) ready.value = true
    }
  }

  watch(
    () => [
      toValue(options.userId),
      toValue(options.context).organizationId,
      toValue(options.context).environmentId,
    ],
    discover,
    { immediate: true },
  )
  watch(
    draft.changeVersion,
    () => {
      void save()
    },
    { flush: 'sync' },
  )
  return {
    status,
    ready,
    savedAtUtc,
    savedDrafts,
    save,
    clear,
    discover,
    select,
    retry: () => retryOperation!(),
    busy: computed(() => !ready.value || status.value === 'clearing'),
  }
}
