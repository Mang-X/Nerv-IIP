import {
  type WmsWalkthroughScenarioFacts,
  type WmsWalkthroughPageWindowInput,
  type WmsInboundWalkthroughScenarioFacts,
} from '../issue1912-wms-walkthrough-authority'
import { queryPath } from '../issue1912-walkthrough-query'
import { listQueryFingerprint } from '../issue1912-walkthrough-policy'

export const WMS_INBOUND_LIST_PATH = '/api/business-console/v1/wms/inbound-orders' as const
export const WMS_OUTBOUND_LIST_PATH = '/api/business-console/v1/wms/outbound-orders' as const

export type WmsInboundListPath = typeof WMS_INBOUND_LIST_PATH
export type WmsOutboundListPath = typeof WMS_OUTBOUND_LIST_PATH
export type WmsListPath = WmsInboundListPath | WmsOutboundListPath

export type WmsWorkPoolScopeFacts = Readonly<{
  scopeKind: 'work-pool'
  scopeId: string
}>

export type WmsListQueryFacts = Readonly<
  WmsWorkPoolScopeFacts & {
    organizationId: string
    environmentId: string
    skip: 0
    take: number
  }
>

export type WmsInboundListQueryFacts = Readonly<WmsListQueryFacts & { siteCode: string }>
export type WmsOutboundListQueryFacts = Readonly<WmsListQueryFacts & { siteCode?: never }>
export type WmsInboundKeywordQueryFacts = Readonly<WmsInboundListQueryFacts & { keyword: string }>
export type WmsOutboundKeywordQueryFacts = Readonly<WmsOutboundListQueryFacts & { keyword: string }>

export type WmsListResponseFacts = Readonly<{
  url: string
  status: number
}>

export type WmsInboundListQueryProof = Readonly<{
  kind: 'inbound'
  listPath: WmsInboundListPath
  selectionQuery: WmsInboundListQueryFacts
  keywordQuery: WmsInboundKeywordQueryFacts
  forbiddenQueryKeys: readonly []
}>

export type WmsOutboundListQueryProof = Readonly<{
  kind: 'outbound'
  listPath: WmsOutboundListPath
  selectionQuery: WmsOutboundListQueryFacts
  keywordQuery: WmsOutboundKeywordQueryFacts
  forbiddenQueryKeys: readonly ['siteCode']
}>

export type WmsListQueryProof = WmsInboundListQueryProof | WmsOutboundListQueryProof

const wmsProofEscapeHatches = [
  'filterResponseMode',
  'reuseCurrentRoute',
  'refreshListBeforeProof',
  'expectedListQuery',
  'listPath',
] as const

/** WMS has one proof lifecycle; generic page-proof escape hatches are rejected at runtime too. */
export function assertWmsPageProofOptions(options: object): void {
  for (const key of wmsProofEscapeHatches) {
    if (key in options) {
      throw new Error(`WMS page proof option ${key} is not allowed`)
    }
  }
}

type WmsScenarioFacts = WmsWalkthroughScenarioFacts &
  Readonly<{
    siteCode?: string
  }>

function requiredText(name: string, value: string): string {
  const normalized = value.trim()
  if (!normalized) throw new Error(`WMS scenario fact ${name} must not be empty`)
  return normalized
}

function workPoolScopeFacts(facts: WmsScenarioFacts): WmsWorkPoolScopeFacts {
  const scopeKind = requiredText('scopeKind', facts.scopeKind).toLowerCase()
  if (scopeKind !== 'work-pool') {
    throw new Error(`WMS scenario fact scopeKind must be work-pool, received ${facts.scopeKind}`)
  }
  return {
    scopeKind: 'work-pool',
    scopeId: requiredText('scopeId', facts.scopeId),
  }
}

function pageWindowFacts(facts: WmsScenarioFacts): WmsWalkthroughPageWindowInput {
  assertWmsPageWindow(facts.pageWindow)
  return facts.pageWindow
}

function listQueryFacts(facts: WmsScenarioFacts): WmsListQueryFacts {
  const scope = workPoolScopeFacts(facts)
  const pageWindow = pageWindowFacts(facts)
  const { mode: _mode, ...queryPageWindow } = pageWindow
  requiredText('keyword', facts.keyword)
  return {
    organizationId: requiredText('organizationId', facts.organizationId),
    environmentId: requiredText('environmentId', facts.environmentId),
    ...scope,
    ...queryPageWindow,
  }
}

export function buildWmsInboundListQueryFacts(
  facts: WmsInboundWalkthroughScenarioFacts,
): WmsInboundKeywordQueryFacts {
  return {
    ...listQueryFacts(facts),
    keyword: requiredText('keyword', facts.keyword),
    siteCode: requiredText('siteCode', facts.siteCode),
  }
}

export function buildWmsOutboundListQueryFacts(
  facts: WmsWalkthroughScenarioFacts,
): WmsOutboundKeywordQueryFacts {
  return {
    ...listQueryFacts(facts),
    keyword: requiredText('keyword', facts.keyword),
  }
}

/**
 * The selection-bound refresh intentionally omits the optional keyword filter. The full
 * authority vector above is used for the subsequent keyword request and its independent checks.
 */
export function buildWmsInboundSelectionQueryFacts(
  facts: WmsInboundWalkthroughScenarioFacts,
): WmsInboundListQueryFacts {
  const { keyword: _keyword, ...selectionFacts } = buildWmsInboundListQueryFacts(facts)
  return selectionFacts
}

export function buildWmsOutboundSelectionQueryFacts(
  facts: WmsWalkthroughScenarioFacts,
): WmsOutboundListQueryFacts {
  const { keyword: _keyword, ...selectionFacts } = buildWmsOutboundListQueryFacts(facts)
  return selectionFacts
}

function expectedWmsListPath(kind: WmsListQueryProof['kind']): WmsListPath {
  return kind === 'inbound' ? WMS_INBOUND_LIST_PATH : WMS_OUTBOUND_LIST_PATH
}

function assertQueryFacts(
  kind: WmsListQueryProof['kind'],
  query:
    | WmsListQueryFacts
    | WmsInboundListQueryFacts
    | WmsOutboundListQueryFacts
    | WmsInboundKeywordQueryFacts
    | WmsOutboundKeywordQueryFacts,
  expectKeyword: boolean,
): void {
  const expected = query
  for (const [name, value] of [
    ['organizationId', expected.organizationId],
    ['environmentId', expected.environmentId],
    ['scopeKind', expected.scopeKind],
    ['scopeId', expected.scopeId],
  ] as const) {
    requiredText(name, value)
  }
  if (expected.scopeKind !== 'work-pool') {
    throw new Error(`WMS scenario fact scopeKind must be work-pool, received ${expected.scopeKind}`)
  }
  if (expected.skip !== 0 || !Number.isInteger(expected.take) || expected.take <= 0) {
    throw new Error(
      `WMS scenario fact pagination must be skip=0 and a positive integer take, received skip=${expected.skip}/take=${expected.take}`,
    )
  }
  if (kind === 'inbound') {
    if (!('siteCode' in expected) || typeof expected.siteCode !== 'string') {
      throw new Error('WMS inbound proof requires siteCode fact')
    }
    requiredText('siteCode', expected.siteCode)
  } else if ('siteCode' in expected && expected.siteCode !== undefined) {
    throw new Error('WMS outbound proof must not define a siteCode fact')
  }
  if (expectKeyword) {
    if (!('keyword' in expected) || typeof expected.keyword !== 'string') {
      throw new Error('WMS proof requires keyword fact')
    }
    requiredText('keyword', expected.keyword)
  } else if ('keyword' in expected) {
    throw new Error('WMS selection query must not define a keyword fact')
  }
}

export function assertExpectedQueryFacts(
  proof: WmsListQueryProof,
): asserts proof is WmsListQueryProof {
  if (proof.listPath !== expectedWmsListPath(proof.kind)) {
    throw new Error(`WMS ${proof.kind} proof received unexpected list path ${proof.listPath}`)
  }

  const expectedForbiddenKeys = proof.kind === 'outbound' ? ['siteCode'] : []
  if (
    !Array.isArray(proof.forbiddenQueryKeys) ||
    proof.forbiddenQueryKeys.length !== expectedForbiddenKeys.length ||
    proof.forbiddenQueryKeys.some((key, index) => key !== expectedForbiddenKeys[index])
  ) {
    throw new Error(`WMS ${proof.kind} proof has an invalid forbidden query key contract`)
  }

  assertQueryFacts(proof.kind, proof.selectionQuery, false)
  assertQueryFacts(proof.kind, proof.keywordQuery, true)
  for (const key of [
    'organizationId',
    'environmentId',
    'scopeKind',
    'scopeId',
    'skip',
    'take',
  ] as const) {
    if (proof.selectionQuery[key] !== proof.keywordQuery[key]) {
      throw new Error(`WMS ${proof.kind} selection and keyword query facts must share ${key}`)
    }
  }
  if (proof.kind === 'inbound' && proof.selectionQuery.siteCode !== proof.keywordQuery.siteCode) {
    throw new Error('WMS inbound selection and keyword query facts must share siteCode')
  }
}

/**
 * Validates the public list request against the NERV-1571 facts. A 200 response alone is not
 * evidence: tenant, scope and pagination must all match, and outbound must explicitly forbid
 * siteCode. The kind/path/query/forbidden-key tuple is supplied by scenario input, never by the
 * response.
 */
export function assertWmsListQueryFacts(
  response: WmsListResponseFacts,
  proof: WmsListQueryProof,
  phase: 'selection' | 'keyword' = 'selection',
): void {
  assertExpectedQueryFacts(proof)
  if (response.status !== 200) {
    throw new Error(`WMS list ${proof.listPath} returned HTTP ${response.status}`)
  }

  const actualUrl = new URL(response.url)
  if (actualUrl.pathname !== proof.listPath) {
    throw new Error(`WMS list response path ${actualUrl.pathname} did not match ${proof.listPath}`)
  }

  for (const key of proof.forbiddenQueryKeys) {
    if (actualUrl.searchParams.has(key)) {
      throw new Error(`WMS list ${proof.listPath} must not send query field ${key}`)
    }
  }

  const expectedQuery = phase === 'selection' ? proof.selectionQuery : proof.keywordQuery
  const expectedUrl = queryPath(proof.listPath, expectedQuery)
  const expectedAbsoluteUrl = new URL(expectedUrl, 'http://walkthrough.expected').toString()
  const expectedKeyword =
    phase === 'keyword' ? requiredText('keyword', proof.keywordQuery.keyword) : undefined
  const actualKeywords = actualUrl.searchParams.getAll('keyword')
  if (expectedKeyword === undefined && actualKeywords.length > 0) {
    throw new Error(`WMS list ${proof.listPath} must not send query field keyword`)
  }
  if (
    expectedKeyword !== undefined &&
    (actualKeywords.length !== 1 || actualKeywords[0] !== expectedKeyword)
  ) {
    throw new Error(`WMS list ${proof.listPath} keyword did not match expected ${expectedKeyword}`)
  }
  if (listQueryFingerprint(actualUrl.toString()) !== listQueryFingerprint(expectedAbsoluteUrl)) {
    throw new Error(
      `WMS list ${proof.listPath} query facts did not match: expected=${new URL(expectedAbsoluteUrl).search} actual=${actualUrl.search}`,
    )
  }
}

export function assertWmsPageWindow(window: WmsWalkthroughPageWindowInput): void {
  if (
    !window ||
    (window.mode !== 'default' && window.mode !== 'selected') ||
    window.skip !== 0 ||
    !Number.isInteger(window.take) ||
    window.take <= 0
  ) {
    throw new Error(
      `WMS page window selection must declare mode default or selected, start at skip=0, and use a positive integer take, received mode=${window?.mode}/skip=${window?.skip}/take=${window?.take}`,
    )
  }
  const take = window.take as number
  if (window.mode === 'default' && take !== 10) {
    throw new Error(`WMS default page window must use take=10, received take=${take}`)
  }
}
