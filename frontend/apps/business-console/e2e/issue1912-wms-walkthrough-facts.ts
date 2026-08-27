import { expect, type Page, type Request, type Response } from '@playwright/test'
import { formatWorkScopeKey } from '@nerv-iip/business-core'

import {
  clickRefreshAndWaitForListResponse,
  fillFilterAndWaitForListResponse,
  listQueryFingerprint,
} from './issue1912-walkthrough-policy'
import { queryPath } from './issue1912-walkthrough-query'
import type {
  WmsInboundWalkthroughScenarioFacts,
  WmsWalkthroughScenarioFacts,
} from './issue1912-wms-walkthrough-authority'

const WMS_INBOUND_LIST_PATH = '/api/business-console/v1/wms/inbound-orders' as const
const WMS_OUTBOUND_LIST_PATH = '/api/business-console/v1/wms/outbound-orders' as const

export type WmsInboundListPath = typeof WMS_INBOUND_LIST_PATH
export type WmsOutboundListPath = typeof WMS_OUTBOUND_LIST_PATH
export type WmsListPath = WmsInboundListPath | WmsOutboundListPath

type WmsInitialEndpointKind = 'target' | 'auxiliary' | 'unknown' | 'outside'

/**
 * A mounted page has a page-specific WMS startup surface. The registry is keyed by the expected
 * target path so an outbound catalog request can be auxiliary on inbound, while the inverse path
 * remains an unexpected request on outbound. Exact paths only are accepted; nested, trailing-slash
 * and otherwise unregistered WMS paths stay fail-closed.
 */
const wmsInitialEndpointRegistry: Readonly<
  Record<WmsListPath, Readonly<Record<string, Exclude<WmsInitialEndpointKind, 'outside'>>>>
> = {
  [WMS_INBOUND_LIST_PATH]: {
    [WMS_INBOUND_LIST_PATH]: 'target',
    [WMS_OUTBOUND_LIST_PATH]: 'auxiliary',
    '/api/business-console/v1/wms/work-scopes/receipts': 'auxiliary',
    '/api/business-console/v1/wms/operational-candidates/receipts': 'auxiliary',
    '/api/business-console/v1/wms/putaway-tasks': 'auxiliary',
    '/api/business-console/v1/wms/picking-tasks': 'auxiliary',
    '/api/business-console/v1/wms/count-executions': 'auxiliary',
    '/api/business-console/v1/wms/receiving-quality-gates': 'auxiliary',
    '/api/business-console/v1/wms/supplier-return-requests': 'auxiliary',
  },
  [WMS_OUTBOUND_LIST_PATH]: {
    [WMS_OUTBOUND_LIST_PATH]: 'target',
    '/api/business-console/v1/wms/work-scopes/shipments': 'auxiliary',
    '/api/business-console/v1/wms/operational-candidates/shipments': 'auxiliary',
    '/api/business-console/v1/wms/putaway-tasks': 'auxiliary',
    '/api/business-console/v1/wms/picking-tasks': 'auxiliary',
    '/api/business-console/v1/wms/count-executions': 'auxiliary',
  },
}

export type WmsWorkPoolScopeFacts = Readonly<{
  scopeKind: 'work-pool'
  scopeId: string
}>

export type WmsListQueryFacts = Readonly<
  WmsWorkPoolScopeFacts & {
    organizationId: string
    environmentId: string
    skip: 0
    take: 10
  }
>

export type WmsInboundListQueryFacts = Readonly<WmsListQueryFacts & { siteCode: string }>
export type WmsOutboundListQueryFacts = Readonly<WmsListQueryFacts & { siteCode?: never }>
export type WmsInboundKeywordQueryFacts = Readonly<WmsInboundListQueryFacts & { keyword: string }>
export type WmsOutboundKeywordQueryFacts = Readonly<WmsOutboundListQueryFacts & { keyword: string }>

export type WmsPageSelection =
  | Readonly<{ label: string; option: string; optionCode?: never }>
  | Readonly<{ label: string; option?: never; optionCode: string }>

export type WmsScopeSelection = Readonly<{
  label: '作业范围'
  option: string
  scopeKind: 'work-pool'
  scopeId: string
}>

export type WmsSiteSelection = Readonly<{
  label: '工厂'
  option?: never
  optionCode: string
}>

export type WmsInboundPageSelection = Readonly<{
  scope: WmsScopeSelection
  site: WmsSiteSelection
}>

export type WmsOutboundPageSelection = Readonly<{
  scope: WmsScopeSelection
}>

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

function listQueryFacts(facts: WmsScenarioFacts): WmsListQueryFacts {
  const scope = workPoolScopeFacts(facts)
  requiredText('keyword', facts.keyword)
  return {
    organizationId: requiredText('organizationId', facts.organizationId),
    environmentId: requiredText('environmentId', facts.environmentId),
    ...scope,
    skip: 0,
    take: 10,
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

/**
 * Runtime guard for the discriminated selection type. It protects the fixture boundary too,
 * because JavaScript callers and intentionally invalid mutation tests can bypass TypeScript.
 */
export function assertWmsPageSelection(selection: WmsPageSelection): void {
  const label = selection.label.trim()
  const option = 'option' in selection ? selection.option : undefined
  const optionCode = 'optionCode' in selection ? selection.optionCode : undefined
  const hasOptionField = 'option' in selection
  const hasOptionCodeField = 'optionCode' in selection
  if (!label) throw new Error('WMS page selection label must not be empty')
  if (hasOptionField === hasOptionCodeField) {
    throw new Error(`WMS page selection ${label} must provide exactly one option fact`)
  }
  if (hasOptionField && (typeof option !== 'string' || option.trim() === '')) {
    throw new Error(`WMS page selection ${label} must provide exactly one option fact`)
  }
  if (hasOptionCodeField && (typeof optionCode !== 'string' || optionCode.trim() === '')) {
    throw new Error(`WMS page selection ${label} must provide exactly one option fact`)
  }
}

export function assertWmsInboundPageSelection(selection: WmsInboundPageSelection): void {
  if (!selection?.scope || !selection.site) {
    throw new Error('WMS inbound proof requires exactly one scope and one site selection')
  }
  if (selection.scope.label !== '作业范围' || selection.site.label !== '工厂') {
    throw new Error('WMS inbound proof received an unexpected selection label')
  }
  assertWmsScopeSelection(selection.scope)
  assertWmsPageSelection(selection.site)
}

export function assertWmsInboundSelectionMatchesQuery(
  selection: WmsInboundPageSelection,
  expectedQuery: WmsInboundListQueryFacts,
): void {
  assertWmsInboundPageSelection(selection)
  if (selection.scope.scopeId !== expectedQuery.scopeId) {
    throw new Error(
      `WMS inbound scope selection ${selection.scope.scopeId} did not match expected scopeId ${expectedQuery.scopeId}`,
    )
  }
  if (selection.scope.scopeKind !== expectedQuery.scopeKind) {
    throw new Error(
      `WMS inbound scope selection ${selection.scope.scopeKind} did not match expected scopeKind ${expectedQuery.scopeKind}`,
    )
  }
  if (selection.site.optionCode !== expectedQuery.siteCode) {
    throw new Error(
      `WMS inbound site selection ${selection.site.optionCode} did not match expected siteCode ${expectedQuery.siteCode}`,
    )
  }
}

export function assertWmsOutboundPageSelection(selection: WmsOutboundPageSelection): void {
  if (!selection?.scope) {
    throw new Error('WMS outbound proof requires exactly one scope selection')
  }
  if (selection.scope.label !== '作业范围') {
    throw new Error('WMS outbound proof received an unexpected selection label')
  }
  assertWmsScopeSelection(selection.scope)
}

export function assertWmsOutboundSelectionMatchesQuery(
  selection: WmsOutboundPageSelection,
  expectedQuery: WmsOutboundListQueryFacts | WmsOutboundKeywordQueryFacts,
): void {
  assertWmsOutboundPageSelection(selection)
  if (selection.scope.scopeId !== expectedQuery.scopeId) {
    throw new Error(
      `WMS outbound scope selection ${selection.scope.scopeId} did not match expected scopeId ${expectedQuery.scopeId}`,
    )
  }
  if (selection.scope.scopeKind !== expectedQuery.scopeKind) {
    throw new Error(
      `WMS outbound scope selection ${selection.scope.scopeKind} did not match expected scopeKind ${expectedQuery.scopeKind}`,
    )
  }
}

function assertWmsScopeSelection(selection: WmsScopeSelection): void {
  if (selection.label !== '作业范围') {
    throw new Error('WMS scope selection must use the 作业范围 label')
  }
  if (selection.scopeKind !== 'work-pool') {
    throw new Error(
      `WMS scope selection scopeKind must be work-pool, received ${selection.scopeKind}`,
    )
  }
  if (
    'optionCode' in selection ||
    typeof selection.option !== 'string' ||
    typeof selection.scopeId !== 'string' ||
    !selection.option.trim() ||
    !selection.scopeId.trim()
  ) {
    throw new Error('WMS scope selection must expose exactly one option and a non-empty scopeId')
  }
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
  if (expected.skip !== 0 || expected.take !== 10) {
    throw new Error(
      `WMS scenario fact pagination must be skip=0/take=10, received skip=${expected.skip}/take=${expected.take}`,
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

function assertExpectedQueryFacts(proof: WmsListQueryProof): asserts proof is WmsListQueryProof {
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

/**
 * 首个列表请求属于页面建立阶段，是目标端点和状态的公开生命周期证据，但不是选择证据：
 * 精确的范围/工厂事实必须由显式选择后的 action-bound 刷新证明。
 */
export function assertWmsInitialListResponse(
  response: WmsListResponseFacts,
  listPath: WmsListPath,
): void {
  if (response.status !== 200) {
    throw new Error(`WMS initial list ${listPath} returned HTTP ${response.status}`)
  }
  const actualPath = new URL(response.url).pathname
  if (actualPath !== listPath) {
    throw new Error(`WMS initial list response path ${actualPath} did not match ${listPath}`)
  }
}

function classifyWmsInitialEndpoint(
  expectedPath: WmsListPath,
  path: string,
): WmsInitialEndpointKind {
  if (!path.startsWith('/api/business-console/v1/wms/')) return 'outside'
  return wmsInitialEndpointRegistry[expectedPath][path] ?? 'unknown'
}

type WmsInitialGuardContext = Readonly<{
  navigationRoute: string
  documentAlreadyCommitted: boolean
}>

async function observeWmsInitialListResponse<T>(
  page: Page,
  expectedPath: WmsListPath,
  action: () => Promise<T>,
  timeoutMs: number,
  context: WmsInitialGuardContext,
): Promise<Readonly<{ result: T; firstList: Response }>> {
  const { navigationRoute } = context
  let resolveFirst!: (response: Response) => void
  let rejectFirst!: (error: unknown) => void
  const firstResponse = new Promise<Response>((resolve, reject) => {
    resolveFirst = resolve
    rejectFirst = reject
  })
  let firstCandidateSeen = false
  let documentCommitted = context.documentAlreadyCommitted
  let firstListRequest: Request | undefined
  const failFirst = (message: string) => {
    if (firstCandidateSeen) return
    firstCandidateSeen = true
    rejectFirst(new Error(message))
  }
  const frameNavigationObserver = (frame: { url: () => string }) => {
    if (frame !== page.mainFrame()) return
    const frameUrl = frame.url()
    if (!frameUrl || frameUrl === 'about:blank') return
    const current = new URL(frameUrl)
    const expected = new URL(navigationRoute, current.toString())
    documentCommitted = current.pathname === expected.pathname && current.search === expected.search
  }
  const refreshDocumentCommitState = () => {
    const currentUrl = page.url()
    if (documentCommitted || !currentUrl || currentUrl === 'about:blank') return
    const current = new URL(currentUrl)
    const expected = new URL(navigationRoute, current.toString())
    documentCommitted = current.pathname === expected.pathname && current.search === expected.search
  }
  const classifyRequest = (request: Request): WmsInitialEndpointKind =>
    classifyWmsInitialEndpoint(expectedPath, new URL(request.url()).pathname)
  const failForUnexpectedRequest = (
    request: Request,
    endpointKind: WmsInitialEndpointKind,
  ): boolean => {
    const path = new URL(request.url()).pathname
    if (endpointKind === 'unknown') {
      failFirst(`unexpected WMS list-like request path ${path}`)
      return true
    }
    if (request.method() !== 'GET') {
      failFirst(`unexpected WMS initial list request method ${request.method()}`)
      return true
    }
    return false
  }
  const requestObserver = (request: Request) => {
    if (firstCandidateSeen) return
    refreshDocumentCommitState()
    if (!documentCommitted || request.frame() !== page.mainFrame()) return
    const endpointKind = classifyRequest(request)
    if (endpointKind === 'outside') return
    if (failForUnexpectedRequest(request, endpointKind)) return
    if (endpointKind === 'auxiliary') return
    firstListRequest ??= request
  }
  const responseObserver = (response: Response) => {
    if (firstCandidateSeen) return
    refreshDocumentCommitState()
    if (!documentCommitted) return
    const request = response.request()
    if (request.frame() !== page.mainFrame()) return
    const path = new URL(response.url()).pathname
    const endpointKind = classifyWmsInitialEndpoint(expectedPath, path)
    if (endpointKind === 'outside') return
    if (failForUnexpectedRequest(request, endpointKind)) return
    if (endpointKind === 'unknown') return
    if (endpointKind === 'auxiliary') {
      if (response.status() !== 200) {
        failFirst(`WMS initial auxiliary ${path} returned HTTP ${response.status()}`)
      }
      return
    }
    try {
      assertWmsInitialListResponse({ url: response.url(), status: response.status() }, expectedPath)
    } catch (error) {
      firstCandidateSeen = true
      rejectFirst(error)
      return
    }
    if (firstListRequest && request !== firstListRequest) return
    firstListRequest ??= request
    firstCandidateSeen = true
    resolveFirst(response)
  }
  const requestFailedObserver = (request: Request) => {
    if (firstCandidateSeen) return
    refreshDocumentCommitState()
    if (!documentCommitted || request.frame() !== page.mainFrame()) return
    const endpointKind = classifyRequest(request)
    if (endpointKind === 'outside') return
    if (failForUnexpectedRequest(request, endpointKind)) return
    if (endpointKind === 'target') firstListRequest ??= request
    const path = new URL(request.url()).pathname
    const failure = request.failure()?.errorText ?? 'unknown network failure'
    failFirst(`WMS initial ${endpointKind} ${path} request failed: ${failure}`)
  }
  const timeout = setTimeout(
    () => rejectFirst(new Error(`WMS initial list ${expectedPath} response was not observed`)),
    timeoutMs,
  )
  page.on('response', responseObserver)
  page.on('request', requestObserver)
  page.on('requestfailed', requestFailedObserver)
  page.on('framenavigated', frameNavigationObserver)
  try {
    const actionResult = action()
    const [result, firstList] = await Promise.all([actionResult, firstResponse])
    return { result, firstList }
  } finally {
    clearTimeout(timeout)
    page.off('response', responseObserver)
    page.off('request', requestObserver)
    page.off('requestfailed', requestFailedObserver)
    page.off('framenavigated', frameNavigationObserver)
  }
}

/**
 * Adds the NERV-1571 lifecycle boundary around an existing navigation action. The generic
 * walkthrough policy intentionally waits for a successful target response; this guard observes
 * the first WMS list response itself so an initial 503 or wrong WMS path cannot be hidden by a
 * later 200. It is deliberately an action wrapper, keeping the NERV-1456 policy unchanged.
 */
export async function withWmsInitialListResponseGuard<T>(
  page: Page,
  expectedPath: WmsListPath,
  action: () => Promise<T>,
  timeoutMs: number,
  navigationRoute: string,
): Promise<Readonly<{ result: T; firstList: Response }>> {
  return observeWmsInitialListResponse(page, expectedPath, action, timeoutMs, {
    navigationRoute,
    documentAlreadyCommitted: false,
  })
}

/**
 * Fixture-only lifecycle wrapper. It is intentionally separate from the production navigation
 * wrapper: synthetic tests start from an already committed document, while real walkthroughs must
 * always provide their exact navigation route to the guard above.
 */
export async function withWmsInitialListResponseGuardForTest<T>(
  page: Page,
  expectedPath: WmsListPath,
  action: () => Promise<T>,
  timeoutMs = 120_000,
): Promise<Readonly<{ result: T; firstList: Response }>> {
  return observeWmsInitialListResponse(page, expectedPath, action, timeoutMs, {
    navigationRoute: page.url(),
    documentAlreadyCommitted: true,
  })
}

async function waitForUniqueVisibleOption(
  option: ReturnType<Page['locator']>,
  label: string,
  timeoutMs: number,
): Promise<void> {
  try {
    let previousCount: number | undefined
    let stableSince: number | undefined
    await expect
      .poll(
        async () => {
          const currentCount = await option.count()
          if (currentCount !== previousCount) {
            previousCount = currentCount
            stableSince = currentCount === 1 ? Date.now() : undefined
          }
          return currentCount === 1 && stableSince !== undefined && Date.now() - stableSince >= 200
            ? 1
            : 0
        },
        {
          timeout: timeoutMs,
          intervals: [50, 100, 250, 500],
          message: `WMS page selection ${label} catalog did not settle on one visible option`,
        },
      )
      .toBe(1)
  } catch {
    const optionCount = await option.count()
    throw new Error(`WMS page selection ${label} expected one catalog option, found ${optionCount}`)
  }
}

/**
 * Selects a real page option. The visible catalog is polled after opening, so an asynchronously
 * mounted site catalog cannot be mistaken for a missing option. No localStorage/default option is
 * accepted as evidence.
 */
export async function selectWmsPageOption(
  page: Page,
  selection: WmsPageSelection,
  timeoutMs = 120_000,
): Promise<void> {
  assertWmsPageSelection(selection)
  const trigger = page.getByLabel(selection.label, { exact: true })
  await trigger.click({ timeout: timeoutMs })
  await expect(page.locator('[role="listbox"]:visible')).toHaveCount(1, { timeout: timeoutMs })

  const option =
    'option' in selection
      ? page.getByRole('option', { name: selection.option, exact: true })
      : page.locator('[role="option"]:visible').filter({
          has: page.getByText(selection.optionCode, { exact: true }),
        })
  await waitForUniqueVisibleOption(option, selection.label, timeoutMs)
  await option.click({ timeout: timeoutMs })
  await expect(trigger).toHaveAttribute('aria-expanded', 'false', { timeout: timeoutMs })
  const selectedText = 'option' in selection ? (selection.option ?? '') : selection.optionCode
  try {
    await expect(trigger).toContainText(selectedText, { timeout: timeoutMs })
  } catch {
    throw new Error(
      `WMS page selection ${selection.label} did not expose selected value ${selectedText}`,
    )
  }
}

/**
 * Work-scope options expose their stable value through the search input (the NvSearchSelect value
 * is searchable even though only the human label is rendered). Requiring the scope id in that
 * input makes the clicked page option and the later query fact share one public selection.
 */
export async function selectWmsScopeOption(
  page: Page,
  selection: WmsScopeSelection,
  timeoutMs = 120_000,
): Promise<void> {
  assertWmsScopeSelection(selection)
  const trigger = page.getByLabel(selection.label, { exact: true })
  await trigger.click({ timeout: timeoutMs })
  await expect(page.locator('[role="listbox"]:visible')).toHaveCount(1, { timeout: timeoutMs })
  const search = page.getByRole('combobox', { name: `搜索${selection.label}`, exact: true })
  await expect(search).toHaveCount(1, { timeout: timeoutMs })
  const expectedOptionValue = formatWorkScopeKey(selection.scopeKind, selection.scopeId)
  await search.fill(expectedOptionValue)
  const option = page.getByRole('option', { name: selection.option, exact: true })
  await waitForUniqueVisibleOption(option, selection.label, timeoutMs)
  await option.click({ timeout: timeoutMs })
  await expect(trigger).toHaveAttribute('aria-expanded', 'false', { timeout: timeoutMs })
  await expect(trigger).toContainText(selection.option, { timeout: timeoutMs })

  // Re-open and filter by the canonical option value. The selected marker is computed from the
  // component's model value, so this readback proves the underlying `scopeKind:scopeId`, not just
  // the trigger's human label or a remembered/default selection.
  await trigger.click({ timeout: timeoutMs })
  await expect(page.locator('[role="listbox"]:visible')).toHaveCount(1, { timeout: timeoutMs })
  const readbackSearch = page.getByRole('combobox', { name: `搜索${selection.label}`, exact: true })
  await readbackSearch.fill(expectedOptionValue)
  const readbackOption = page.getByRole('option', { name: selection.option, exact: true })
  await waitForUniqueVisibleOption(readbackOption, selection.label, timeoutMs)
  await expect(readbackOption).toHaveAttribute('aria-selected', 'true', { timeout: timeoutMs })
  await readbackOption.click({ timeout: timeoutMs })
  await expect(trigger).toHaveAttribute('aria-expanded', 'false', { timeout: timeoutMs })
}

export type WmsListPageProofOptions =
  | Readonly<{
      kind: 'inbound'
      page: Page
      selection: WmsInboundPageSelection
      query: WmsInboundListQueryProof
    }>
  | Readonly<{
      kind: 'outbound'
      page: Page
      selection: WmsOutboundPageSelection
      query: WmsOutboundListQueryProof
    }>

export type WmsInboundListPageProofInput = Omit<
  Extract<WmsListPageProofOptions, { kind: 'inbound' }>,
  'page'
>
export type WmsOutboundListPageProofInput = Omit<
  Extract<WmsListPageProofOptions, { kind: 'outbound' }>,
  'page'
>

/**
 * WMS-only proof layer: explicit scope/site selection, then action-bound refresh and exact facts.
 * Keeping this sequence outside generic ERP/MES page proof makes removal of either WMS step a
 * directly testable regression instead of an optional branch hidden in a shared helper.
 */
export async function proveWmsListPage(options: WmsListPageProofOptions): Promise<Response> {
  assertExpectedQueryFacts(options.query)
  if (options.query.kind !== options.kind) {
    throw new Error(`WMS ${options.kind} proof received a ${options.query.kind} query contract`)
  }

  if (options.kind === 'inbound') {
    assertWmsInboundSelectionMatchesQuery(options.selection, options.query.selectionQuery)
    await selectWmsScopeOption(options.page, options.selection.scope)
    await selectWmsPageOption(options.page, options.selection.site)
  } else {
    assertWmsOutboundSelectionMatchesQuery(options.selection, options.query.selectionQuery)
    await selectWmsScopeOption(options.page, options.selection.scope)
  }

  return refreshWmsListAndConfirm(options.page, options.query)
}

export async function refreshWmsListAndConfirm(
  page: Page,
  proof: WmsListQueryProof,
  timeoutMs = 120_000,
): Promise<Response> {
  assertExpectedQueryFacts(proof)
  const response = await clickRefreshAndWaitForListResponse(page, proof.listPath, timeoutMs)
  assertWmsListQueryFacts({ url: response.url(), status: response.status() }, proof)
  return response
}

/**
 * Binds the authority keyword to the real filter action. The generic policy remains responsible
 * for action ownership and HTTP status/abort handling; this WMS wrapper additionally checks the
 * exact keyword multiplicity and the full query after that action has completed.
 */
export async function fillWmsKeywordAndConfirm(
  page: Page,
  proof: WmsListQueryProof,
  initialListResponse: Response,
  initialListNavigationEpoch: number | undefined,
  filterLabel = '关键字搜索',
  timeoutMs = 120_000,
): Promise<Response> {
  assertExpectedQueryFacts(proof)
  const expectedKeyword = proof.keywordQuery.keyword
  const expectedQueryFingerprint = listQueryFingerprint(
    new URL(
      queryPath(proof.listPath, proof.selectionQuery),
      'http://walkthrough.expected',
    ).toString(),
  )
  const actionRequests: Request[] = []
  const actionResponses = new Map<Request, Response>()
  let capturingActionRequest = false
  const requestObserver = (request: Request) => {
    if (!capturingActionRequest) return
    const url = new URL(request.url())
    const keywords = url.searchParams.getAll('keyword')
    if (
      request.method() === 'GET' &&
      request.frame() === page.mainFrame() &&
      url.pathname === proof.listPath &&
      Boolean(request.headers()['x-nerv-walkthrough-action']) &&
      keywords.length === 1 &&
      keywords[0] === expectedKeyword &&
      listQueryFingerprint(request.url()) === expectedQueryFingerprint
    ) {
      actionRequests.push(request)
    }
  }
  const responseObserver = (response: Response) => {
    const request = response.request()
    if (actionRequests.includes(request)) actionResponses.set(request, response)
  }
  page.on('request', requestObserver)
  page.on('response', responseObserver)
  try {
    capturingActionRequest = true
    await fillFilterAndWaitForListResponse(page, {
      route: page.url(),
      listPath: proof.listPath,
      filterLabel,
      stableText: expectedKeyword,
      responseMode: 'server',
      initialListResponse,
      initialListNavigationEpoch,
      expectedListQueryFingerprint: expectedQueryFingerprint,
      timeoutMs,
    })
    capturingActionRequest = false
    await expect
      .poll(() => actionRequests.length, {
        timeout: timeoutMs,
        message: `WMS keyword action did not emit exactly one marked request for ${proof.listPath}`,
      })
      .toBe(1)
    const actionRequest = actionRequests[0]
    if (!actionRequest) {
      throw new Error(`WMS keyword action request was not observed for ${proof.listPath}`)
    }
    await expect
      .poll(() => actionResponses.has(actionRequest), {
        timeout: timeoutMs,
        message: `WMS keyword action did not emit a response for ${proof.listPath}`,
      })
      .toBe(true)
    const response = actionResponses.get(actionRequest)
    if (!response) {
      throw new Error(`WMS keyword action response was not observed for ${proof.listPath}`)
    }
    assertWmsListQueryFacts({ url: response.url(), status: response.status() }, proof, 'keyword')
    return response
  } finally {
    capturingActionRequest = false
    page.off('request', requestObserver)
    page.off('response', responseObserver)
  }
}
