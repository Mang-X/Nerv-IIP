import type {
  BusinessConsoleSchedulePlan,
  BusinessConsoleSchedulingInsertionPreviewRequest,
} from '@nerv-iip/api-client'
import { computed, shallowRef, toValue, watch, type MaybeRefOrGetter, type Ref } from 'vue'
import { inlineErrorMessage } from '@/utils/notify'
import { useSchedulingInsertionPreview } from './useSchedulingInsertionPreview'

interface Options {
  baseline: MaybeRefOrGetter<BusinessConsoleSchedulePlan | undefined>
  context: MaybeRefOrGetter<{ organizationId: string; environmentId: string }>
  enabled: Ref<boolean>
  saveOrder: (id: string, values: { priority: number; isRush: boolean }) => Promise<void>
}

/** 急单事实保存与候选计算分开；重试不会重复保存 MES 工单。 */
export function useSchedulingRushInsertion(options: Options) {
  const task = useSchedulingInsertionPreview(options.enabled)
  const message = shallowRef('')
  const request = shallowRef<BusinessConsoleSchedulingInsertionPreviewRequest>()
  let generation = 0
  function reset() {
    generation++
    task.reset()
    request.value = undefined
    message.value = ''
  }
  watch(
    () => [toValue(options.context).organizationId, toValue(options.context).environmentId],
    reset,
  )
  async function retry() {
    if (!request.value || !options.enabled.value) return
    const current = generation
    message.value = '正在受理插单候选'
    try {
      await task.start(request.value)
      if (current === generation) message.value = ''
    } catch (error) {
      if (current === generation)
        message.value = `急单已保存，候选受理失败：${inlineErrorMessage(error, '请手动重试')}`
    }
  }
  async function saveOrder(id: string, values: { priority: number; isRush: boolean }) {
    const current = ++generation
    // 在保存前固定正在编辑的持久化方案，不能取保存期间切换查阅的方案。
    const baseline = toValue(options.baseline)
    const context = { ...toValue(options.context) }
    await options.saveOrder(id, values)
    if (current !== generation || !values.isRush) return
    task.reset()
    request.value = undefined
    if (!baseline?.planId) {
      message.value = '急单已保存，请先生成方案，再计算插单候选。'
      return
    }
    if (!options.enabled.value) {
      message.value = '急单已保存，当前账号没有排产管理权限。'
      return
    }
    request.value = { ...context, planId: baseline.planId, workOrderId: id }
    await retry()
  }
  return {
    task,
    reset,
    message: computed(() =>
      task.error.value
        ? `急单已保存，候选计算失败：${inlineErrorMessage(task.error.value, '请手动重试')}`
        : message.value,
    ),
    request,
    saveOrder,
    retry,
  }
}
