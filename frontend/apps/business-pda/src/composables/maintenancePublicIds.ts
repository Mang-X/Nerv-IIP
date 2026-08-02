const CANONICAL_GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const EMPTY_GUID = '00000000-0000-0000-0000-000000000000'

export function normalizeCanonicalGuid(value: unknown): string | undefined {
  if (typeof value !== 'string') return undefined
  const normalized = value.trim().toLowerCase()
  return CANONICAL_GUID.test(normalized) && normalized !== EMPTY_GUID ? normalized : undefined
}

export function normalizeMaintenanceDeviceReference(value: unknown): string {
  if (typeof value !== 'string') return ''
  return normalizeCanonicalGuid(value) ?? value.trim()
}

export function normalizeMaintenanceDeviceReferences(values: unknown): string[] {
  if (!Array.isArray(values)) return []
  const unique = new Set<string>()
  for (const value of values) {
    const normalized = normalizeMaintenanceDeviceReference(value)
    if (normalized) unique.add(normalized)
  }
  return [...unique].slice(0, 2)
}

export function serializeMaintenanceKey(value: unknown): string {
  return JSON.stringify(value)
}
