import { describe, expect, it } from 'vitest'
import { inferPartnerRole, roleLabel } from './masterDataPageHelpers'

describe('master data page helpers', () => {
  it('uses an explicit unknown partner role instead of reusing the all filter value', () => {
    expect(inferPartnerRole({ resourceType: 'business-partner', code: 'CUST-GAC' })).toBe('customer')
    expect(inferPartnerRole({ resourceType: 'business-partner', code: 'SUP-OIL-CC' })).toBe('supplier')
    expect(inferPartnerRole({ resourceType: 'business-partner', code: 'OEM-001' })).toBe('unknown')
    expect(roleLabel('unknown')).toBe('未分配')
  })
})
