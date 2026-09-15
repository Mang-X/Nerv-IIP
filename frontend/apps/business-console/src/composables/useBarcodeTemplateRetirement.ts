import {
  getBusinessConsoleBarcodeTemplateAssetRetirement,
  retireBusinessConsoleBarcodeTemplateAsset,
  type GetBusinessConsoleBarcodeTemplateAssetRetirementData,
  type RetireBusinessConsoleBarcodeTemplateAssetData,
  type BusinessConsoleBarcodeTemplateAssetRetirement,
} from '@nerv-iip/api-client'
import { computed, shallowRef } from 'vue'

export function useBarcodeTemplateRetirement(
  query: GetBusinessConsoleBarcodeTemplateAssetRetirementData['query'],
) {
  const context = shallowRef<BusinessConsoleBarcodeTemplateAssetRetirement>()
  const busy = shallowRef(false)
  const accepted = shallowRef(false)
  const submitted = shallowRef<RetireBusinessConsoleBarcodeTemplateAssetData['body']>()
  const canSubmit = computed(
    () =>
      !!context.value?.checksum &&
      !context.value.decisionId &&
      !context.value.status &&
      !accepted.value &&
      !busy.value,
  )

  async function refresh() {
    if (busy.value) return
    busy.value = true
    try {
      const { data } = await getBusinessConsoleBarcodeTemplateAssetRetirement({
        query,
        throwOnError: true,
      })
      if (!data.success || !data.data) throw new Error('退役状态读取未完成。')
      context.value = data.data
    } finally {
      busy.value = false
    }
  }

  async function submit(reason: string) {
    if (!canSubmit.value) return false
    submitted.value ??= {
      ...query,
      checksum: context.value!.checksum!,
      reason: reason.trim(),
      idempotencyKey: crypto.randomUUID(),
    }
    busy.value = true
    try {
      const { data } = await retireBusinessConsoleBarcodeTemplateAsset({
        body: submitted.value,
        throwOnError: true,
      })
      if (!data.success || !data.data?.decisionId) throw new Error('退役请求尚未确认。')
      accepted.value = true
      return true
    } finally {
      busy.value = false
    }
  }

  return { context, busy, accepted, submitted, canSubmit, refresh, submit }
}
