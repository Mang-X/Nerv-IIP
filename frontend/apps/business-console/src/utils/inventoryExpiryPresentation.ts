import type {
  BusinessConsoleInventoryAvailabilityLineResponse,
  BusinessConsoleInventoryExpiryAlertLineResponse,
  BusinessConsoleInventoryExpiryAlertsResponse,
} from '@nerv-iip/api-client'

export type InventoryExpiryDisplayLine = BusinessConsoleInventoryAvailabilityLineResponse &
  Partial<
    Omit<
      BusinessConsoleInventoryExpiryAlertLineResponse,
      keyof BusinessConsoleInventoryAvailabilityLineResponse
    >
  >

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
  response: BusinessConsoleInventoryExpiryAlertsResponse | undefined,
  hasScope: boolean,
  hasSuccessfulResponse: boolean,
): InventoryExpirySummary {
  if (!hasScope || !hasSuccessfulResponse || !response) {
    return { alertCount: '—', expiredCount: '—', nearCount: '—', skuCount: '—' }
  }
  return {
    alertCount: response.totalCount ?? 0,
    expiredCount: response.expiredCount ?? 0,
    nearCount: response.nearExpiryCount ?? 0,
    skuCount: response.skuCount ?? 0,
  }
}

export function formatInventoryExpiryDate(value?: string | null): string {
  return value ? value.slice(0, 10) : '—'
}

export function formatInventoryExpirySource(value?: string | null): string {
  if (value === 'derived') return '系统推导'
  if (value === 'direct') return '直接录入'
  if (value === 'mixed') return '混合来源'
  return '来源未知'
}

export function formatInventoryShelfLife(value?: number | null): string {
  return value == null ? '—' : `${value} 天`
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
