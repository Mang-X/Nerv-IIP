import { useInventoryExpiryAlerts, type InventoryAvailabilityFilters } from './useBusinessInventory'
import { notifyError } from '@/utils/notify'
import { summarizeInventoryExpiryAlerts } from '@/utils/inventoryExpiryPresentation'
import { computed, shallowRef, watch } from 'vue'

type ExpirySourceFilters = Pick<
  InventoryAvailabilityFilters,
  | 'siteCode'
  | 'skuCode'
  | 'uomCode'
  | 'locationCode'
  | 'lotNo'
  | 'serialNo'
  | 'qualityStatus'
  | 'ownerType'
  | 'ownerId'
>

export function useInventoryExpiryView(sourceFilters: ExpirySourceFilters) {
  const nearExpiryOnly = shallowRef(false)
  const query = useInventoryExpiryAlerts(() => nearExpiryOnly.value)

  watch(
    () => [sourceFilters.siteCode, sourceFilters.skuCode, sourceFilters.locationCode] as const,
    ([siteCode, skuCode, locationCode]) => {
      query.filters.siteCode = siteCode
      query.filters.skuCode = skuCode || undefined
      query.filters.locationCode = locationCode || undefined
    },
    { immediate: true },
  )
  watch(query.expiryAlertsError, (error) => {
    if (!error || !nearExpiryOnly.value) return
    const message = errorMessage(error)
    const fallback = /more than \d+ ledger lines|add sku or location/i.test(message)
      ? '效期预警范围过大，请添加 SKU 或库位后重试。'
      : '近效期批次加载失败，请稍后重试。'
    notifyError(error, fallback)
  })

  const hasExpiryScope = computed(() => sourceFilters.siteCode.trim().length > 0)
  const visibleExpiryAlerts = computed(() =>
    hasExpiryScope.value && query.expiryAlertsSuccessful.value ? query.expiryAlerts.value : [],
  )
  const expirySummary = computed(() =>
    summarizeInventoryExpiryAlerts(
      query.expiryAlerts.value,
      hasExpiryScope.value,
      query.expiryAlertsSuccessful.value,
    ),
  )

  function toggleNearExpiryView() {
    if (!nearExpiryOnly.value) {
      sourceFilters.uomCode = ''
      sourceFilters.lotNo = undefined
      sourceFilters.serialNo = undefined
      sourceFilters.qualityStatus = undefined
      sourceFilters.ownerType = undefined
      sourceFilters.ownerId = undefined
    }
    nearExpiryOnly.value = !nearExpiryOnly.value
  }

  return {
    ...query,
    nearExpiryOnly,
    hasExpiryScope,
    visibleExpiryAlerts,
    expirySummary,
    toggleNearExpiryView,
  }
}

function errorMessage(error: unknown): string {
  if (error instanceof Error) return error.message
  if (typeof error === 'object' && error !== null && 'message' in error) {
    const message = (error as { message?: unknown }).message
    return typeof message === 'string' ? message : ''
  }
  return ''
}
