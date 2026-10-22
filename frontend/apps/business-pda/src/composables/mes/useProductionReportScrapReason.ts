import { computed, ref, type Ref } from 'vue'
import { useMesScrapReasonCodes } from '@/composables/useBusinessMes'

export function useProductionReportScrapReason(scrapQuantity: Readonly<Ref<number>>) {
  const scrapReasonCode = ref('')
  const state = useMesScrapReasonCodes(() => scrapQuantity.value > 0)

  const invalidScrapReasonCode = computed(() => {
    if (scrapQuantity.value <= 0) return false
    if (!state.qualityInspectionRecordsReadPermission.value) return true
    if (state.scrapReasonCodesPending.value || state.scrapReasonCodesError.value) return true
    return (
      !scrapReasonCode.value.trim() ||
      !state.scrapReasonCodes.value.some(
        (row) => row.reasonCode?.trim() === scrapReasonCode.value.trim() && row.enabled !== false,
      )
    )
  })

  const scrapReasonValidationMessage = computed(() => {
    if (scrapQuantity.value <= 0) return ''
    if (!state.qualityInspectionRecordsReadPermission.value) {
      return '当前账号没有质量原因码读取权限，无法提交报废报工。'
    }
    if (state.scrapReasonCodesPending.value) return '正在读取报废原因码，请稍后重试。'
    if (state.scrapReasonCodesError.value) return '报废原因码读取失败，请刷新后重试。'
    if (state.scrapReasonCodes.value.length === 0) return '当前没有可用的报废原因码。'
    if (!scrapReasonCode.value.trim()) return '报废数量大于 0 时必须选择报废原因码。'
    return '所选报废原因码已失效，请重新选择。'
  })

  return {
    ...state,
    scrapReasonCode,
    invalidScrapReasonCode,
    scrapReasonValidationMessage,
  }
}
