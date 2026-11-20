import {
  getBusinessConsolePrincipalWorkContext,
  getBusinessConsoleWmsCountWorkScopes,
  getBusinessConsoleWmsReceiptWorkScopes,
  getBusinessConsoleWmsShipmentWorkScopes,
} from '@nerv-iip/api-client'
import { NoWorkScope } from './readError'
import type { ScreenSession } from '../session'
import { readRows, readScreenPayload } from './response'

const catalogs = {
  receipts: getBusinessConsoleWmsReceiptWorkScopes,
  shipments: getBusinessConsoleWmsShipmentWorkScopes,
  counts: getBusinessConsoleWmsCountWorkScopes,
}

export async function warehouseScope(session: ScreenSession, domain: keyof typeof catalogs) {
  const catalog = readScreenPayload(
    await catalogs[domain]({
      query: session,
      throwOnError: true,
      signal: AbortSignal.timeout(12000),
    }),
  )
  const items = readRows(catalog.items)
  for (const scopeKind of ['work-pool', 'site', 'self']) {
    const scope = items.find((item) => item.scopeKind === scopeKind && item.scopeId?.trim())
    if (scope?.scopeId)
      return {
        query: { ...session, scopeKind, scopeId: scope.scopeId },
        label: scope.displayName || scope.scopeId,
      }
  }
  throw new NoWorkScope('没有已授权的仓储作业范围')
}

export async function inspectionScope(session: ScreenSession) {
  const context = readScreenPayload(
    await getBusinessConsolePrincipalWorkContext({
      signal: AbortSignal.timeout(12000),
      throwOnError: true,
      query: { ...session, permissionCode: 'business.quality.inspection-records.read' },
    }),
  )
  const scopes = readRows(context.authorizedScopes)
  for (const kind of ['organization', 'team', 'self']) {
    const scope = scopes.find((item) => item.kind === kind && item.id)
    if (scope?.id)
      return {
        query: { ...session, scopeKind: kind, scopeId: scope.id },
        label: scope.displayName || scope.id,
      }
  }
  throw new NoWorkScope('没有已授权的检验范围')
}
