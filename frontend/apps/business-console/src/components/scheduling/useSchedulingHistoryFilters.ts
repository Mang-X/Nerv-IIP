import type { BusinessConsoleSchedulingPlanStatus } from '@nerv-iip/api-client'
import type { useBusinessScheduling } from '@/composables/scheduling/useBusinessScheduling'
import { useSchedulingPlanCsv } from '@/composables/scheduling/useSchedulingPlanCsv'
import { notifyOperationFailure, notifySuccess } from '@/utils/notify'
import { computed } from 'vue'

export function useSchedulingHistoryFilters({
  detailSelection,
  filters: schedulingFilters,
}: Pick<ReturnType<typeof useBusinessScheduling>, 'detailSelection' | 'filters'>) {
  const csv = useSchedulingPlanCsv()
  async function downloadPlanCsv() {
    try {
      await csv.download({ ...detailSelection })
      notifySuccess('方案 CSV 已下载')
    } catch (error) {
      notifyOperationFailure('方案 CSV 下载失败', error, '方案 CSV 下载失败，请稍后重试。')
    }
  }

  const historyStatus = computed({
    get: () => schedulingFilters.status ?? 'all',
    set: (value: string) => {
      schedulingFilters.status =
        value === 'all' ? undefined : (value as BusinessConsoleSchedulingPlanStatus)
    },
  })
  const historyInvalidated = computed({
    get: () =>
      schedulingFilters.isInvalidated === undefined
        ? 'all'
        : String(schedulingFilters.isInvalidated),
    set: (value: string) => {
      schedulingFilters.isInvalidated = value === 'all' ? undefined : value === 'true'
    },
  })
  const historyStatuses = ['generated', 'released', 'superseded', 'revoked'] as const
  const hasHistoryFilters = computed(() =>
    Boolean(
      schedulingFilters.status ||
      schedulingFilters.releasedOn ||
      schedulingFilters.isInvalidated !== undefined,
    ),
  )
  function clearHistoryFilters() {
    schedulingFilters.status = undefined
    schedulingFilters.releasedOn = ''
    schedulingFilters.isInvalidated = undefined
  }

  return {
    csv,
    downloadPlanCsv,
    historyStatus,
    historyInvalidated,
    historyStatuses,
    hasHistoryFilters,
    clearHistoryFilters,
  }
}
