import { openSopFileContent } from '@nerv-iip/business-core'
import { REQUEST_TIMEOUT_MS } from '@/api/request-timeout'
import { useMesCurrentOperationSops } from '@/composables/mes'
import { ref } from 'vue'

type CurrentSop = { fileId?: string | null; fileName?: string | null }

export function useOperationSopFiles() {
  const {
    filters: sopFilters,
    currentSops,
    pending: sopsPending,
    error: sopsError,
    refresh: refreshSops,
  } = useMesCurrentOperationSops()

  const openingSopFileId = ref<string | null>(null)
  const sopFileError = ref('')

  // 下载复用 PDA 全局超时，弱网或离线时有界失败（#814）。

  async function openSopFile(sop: CurrentSop) {
    const fileId = sop.fileId?.trim()
    if (!fileId) {
      sopFileError.value = '当前SOP未绑定可查看的文件。'
      return
    }
    sopFileError.value = ''
    openingSopFileId.value = fileId
    try {
      await openSopFileContent(
        fileId,
        { organizationId: sopFilters.organizationId, environmentId: sopFilters.environmentId },
        { timeoutMs: REQUEST_TIMEOUT_MS },
      )
    } catch (error) {
      sopFileError.value = error instanceof Error ? error.message : '无法打开SOP。'
    } finally {
      openingSopFileId.value = null
    }
  }

  return {
    sopFilters,
    currentSops,
    sopsPending,
    sopsError,
    refreshSops,
    openingSopFileId,
    sopFileError,
    openSopFile,
  }
}
