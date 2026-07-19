import type {
  BusinessConsoleInventoryAvailabilityLineResponse,
  BusinessConsoleInventoryExpiryAlertLineResponse,
} from '@nerv-iip/api-client'

export type InventoryExpiryDisplayLine = BusinessConsoleInventoryAvailabilityLineResponse &
  Partial<BusinessConsoleInventoryExpiryAlertLineResponse>

export interface InventoryExpirySummary {
  alertCount: number | string
  expiredCount: number | string
  nearCount: number | string
  skuCount: number | string
}

export function inventoryExpiryRowKey(
  line: InventoryExpiryDisplayLine,
  fallbackSkuCode: string,
): string {
  return [
    (line.skuCode ?? fallbackSkuCode) || 'sku',
    line.uomCode ?? 'uom',
    line.siteCode ?? 'site',
    inventoryLineIdentity(line, fallbackSkuCode),
    line.productionDate ?? 'production',
    line.expiryDate ?? 'expiry',
  ].join('|')
}

export function summarizeInventoryExpiryAlerts(
  alerts: BusinessConsoleInventoryExpiryAlertLineResponse[],
  hasScope: boolean,
  hasSuccessfulResponse: boolean,
): InventoryExpirySummary {
  if (!hasScope || !hasSuccessfulResponse) {
    return { alertCount: '—', expiredCount: '—', nearCount: '—', skuCount: '—' }
  }
  return {
    alertCount: alerts.length,
    expiredCount: alerts.filter((line) => line.isExpired).length,
    nearCount: alerts.filter((line) => !line.isExpired && line.isNearExpiry).length,
    skuCount: new Set(alerts.map((line) => line.skuCode)).size,
  }
}

export function formatInventoryExpiryDate(value?: string | null): string {
  return value ? value.slice(0, 10) : '—'
}

function inventoryLineIdentity(
  line: InventoryExpiryDisplayLine,
  fallbackSkuCode: string | undefined,
): string {
  return [
    (line.skuCode ?? fallbackSkuCode) || 'sku',
    line.locationCode ?? 'loc',
    line.lotNo ?? 'lot',
    line.serialNo ?? 'serial',
    line.qualityStatus ?? 'status',
    line.ownerType ?? 'owner',
    line.ownerId ?? 'id',
  ].join('|')
}
