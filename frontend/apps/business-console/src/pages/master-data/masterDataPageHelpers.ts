import type { BusinessConsoleResourceItem } from '@nerv-iip/api-client'

export type PartnerRole = 'all' | 'customer' | 'supplier' | 'unknown'

export function inferPartnerRole(row: BusinessConsoleResourceItem): PartnerRole {
  // Temporary fallback until BusinessGateway returns an explicit partner role field.
  const code = row.code?.toLowerCase() ?? ''
  if (code.includes('cust')) return 'customer'
  if (code.includes('sup')) return 'supplier'
  return 'unknown'
}

export function roleLabel(role: PartnerRole) {
  if (role === 'customer') return '客户'
  if (role === 'supplier') return '供应商'
  return '未分配'
}
