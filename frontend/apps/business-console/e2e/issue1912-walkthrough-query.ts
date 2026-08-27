export type WalkthroughQuery = Readonly<Record<string, unknown>>

/**
 * Canonical query serializer for the #1912 walkthrough.
 * Empty values are omitted and all other values use URLSearchParams' string form.
 */
export function queryPath(
  path: string,
  query: WalkthroughQuery,
  baseUrl = 'http://walkthrough.expected',
): string {
  const url = new URL(path, baseUrl)
  for (const [key, value] of Object.entries(query)) {
    if (value !== null && value !== undefined && value !== '') {
      url.searchParams.set(key, String(value))
    }
  }
  return `${url.pathname}${url.search}`
}
