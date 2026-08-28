import { describe, expect, it } from 'vitest'

import type { WalkthroughQuery } from '../e2e/issue1912-walkthrough-query'
import { queryPath } from '../e2e/issue1912-walkthrough-query'
import {
  assertWmsInboundPageSelection,
  assertWmsInboundSelectionMatchesQuery,
  assertWmsInitialListResponse,
  assertWmsListQueryFacts,
  assertWmsPageProofOptions,
  assertWmsOutboundPageSelection,
  assertWmsOutboundSelectionMatchesQuery,
  assertWmsPageSelection,
  buildWmsInboundListQueryFacts,
  buildWmsInboundSelectionQueryFacts,
  buildWmsOutboundListQueryFacts,
  buildWmsOutboundSelectionQueryFacts,
  type WmsInboundListQueryProof,
  type WmsInboundKeywordQueryFacts,
  type WmsOutboundListQueryProof,
  type WmsOutboundKeywordQueryFacts,
} from '../e2e/issue1912-wms-walkthrough-facts'
import {
  NERV_1571_WMS_INBOUND_FACTS,
  NERV_1571_WMS_INBOUND_QUERY_FACTS,
  NERV_1571_WMS_OUTBOUND_FACTS,
  NERV_1571_WMS_OUTBOUND_QUERY_FACTS,
} from '../e2e/issue1912-wms-walkthrough-authority'

const inboundPath = '/api/business-console/v1/wms/inbound-orders'
const outboundPath = '/api/business-console/v1/wms/outbound-orders'

// 预期值来自 NERV-1571 的独立场景输入：显式 pageWindow、页面选择后的 SITE-001 和作业池。
// 它们先于响应生成，并不是当前实现源码或响应 URL 的回读。

function response(path: string, query: WalkthroughQuery, status = 200) {
  return {
    status,
    url: `https://console.fixture${queryPath(path, query, 'https://console.fixture')}`,
  }
}

function inboundProof(
  keywordQuery: WmsInboundKeywordQueryFacts = NERV_1571_WMS_INBOUND_QUERY_FACTS,
): WmsInboundListQueryProof {
  const { keyword: _keyword, ...selectionQuery } = keywordQuery
  return {
    kind: 'inbound',
    listPath: inboundPath,
    selectionQuery,
    keywordQuery,
    forbiddenQueryKeys: [],
  }
}

function outboundProof(
  keywordQuery: WmsOutboundKeywordQueryFacts = NERV_1571_WMS_OUTBOUND_QUERY_FACTS,
): WmsOutboundListQueryProof {
  const { keyword: _keyword, ...selectionQuery } = keywordQuery
  return {
    kind: 'outbound',
    listPath: outboundPath,
    selectionQuery,
    keywordQuery,
    forbiddenQueryKeys: ['siteCode'],
  }
}

describe('NERV-1571 / #1912 WMS walkthrough fact contract', () => {
  it('maps independent scenario facts to the documented query vector', () => {
    expect(buildWmsInboundListQueryFacts(NERV_1571_WMS_INBOUND_FACTS)).toEqual(
      NERV_1571_WMS_INBOUND_QUERY_FACTS,
    )
    expect(buildWmsOutboundListQueryFacts(NERV_1571_WMS_OUTBOUND_FACTS)).toEqual(
      NERV_1571_WMS_OUTBOUND_QUERY_FACTS,
    )

    expect(buildWmsInboundSelectionQueryFacts(NERV_1571_WMS_INBOUND_FACTS)).toEqual({
      organizationId: 'org-live',
      environmentId: 'env-live',
      scopeKind: 'work-pool',
      scopeId: 'pool-receiving-001',
      skip: 0,
      take: 10,
      siteCode: 'SITE-001',
    })
    expect(buildWmsOutboundSelectionQueryFacts(NERV_1571_WMS_OUTBOUND_FACTS)).toEqual({
      organizationId: 'org-live',
      environmentId: 'env-live',
      scopeKind: 'work-pool',
      scopeId: 'pool-shipping-001',
      skip: 0,
      take: 10,
    })
  })

  it('uses the explicit scenario page window instead of a page implementation default', () => {
    const scenario = {
      ...NERV_1571_WMS_OUTBOUND_FACTS,
      pageWindow: { skip: 0 as const, take: 20 },
    }

    expect(buildWmsOutboundListQueryFacts(scenario)).toMatchObject({
      skip: 0,
      take: 20,
    })
    expect(() =>
      assertWmsListQueryFacts(
        response(outboundPath, {
          ...NERV_1571_WMS_OUTBOUND_QUERY_FACTS,
          take: 10,
        }),
        outboundProof(buildWmsOutboundListQueryFacts(scenario)),
        'keyword',
      ),
    ).toThrow('query facts')
  })

  it('fails closed for same-keyword tenant, environment, scope, pagination, site, path, and status mutations', () => {
    const expected = NERV_1571_WMS_INBOUND_QUERY_FACTS
    const proof = inboundProof(expected)
    const { keyword: _keyword, ...selectionExpected } = expected
    const actual = (overrides: WalkthroughQuery = {}, status = 200) =>
      response(inboundPath, { ...selectionExpected, ...overrides }, status)
    const keywordActual = (overrides: WalkthroughQuery = {}, status = 200) =>
      response(inboundPath, { ...expected, ...overrides }, status)

    expect(() => assertWmsListQueryFacts(actual(), proof)).not.toThrow()
    expect(() => assertWmsListQueryFacts(keywordActual(), proof, 'keyword')).not.toThrow()

    const mutations: Array<[string, ReturnType<typeof actual>, string]> = [
      ['organizationId', actual({ organizationId: 'org-other' }), 'query facts'],
      ['environmentId', actual({ environmentId: 'env-stale' }), 'query facts'],
      ['scopeKind', actual({ scopeKind: 'self' }), 'query facts'],
      ['scopeId', actual({ scopeId: 'self-user-049' }), 'query facts'],
      ['skip', actual({ skip: 10 }), 'query facts'],
      ['take', actual({ take: 100 }), 'query facts'],
      ['keyword', keywordActual({ keyword: 'IN-WALK-002' }), 'keyword'],
      ['siteCode', actual({ siteCode: 'SITE-002' }), 'query facts'],
      ['path', response(outboundPath, expected), 'response path'],
      ['status', actual({}, 503), 'HTTP 503'],
    ]

    for (const [mutation, mutatedResponse, expectedMessage] of mutations) {
      expect(
        () => assertWmsListQueryFacts(mutatedResponse, proof),
        `${mutation} mutation must fail closed`,
      ).toThrow(expectedMessage)
    }

    const keywordMutations: Array<[string, ReturnType<typeof keywordActual>]> = [
      ['organizationId', keywordActual({ organizationId: 'org-other' })],
      ['environmentId', keywordActual({ environmentId: 'env-stale' })],
      ['scopeKind', keywordActual({ scopeKind: 'self' })],
      ['scopeId', keywordActual({ scopeId: 'self-user-049' })],
      ['skip', keywordActual({ skip: 10 })],
      ['take', keywordActual({ take: 100 })],
      ['siteCode', keywordActual({ siteCode: 'SITE-002' })],
    ]
    for (const [mutation, mutatedResponse] of keywordMutations) {
      expect(
        () => assertWmsListQueryFacts(mutatedResponse, proof, 'keyword'),
        `${mutation} mutation must fail closed in the keyword action proof`,
      ).toThrow(/query facts/)
    }

    const missingSite: WalkthroughQuery = { ...selectionExpected }
    delete (missingSite as Record<string, unknown>).siteCode
    expect(() => assertWmsListQueryFacts(response(inboundPath, missingSite), proof)).toThrow(
      'query facts',
    )

    const missingKeyword: WalkthroughQuery = { ...expected }
    delete (missingKeyword as Record<string, unknown>).keyword
    expect(() =>
      assertWmsListQueryFacts(response(inboundPath, missingKeyword), proof, 'keyword'),
    ).toThrow('keyword')

    const duplicateKeyword = new URL(response(inboundPath, expected).url)
    duplicateKeyword.searchParams.append('keyword', expected.keyword)
    expect(() =>
      assertWmsListQueryFacts({ status: 200, url: duplicateKeyword.toString() }, proof, 'keyword'),
    ).toThrow('keyword')
  })

  it('binds outbound path, query, and forbidden siteCode as one proof contract', () => {
    const expected = NERV_1571_WMS_OUTBOUND_QUERY_FACTS
    const proof = outboundProof(expected)
    expect(() =>
      assertWmsListQueryFacts(response(outboundPath, expected), proof, 'keyword'),
    ).not.toThrow()
    const { keyword: _keyword, ...selectionExpected } = expected
    expect(() =>
      assertWmsListQueryFacts(response(outboundPath, selectionExpected), proof),
    ).not.toThrow()
    expect(() =>
      assertWmsListQueryFacts(
        response(outboundPath, { ...selectionExpected, siteCode: 'SITE-001' }),
        proof,
      ),
    ).toThrow('must not send query field siteCode')

    expect(() =>
      assertWmsListQueryFacts(response(inboundPath, NERV_1571_WMS_INBOUND_QUERY_FACTS), {
        kind: 'outbound',
        listPath: inboundPath,
        selectionQuery: selectionExpected,
        keywordQuery: expected,
        forbiddenQueryKeys: ['siteCode'],
      } as never),
    ).toThrow('unexpected list path')
  })

  it('keeps the first WMS list response public but rejects a wrong path or status immediately', () => {
    const expected = NERV_1571_WMS_INBOUND_QUERY_FACTS

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
      assertWmsOutboundPageSelection({
        scope: {
          label: '作业范围',
          option: '发货作业池',
          scopeKind: 'work-pool',
          scopeId: '',
        },
      }),
    ).toThrow('scopeId')

    expect(() =>
      assertWmsInboundPageSelection({
        scope: {
          label: '作业范围',
          option: '收货作业池',
          scopeKind: 'work-pool',
          scopeId: 'pool-receiving-001',
        },
      } as never),
    ).toThrow('exactly one scope and one site')
    expect(() =>
      assertWmsInboundPageSelection({
        scope: {
          label: '作业范围',
          option: '收货作业池',
          scopeKind: 'work-pool',
          scopeId: 'pool-receiving-001',
          optionCode: 'POOL-001',
        },
        site: { label: '工厂', optionCode: 'SITE-001' },
      } as never),
    ).toThrow('exactly one')
    expect(() => assertWmsOutboundPageSelection({} as never)).toThrow('scope selection')
    expect(() =>
      assertWmsOutboundPageSelection({
        scope: {
          label: '作业范围',
          option: '发货作业池',
          scopeKind: 'self',
          scopeId: 'pool-shipping-001',
        },
      } as never),
    ).toThrow('scopeKind')

    expect(() =>
      assertWmsInboundSelectionMatchesQuery(
        {
          scope: {
            label: '作业范围',
            option: '收货作业池',
            scopeKind: 'work-pool',
            scopeId: 'pool-receiving-001',
          },
          site: { label: '工厂', optionCode: 'SITE-002' },
        },
        NERV_1571_WMS_INBOUND_QUERY_FACTS,
      ),
    ).toThrow('did not match expected siteCode')
    expect(() =>
      assertWmsInboundSelectionMatchesQuery(
        {
          scope: {
            label: '作业范围',
            option: '收货作业池',
            scopeKind: 'work-pool',
            scopeId: 'pool-other-001',
          },
          site: { label: '工厂', optionCode: 'SITE-001' },
        },
        NERV_1571_WMS_INBOUND_QUERY_FACTS,
      ),
    ).toThrow('did not match expected scopeId')
    expect(() =>
      assertWmsOutboundSelectionMatchesQuery(
        {
          scope: {
            label: '作业范围',
            option: '发货作业池',
            scopeKind: 'work-pool',
            scopeId: 'pool-other-001',
          },
        },
        NERV_1571_WMS_OUTBOUND_QUERY_FACTS,
      ),
    ).toThrow('did not match expected scopeId')
  })

  it('rejects generic proof escape hatches at the WMS wrapper boundary', () => {
    for (const key of [
      'filterResponseMode',
      'reuseCurrentRoute',
      'refreshListBeforeProof',
      'expectedListQuery',
      'listPath',
    ]) {
      expect(() => assertWmsPageProofOptions({ [key]: true })).toThrow(
        `option ${key} is not allowed`,
      )
    }
  })

  it('rejects empty authority facts before a request can be proved', () => {
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
      buildWmsInboundListQueryFacts({ ...NERV_1571_WMS_INBOUND_FACTS, keyword: '' }),
    ).toThrow('keyword')
    expect(() =>
      buildWmsInboundListQueryFacts({ ...NERV_1571_WMS_INBOUND_FACTS, siteCode: '' }),
    ).toThrow('siteCode')
    expect(() =>
      buildWmsOutboundListQueryFacts({ ...NERV_1571_WMS_OUTBOUND_FACTS, scopeKind: 'self' }),
    ).toThrow('scopeKind')
  })
})
