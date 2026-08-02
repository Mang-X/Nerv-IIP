import { describe, expect, it } from 'vitest'
import {
  normalizeCanonicalGuid,
  normalizeMaintenanceDeviceReference,
  normalizeMaintenanceDeviceReferences,
  normalizeMaintenancePublicReference,
  serializeMaintenanceKey,
} from './maintenancePublicIds'

describe('maintenance public identifiers', () => {
  it('keeps untyped device references Ordinal while canonicalizing only explicit strong GUIDs', () => {
    const guidShapedCode = '019F0000-0000-7000-8000-000000000001'

    expect(normalizeCanonicalGuid(guidShapedCode)).toBe(guidShapedCode.toLowerCase())
    expect(normalizeMaintenanceDeviceReference(guidShapedCode)).toBe(guidShapedCode)
    expect(
      normalizeMaintenanceDeviceReferences([
        guidShapedCode,
        guidShapedCode.toLowerCase(),
        'DEV-A',
        'dev-a',
      ]),
    ).toEqual([guidShapedCode, guidShapedCode.toLowerCase()])
    expect(normalizeMaintenanceDeviceReferences(['DEV-A', 'dev-a'])).toEqual(['DEV-A', 'dev-a'])
    expect(normalizeMaintenancePublicReference(guidShapedCode)).toBe(guidShapedCode.toLowerCase())
    expect(normalizeMaintenancePublicReference('ALM-A')).toBe('ALM-A')
    expect(normalizeMaintenancePublicReference('alm-a')).toBe('alm-a')
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
