import type { BusinessConsoleWmsWorkScopeCatalogItem } from '@nerv-iip/api-client'
import { formatWorkScopeKey } from '../scope/workScopeKey'

const supportedKinds = new Set(['self', 'work-pool', 'site'])

export type WmsWorkScopeCatalogKind = 'receipts' | 'shipments' | 'counts'

export interface WmsWorkScopeOption {
  label: string
  value: string
}

export function toWmsWorkScopeOptions(
  items: readonly BusinessConsoleWmsWorkScopeCatalogItem[],
): WmsWorkScopeOption[] {
  const options: WmsWorkScopeOption[] = []
  for (const item of items) {
    const kind = item.scopeKind?.trim() ?? ''
    const id = item.scopeId?.trim() ?? ''
    if (id && supportedKinds.has(kind)) {
      options.push({
        label: kind === 'self' ? '我的任务' : (item.displayName?.trim() ?? id),
        value: formatWorkScopeKey(kind, id),
      })
    }
  }
  return options
}
