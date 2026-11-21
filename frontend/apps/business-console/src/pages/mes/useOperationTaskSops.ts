import type { BusinessConsoleMesOperationTaskRow } from '@nerv-iip/api-client'
import { openSopFileContent } from '@nerv-iip/business-core'
import { useMesCurrentOperationSops } from '@/composables/mes'
import { inlineErrorMessage } from '@/utils/notify'
import { computed, ref } from 'vue'

type Row = BusinessConsoleMesOperationTaskRow
type CurrentSop = { fileId?: string | null; fileName?: string | null }

export function useOperationTaskSops() {
  const selectedSopTask = ref<Row | null>(null)
  const openingSopFileId = ref<string | null>(null)
  const sopFileError = ref('')
  const {
    filters: sopFilters,
    currentSops,
    currentSopsError,
    currentSopsPending,
    refreshCurrentSops,
  } = useMesCurrentOperationSops()

  const sopContextMessage = computed(() => {
    const task = selectedSopTask.value
    if (!task) return ''
    if (!task.routingCode?.trim() || !task.routingRevision?.trim())
      return '当前任务缺少工艺路线号或版本，无法确定适用的SOP。'
    if (!task.workCenterCode?.trim()) return '当前任务缺少工作中心编码，无法确定适用的SOP。'
    return ''
  })

  function closeSops() {
    selectedSopTask.value = null
    sopFilters.operationCode = ''
    sopFileError.value = ''
  }

  function openSops(task: Row) {
    selectedSopTask.value = task
    sopFileError.value = ''
    sopFilters.operationCode = sopContextMessage.value ? '' : (task.operationCode?.trim() ?? '')
    sopFilters.workCenterCode = task.workCenterCode?.trim() ?? ''
    sopFilters.routingCode = task.routingCode?.trim() ?? ''
    sopFilters.routingRevision = task.routingRevision?.trim() ?? ''
    sopFilters.asOfDate = ''
  }
  async function openSopFile(sop: CurrentSop) {
    const fileId = sop.fileId?.trim()
    if (!fileId) {
      sopFileError.value = '当前SOP未绑定可查看的文件。'
      return
    }
    sopFileError.value = ''
    openingSopFileId.value = fileId
    try {
      await openSopFileContent(fileId, {
        organizationId: sopFilters.organizationId,
        environmentId: sopFilters.environmentId,
      })
    } catch (error) {
      sopFileError.value = inlineErrorMessage(error, '无法打开SOP。')
    } finally {
      openingSopFileId.value = null
    }
  }

  function canOpenSops(task: Row) {
    return Boolean(task.operationCode?.trim())
  }
  const selectedSopErrorMessage = computed(() => inlineErrorMessage(currentSopsError.value))
  const selectedSopTitle = computed(() => {
    const task = selectedSopTask.value
    if (!task) return ''
    return `${task.operationTaskNo ?? task.operationTaskId ?? '工序任务'} · ${task.operationCode ?? '未绑定工序'}`
  })

  return {
    selectedSopTask,
    openingSopFileId,
    sopFileError,
    currentSops,
    currentSopsPending,
    refreshCurrentSops,
    sopContextMessage,
    closeSops,
    openSops,
    openSopFile,
    canOpenSops,
    selectedSopErrorMessage,
    selectedSopTitle,
  }
}
