import {
  createBusinessConsoleMaintenanceDowntimeReasonMutationOptions,
  deleteBusinessConsoleMaintenanceDowntimeReasonMutationOptions,
  updateBusinessConsoleMaintenanceDowntimeReasonMutationOptions,
  type BusinessConsoleCreateMaintenanceDowntimeReasonRequest,
  type BusinessConsoleUpdateMaintenanceDowntimeReasonRequest,
} from '@nerv-iip/api-client'
import { useMutation, useQueryCache, type UseQueryEntry } from '@pinia/colada'
import { computed } from 'vue'

/** 停机原因目录读自可搜索目录 `downtime-reason`；写完让所有停机原因候选（建单、完工、MES 停机）重取。 */
function isDowntimeReasonDirectoryQuery(entry: UseQueryEntry) {
  const keyParts = Array.isArray(entry.key) ? entry.key : [entry.key]
  return keyParts.some((part) => {
    if (typeof part !== 'object' || part === null || !('_id' in part)) return false
    if (part._id !== 'listBusinessConsoleSearchableDirectory') return false
    const path = (part as { path?: { directoryType?: string } }).path
    return path?.directoryType === 'downtime-reason' || path?.directoryType === 'maintenance-reason'
  })
}

/** 停机原因目录维护（#3855）：新增 / 修改 / 删除，沿用维修工单管理权限。 */
export function useMaintenanceDowntimeReasonMutations() {
  const queryCache = useQueryCache()
  const refreshDirectory = () =>
    queryCache
      .invalidateQueries({ predicate: isDowntimeReasonDirectoryQuery })
      .catch(() => undefined)

  const createMutation = useMutation({
    ...createBusinessConsoleMaintenanceDowntimeReasonMutationOptions(),
    onSuccess: () => void refreshDirectory(),
  })
  const updateMutation = useMutation({
    ...updateBusinessConsoleMaintenanceDowntimeReasonMutationOptions(),
    onSuccess: () => void refreshDirectory(),
  })
  const deleteMutation = useMutation({
    ...deleteBusinessConsoleMaintenanceDowntimeReasonMutationOptions(),
    onSuccess: () => void refreshDirectory(),
  })

  return {
    createReason: (body: BusinessConsoleCreateMaintenanceDowntimeReasonRequest) =>
      createMutation.mutateAsync({ body }),
    updateReason: (
      reasonCode: string,
      body: BusinessConsoleUpdateMaintenanceDowntimeReasonRequest,
    ) => updateMutation.mutateAsync({ path: { reasonCode }, body }),
    deleteReason: (reasonCode: string, scope: { organizationId: string; environmentId: string }) =>
      deleteMutation.mutateAsync({ path: { reasonCode }, body: scope }),
    saving: computed(() => createMutation.isLoading.value || updateMutation.isLoading.value),
    deleting: deleteMutation.isLoading,
  }
}
