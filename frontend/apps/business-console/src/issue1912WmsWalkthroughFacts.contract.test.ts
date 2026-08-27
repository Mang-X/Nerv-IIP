import { readFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'
import { assertWmsListQueryFacts } from '../e2e/issue1912-wms-walkthrough-facts'

const sourceDirectory = resolve(dirname(fileURLToPath(import.meta.url)), '../e2e')
const walkthroughSource = readFileSync(
  resolve(sourceDirectory, 'issue1912-real-machine-walkthrough.spec.ts'),
  'utf8',
)
const inboundPageSource = readFileSync(
  resolve(sourceDirectory, '../src/pages/wms/inbound.vue'),
  'utf8',
)
const outboundPageSource = readFileSync(
  resolve(sourceDirectory, '../src/pages/wms/outbound.vue'),
  'utf8',
)
const pagedListSource = readFileSync(
  resolve(sourceDirectory, '../src/composables/usePagedList.ts'),
  'utf8',
)

function sourceBetween(source: string, start: string, end: string): string {
  const startIndex = source.indexOf(start)
  const endIndex = source.indexOf(end, startIndex + start.length)
  if (startIndex < 0 || endIndex < 0) throw new Error(`source boundaries not found: ${start}`)
  return source.slice(startIndex, endIndex)
}

function queryString(query: Record<string, unknown>): string {
  return new URLSearchParams(
    Object.entries(query).map(([key, value]) => [key, String(value)] as [string, string]),
  ).toString()
}

describe('NERV-1571 / #1912 WMS walkthrough fact contract', () => {
  it('uses the real WMS page default page size and selects scope/site before proving the list', () => {
    const wmsQueryBuilder = sourceBetween(
      walkthroughSource,
      'const wmsListQuery =',
      'const samePageRoute =',
    )
    const inboundProof = sourceBetween(
      walkthroughSource,
      "const inboundUi = await provePageSafely('receipt-inbound-inventory'",
      "record({\n      node: 'receipt-inbound-inventory'",
    )
    const inboundQuery = sourceBetween(
      walkthroughSource,
      'const inboundListQuery =',
      "const inboundUi = await provePageSafely('receipt-inbound-inventory'",
    )
    const outboundProof = sourceBetween(
      walkthroughSource,
      "const outboundUi = await provePageSafely('delivery-wms-outbound'",
      "record({\n      node: 'delivery-wms-outbound'",
    )

    expect(pagedListSource).toContain("options.initialPageSize ?? '10'")
    expect(inboundPageSource).toContain('usePagedList(filters, {')
    expect(outboundPageSource).toContain('usePagedList(filters, {')
    expect(wmsQueryBuilder).toContain('take: 10')
    expect(wmsQueryBuilder).not.toContain('take: 100')
    expect(inboundProof).toContain('beforeFilterSelections')
    expect(inboundProof).toContain('optionCode: receiptReadSiteCode')
    expect(inboundProof).toContain('expectedRefreshListQuery')
    expect(inboundQuery).toContain('siteCode: receiptReadSiteCode')
    expect(outboundProof).toContain('beforeFilterSelections')
    expect(outboundProof).toContain('expectedRefreshListQuery')
    expect(outboundProof).toContain("forbiddenRefreshQueryKeys: ['siteCode']")
  })

  it('does not derive an expected WMS fingerprint from the first response URL', () => {
    expect(walkthroughSource).not.toContain('listQueryFingerprint(firstList.url())')
    expect(walkthroughSource).not.toContain('expectedListQueryFingerprint(firstList.url())')
  })

  it('fails closed for wrong WMS tenant, pagination, scope, or forbidden siteCode', () => {
    const expectedQuery = {
      organizationId: 'org-live',
      environmentId: 'env-live',
      scopeKind: 'work-pool',
      scopeId: 'pool-receiving-001',
      skip: 0,
      take: 10,
      siteCode: 'SITE-001',
    }
    const listPath = '/api/business-console/v1/wms/inbound-orders'
    const response = (query: string) => ({
      status: 200,
      url: `https://console.fixture${listPath}?${query}`,
    })

    expect(() =>
      assertWmsListQueryFacts(response(queryString(expectedQuery)), listPath, expectedQuery),
    ).not.toThrow()
    for (const query of [
      queryString({ ...expectedQuery, environmentId: 'env-stale' }),
      queryString({ ...expectedQuery, take: '100' }),
      queryString({ ...expectedQuery, scopeId: 'self-user-049' }),
      queryString({ ...expectedQuery, siteCode: 'SITE-002' }),
    ]) {
      expect(() => assertWmsListQueryFacts(response(query), listPath, expectedQuery)).toThrow()
    }

    const outboundPath = '/api/business-console/v1/wms/outbound-orders'
    const outboundQuery = {
      organizationId: 'org-live',
      environmentId: 'env-live',
      scopeKind: 'work-pool',
      scopeId: 'pool-shipping-001',
      skip: 0,
      take: 10,
    }
    expect(() =>
      assertWmsListQueryFacts(
        {
          status: 200,
          url: `https://console.fixture${outboundPath}?${queryString({ ...outboundQuery, siteCode: 'SITE-001' })}`,
        },
        outboundPath,
        outboundQuery,
        ['siteCode'],
      ),
    ).toThrow('must not send query field siteCode')
  })
})
