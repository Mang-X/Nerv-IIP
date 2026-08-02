import { describe, expect, it } from 'vitest'
import {
  normalizeMaintenanceDeviceReference,
  normalizeMaintenanceDeviceReferences,
  serializeMaintenanceKey,
} from './maintenancePublicIds'

describe('maintenance public identifiers', () => {
  it('normalizes public GUIDs while preserving business-code Ordinal semantics', () => {
    const publicId = '019F0000-0000-7000-8000-000000000001'

    expect(normalizeMaintenanceDeviceReference(publicId)).toBe(publicId.toLowerCase())
    expect(
      normalizeMaintenanceDeviceReferences([publicId, publicId.toLowerCase(), 'DEV-A', 'dev-a']),
    ).toEqual([publicId.toLowerCase(), 'DEV-A'])
    expect(normalizeMaintenanceDeviceReferences(['DEV-A', 'dev-a'])).toEqual(['DEV-A', 'dev-a'])
  })

  it('keeps delimiter-bearing structured identities collision-free', () => {
    expect(serializeMaintenanceKey(['org:a', 'env', 'principal'])).not.toBe(
      serializeMaintenanceKey(['org', 'a:env', 'principal']),
    )
    expect(serializeMaintenanceKey({ users: ['user,a'], teams: ['b'] })).not.toBe(
      serializeMaintenanceKey({ users: ['user', 'a'], teams: ['b'] }),
    )
  })
})
