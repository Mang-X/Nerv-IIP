import { describe, expect, it } from 'vitest'

import {
  assertWmsListQueryFacts,
  assertWmsInitialListResponse,
  assertWmsInboundSelectionMatchesQuery,
  assertWmsInboundPageSelection,
  assertWmsOutboundPageSelection,
  assertWmsPageSelection,
  buildWmsInboundListQueryFacts,
  buildWmsOutboundListQueryFacts,
  type WmsInboundListQueryFacts,
  type WmsOutboundListQueryFacts,
} from '../e2e/issue1912-wms-walkthrough-facts'
import { queryPath } from '../e2e/issue1912-walkthrough-query'
import {
  NERV_1571_WMS_INBOUND_FACTS,
  NERV_1571_WMS_INBOUND_QUERY_FACTS,
  NERV_1571_WMS_OUTBOUND_FACTS,
  NERV_1571_WMS_OUTBOUND_QUERY_FACTS,
} from '../e2e/issue1912-wms-walkthrough-authority'

const inboundPath = '/api/business-console/v1/wms/inbound-orders'
const outboundPath = '/api/business-console/v1/wms/outbound-orders'

// The expected values below come from docs/architecture/nerv-1571-wms-walkthrough-facts.md §场景事实
// (Linear NERV-1571 / GitHub #1912), not from a page response or the implementation under test.

function response(path: string, query: Record<string, string | number>, status = 200) {
  return {
    status,
    url: `https://console.fixture${queryPath(path, query, 'https://console.fixture')}`,
  }
}

describe('NERV-1571 / #1912 WMS walkthrough fact contract', () => {
  it('maps independent scenario facts to the documented query vector', () => {
    const inbound: WmsInboundListQueryFacts = buildWmsInboundListQueryFacts(
      NERV_1571_WMS_INBOUND_FACTS,
    )
    const outbound: WmsOutboundListQueryFacts = buildWmsOutboundListQueryFacts(
      NERV_1571_WMS_OUTBOUND_FACTS,
    )

    expect(inbound).toEqual(NERV_1571_WMS_INBOUND_QUERY_FACTS)
    expect(outbound).toEqual(NERV_1571_WMS_OUTBOUND_QUERY_FACTS)
  })

  it('fails closed for every tenant, scope, pagination, path, status, and site mutation', () => {
    const expected: WmsInboundListQueryFacts = NERV_1571_WMS_INBOUND_QUERY_FACTS
    type QueryMutation = Partial<Record<keyof typeof expected, string | number>>
    const actual = (overrides: QueryMutation = {}, status = 200) =>
      response(inboundPath, { ...expected, ...overrides }, status)

    expect(() => assertWmsListQueryFacts(actual(), inboundPath, expected)).not.toThrow()

    const mutations: Array<[string, Parameters<typeof assertWmsListQueryFacts>[0], string]> = [
      ['organizationId', actual({ organizationId: 'org-other' }), 'query facts'],
      ['environmentId', actual({ environmentId: 'env-stale' }), 'query facts'],
      ['scopeKind', actual({ scopeKind: 'self' }), 'query facts'],
      ['scopeId', actual({ scopeId: 'self-user-049' }), 'query facts'],
      ['skip', actual({ skip: 10 }), 'query facts'],
      ['take', actual({ take: 100 }), 'query facts'],
      ['siteCode', actual({ siteCode: 'SITE-002' }), 'query facts'],
      ['path', actual(), 'response path'],
      ['status', actual(), 'HTTP 503'],
    ]
    mutations[7][1] = response('/api/business-console/v1/wms/outbound-orders', expected)
    mutations[8][1] = actual({}, 503)

    for (const [mutation, mutatedResponse, expectedMessage] of mutations) {
      expect(
        () => assertWmsListQueryFacts(mutatedResponse, inboundPath, expected),
        `${mutation} mutation must fail closed`,
      ).toThrow(expectedMessage)
    }

    const missingSite: Record<string, string | number> = { ...expected }
    delete missingSite.siteCode
    expect(() =>
      assertWmsListQueryFacts(response(inboundPath, missingSite), inboundPath, expected),
    ).toThrow('query facts')

    expect(() =>
      assertWmsListQueryFacts(
        response(outboundPath, { ...NERV_1571_WMS_OUTBOUND_QUERY_FACTS, siteCode: 'SITE-001' }),
        outboundPath,
        NERV_1571_WMS_OUTBOUND_QUERY_FACTS,
        ['siteCode'],
      ),
    ).toThrow('must not send query field siteCode')
  })

  it('keeps the first WMS list response public but does not accept a wrong path or status', () => {
    const expected: WmsInboundListQueryFacts = NERV_1571_WMS_INBOUND_QUERY_FACTS

    expect(() =>
      assertWmsInitialListResponse(response(inboundPath, expected), inboundPath),
    ).not.toThrow()
    expect(() =>
      assertWmsInitialListResponse(response(outboundPath, expected), inboundPath),
    ).toThrow('response path')
    expect(() =>
      assertWmsInitialListResponse(response(inboundPath, expected, 503), inboundPath),
    ).toThrow('HTTP 503')
  })

  it('requires a non-empty discriminated selection and rejects neither or both facts', () => {
    expect(() => assertWmsPageSelection({ label: '作业范围', option: '收货作业池' })).not.toThrow()
    expect(() => assertWmsPageSelection({ label: '工厂', optionCode: 'SITE-001' })).not.toThrow()
    expect(() => assertWmsPageSelection({ label: '作业范围', option: '' })).toThrow('exactly one')
    expect(() => assertWmsPageSelection({ label: '工厂', optionCode: '' })).toThrow('exactly one')
    expect(() =>
      assertWmsPageSelection({ label: '工厂', option: '', optionCode: 'SITE-001' } as never),
    ).toThrow('exactly one')
    expect(() => assertWmsPageSelection({ label: '工厂' } as never)).toThrow('exactly one')
    expect(() => assertWmsPageSelection({ label: '', option: '工厂' })).toThrow('label')

    expect(() =>
      assertWmsInboundPageSelection({
        scope: { label: '作业范围', option: '收货作业池' },
      } as never),
    ).toThrow('exactly one scope and one site')
    expect(() =>
      assertWmsInboundPageSelection({
        scope: { label: '作业范围', option: '收货作业池', optionCode: 'POOL-001' },
        site: { label: '工厂', optionCode: 'SITE-001' },
      } as never),
    ).toThrow('exactly one')
    expect(() => assertWmsOutboundPageSelection({} as never)).toThrow('scope selection')

    const expected: WmsInboundListQueryFacts = NERV_1571_WMS_INBOUND_QUERY_FACTS
    expect(() =>
      assertWmsInboundSelectionMatchesQuery(
        {
          scope: { label: '作业范围', option: '收货作业池' },
          site: { label: '工厂', optionCode: 'SITE-002' },
        },
        expected,
      ),
    ).toThrow('did not match expected siteCode')
  })

  it('rejects empty scenario facts before a request can be proved', () => {
    expect(() =>
      buildWmsInboundListQueryFacts({ ...NERV_1571_WMS_INBOUND_FACTS, organizationId: '' }),
    ).toThrow('organizationId')
    expect(() =>
      buildWmsInboundListQueryFacts({ ...NERV_1571_WMS_INBOUND_FACTS, scopeId: '' }),
    ).toThrow('scopeId')
    expect(() =>
      buildWmsInboundListQueryFacts({ ...NERV_1571_WMS_INBOUND_FACTS, environmentId: '' }),
    ).toThrow('environmentId')
    expect(() =>
      buildWmsInboundListQueryFacts({ ...NERV_1571_WMS_INBOUND_FACTS, siteCode: '' }),
    ).toThrow('siteCode')
    expect(() =>
      buildWmsOutboundListQueryFacts({ ...NERV_1571_WMS_OUTBOUND_FACTS, scopeKind: 'self' }),
    ).toThrow('scopeKind')
  })
})
