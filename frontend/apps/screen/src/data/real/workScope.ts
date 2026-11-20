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

export async function warehouseOptions(session: ScreenSession, domain: keyof typeof catalogs) {
  const catalog = readScreenPayload(
    await catalogs[domain]({
      query: session,
      throwOnError: true,
      signal: AbortSignal.timeout(12000),
    }),
  )
  const items = readRows(catalog.items)
  return ['work-pool', 'site', 'self'].flatMap((kind) =>
    items.flatMap((item) =>
      item.scopeKind === kind && item.scopeId?.trim()
        ? [{ kind, id: item.scopeId, label: item.displayName || item.scopeId }]
        : [],
    ),
  )
}

export interface ScopeOption {
  kind: string
  id: string
  label: string
}

export function scopeQuery(session: ScreenSession, scope: ScopeOption) {
  return { ...session, scopeKind: scope.kind, scopeId: scope.id }
}

export async function warehouseScope(session: ScreenSession, domain: keyof typeof catalogs) {
  const [scope] = await warehouseOptions(session, domain)
  if (!scope) throw new NoWorkScope('没有已授权的仓储作业范围')
  return { query: scopeQuery(session, scope), label: scope.label }
}

export async function inspectionOptions(session: ScreenSession) {
  const context = readScreenPayload(
    await getBusinessConsolePrincipalWorkContext({
      signal: AbortSignal.timeout(12000),
      throwOnError: true,
      query: { ...session, permissionCode: 'business.quality.inspection-records.read' },
    }),
  )
  const scopes = readRows(context.authorizedScopes)
  return ['organization', 'team', 'self'].flatMap((kind) =>
    scopes.flatMap((item) =>
      item.kind === kind && item.id
        ? [{ kind, id: item.id, label: item.displayName || item.id }]
        : [],
    ),
  )
}

export async function inspectionScope(session: ScreenSession) {
  const [scope] = await inspectionOptions(session)
  if (!scope) throw new NoWorkScope('没有已授权的检验范围')
  return { query: scopeQuery(session, scope), label: scope.label }
}
