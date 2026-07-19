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
  const availabilityOnlySnapshot =
    shallowRef<
      Pick<
        ExpirySourceFilters,
        'uomCode' | 'lotNo' | 'serialNo' | 'qualityStatus' | 'ownerType' | 'ownerId'
      >
    >()
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
    notifyError(error, '近效期批次加载失败，请稍后重试。')
  })

  const hasExpirySite = computed(() => query.filters.siteCode.trim().length > 0)
  const hasExpiryScope = computed(
    () =>
      query.filters.organizationId.trim().length > 0 &&
      query.filters.environmentId.trim().length > 0 &&
      query.filters.siteCode.trim().length > 0,
  )
  const visibleExpiryAlerts = computed(() =>
    hasExpiryScope.value && query.expiryAlertsSuccessful.value ? query.expiryAlerts.value : [],
  )
  const expirySummary = computed(() =>
    summarizeInventoryExpiryAlerts(
      query.expiryAlertsResponse.value,
      hasExpiryScope.value,
      query.expiryAlertsSuccessful.value,
    ),
  )

  function toggleNearExpiryView() {
    if (!nearExpiryOnly.value) {
      availabilityOnlySnapshot.value = {
        uomCode: sourceFilters.uomCode,
        lotNo: sourceFilters.lotNo,
        serialNo: sourceFilters.serialNo,
        qualityStatus: sourceFilters.qualityStatus,
        ownerType: sourceFilters.ownerType,
        ownerId: sourceFilters.ownerId,
      }
      sourceFilters.uomCode = ''
      sourceFilters.lotNo = undefined
      sourceFilters.serialNo = undefined
      sourceFilters.qualityStatus = undefined
      sourceFilters.ownerType = undefined
      sourceFilters.ownerId = undefined
    } else if (availabilityOnlySnapshot.value) {
      Object.assign(sourceFilters, availabilityOnlySnapshot.value)
      availabilityOnlySnapshot.value = undefined
    }
    nearExpiryOnly.value = !nearExpiryOnly.value
  }

  return {
    ...query,
    nearExpiryOnly,
    hasExpirySite,
    hasExpiryScope,
    visibleExpiryAlerts,
    expirySummary,
    toggleNearExpiryView,
  }
}
