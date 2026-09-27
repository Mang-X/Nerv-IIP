import type {
  BusinessConsoleMesOperationTaskRow,
  BusinessConsoleMesTelemetryCandidateRow,
} from '@nerv-iip/api-client'
import { computed, ref, shallowRef, watch, type Ref } from 'vue'
import { useDeviceAssetNames } from '@/composables/useBusinessDeviceDirectory'
import {
  type MesReportExecutionContext,
  useMesTelemetryCandidateTargetTasks,
} from '@/composables/useBusinessMes'

type Task = BusinessConsoleMesOperationTaskRow

/**
 * 遥测候选转正的目标工序。车间惯例是选设备当前在制的工单工序，不输入编号，目标按优先级派生：
 * 用户显式点选 > 候选自带的工单工序 > 这台设备上唯一一道执行中的工序。
 */
export function useTelemetryCandidateTarget(
  candidate: Readonly<Ref<BusinessConsoleMesTelemetryCandidateRow | undefined>>,
  context: Readonly<Ref<MesReportExecutionContext | undefined>>,
) {
  const keyword = ref('')
  const chosen = shallowRef<Task | null>(null)
  // 「改选」进行中时旧目标不算数：此时不许转正，免得提交的是用户正要换掉的那道。
  const choosing = ref(false)
  const deviceAssetId = computed(() => candidate.value?.deviceAssetId?.trim() ?? '')
  const { tasks, pending, error, refresh } = useMesTelemetryCandidateTargetTasks(
    context,
    deviceAssetId,
    keyword,
  )

  const target = computed<Task | null>(() => {
    const current = candidate.value
    if (!current) return null
    if (chosen.value) return chosen.value
    const workOrderId = current.workOrderId?.trim()
    const operationTaskId = current.operationTaskId?.trim()
    if (workOrderId && operationTaskId) {
      // 候选只带编号；列表里有同一道工序时用整行，好显示工序序号。
      return (
        tasks.value.find((row) => row.operationTaskId === operationTaskId) ?? {
          workOrderId,
          operationTaskId,
        }
      )
    }
    return !keyword.value.trim() && tasks.value.length === 1 ? tasks.value[0] : null
  })

  watch(
    () => candidate.value?.candidateId,
    () => {
      keyword.value = ''
      chosen.value = null
      choosing.value = false
    },
  )

  // 列表行要说清是哪台设备（尤其标了「非本设备」的行）；工序读面只回设备标识，名称回主数据查。
  const { resolveDeviceName } = useDeviceAssetNames(
    computed(() => tasks.value.map((task) => task.deviceAssetId ?? '')),
  )
  function deviceLabel(task: Task) {
    if (!task.deviceAssetId?.trim()) return '未派设备'
    return task.deviceAssetName?.trim() || resolveDeviceName(task.deviceAssetId) || '设备信息未提供'
  }

  return {
    keyword,
    deviceAssetId,
    tasks,
    pending,
    error,
    refresh,
    target,
    choosing,
    deviceLabel,
    choose: (task: Task) => {
      chosen.value = task
      choosing.value = false
    },
  }
}
