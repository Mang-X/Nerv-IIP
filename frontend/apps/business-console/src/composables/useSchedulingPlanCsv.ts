import { exportBusinessConsoleSchedulingPlanCsv } from '@nerv-iip/api-client'
import { ref } from 'vue'
import type { SchedulingPlanSelection } from './useBusinessScheduling'
import { hasBusinessContext } from './businessContextBinding'

export function useSchedulingPlanCsv() {
  const pending = ref(false)
  async function download(selection: SchedulingPlanSelection) {
    const { planId, organizationId, environmentId } = selection
    if (!hasBusinessContext(selection) || !planId) throw new Error('请先选择组织、环境及排程方案。')
    pending.value = true
    try {
      const { data } = await exportBusinessConsoleSchedulingPlanCsv({
        path: { planId },
        query: { organizationId, environmentId },
        throwOnError: true,
      })
      const url = URL.createObjectURL(data!)
      const link = document.createElement('a')
      link.href = url
      link.download = `schedule-${planId}.csv`
      link.click()
      URL.revokeObjectURL(url)
    } finally {
      pending.value = false
    }
  }
  return { download, pending }
}
