import type { useBusinessPlanning } from '@/composables/planning/useBusinessPlanning'
import { notifyOperationFailure, notifySuccess, notifyWarning } from '@/utils/notify'
import { computed, watch } from 'vue'
import type { Ref } from 'vue'

export function usePlanningMrpRunDialog(
  { runMrp, activeMrpRun }: Pick<ReturnType<typeof useBusinessPlanning>, 'runMrp' | 'activeMrpRun'>,
  mrpOpen: Ref<boolean>,
) {
  // 运行 MRP（#1306 异步任务模式）：提交只是「受理」，弹框显示计算中且全程可关闭，
  // 后台照跑；轮询到终态后统一 toast + 刷新读面（关没关弹框都一样）。
  async function submitMrpRun() {
    try {
      await runMrp()
      notifySuccess('MRP 已受理，正在后台计算，完成后自动刷新。')
    } catch (error) {
      notifyOperationFailure('运行 MRP 失败', error, '运行 MRP 失败，请稍后重试。')
    }
  }
  const mrpRunInProgress = computed(
    () => activeMrpRun.status === 'queued' || activeMrpRun.status === 'running',
  )
  watch(
    () => activeMrpRun.status,
    (status) => {
      if (status === 'completed') {
        notifySuccess(`MRP 计算完成，共生成 ${activeMrpRun.suggestionCount ?? 0} 条计划建议。`)
        mrpOpen.value = false
      } else if (status === 'failed') {
        // failureReason 是服务端领域消息，走分层透传（中文原样上屏、英文映射人话）。
        // 后端 worker 对非预期异常会自带「MRP 计算失败：」前缀，这里去重避免叠成两层。
        const reason = activeMrpRun.failureReason.replace(/^MRP 计算失败[:：]\s*/, '')
        notifyOperationFailure(
          'MRP 计算失败',
          reason,
          'MRP 计算失败，请在「MRP 运行」列表查看原因。',
        )
      } else if (status === 'polling-timeout') {
        notifyWarning('MRP 仍在后台计算，可稍后在「MRP 运行」列表查看结果。')
      }
    },
  )

  return { submitMrpRun, mrpRunInProgress }
}
