import { expect, type Page, type Request, type Response } from '@playwright/test'

import {
  clickRefreshAndWaitForListResponse,
  listQueryFingerprint,
} from './issue1912-walkthrough-policy'
import { queryPath } from './issue1912-walkthrough-query'
import type {
  WmsInboundWalkthroughScenarioFacts,
  WmsWalkthroughScenarioFacts,
} from './issue1912-wms-walkthrough-authority'

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
export type WmsInboundListPath = '/api/business-console/v1/wms/inbound-orders'
export type WmsOutboundListPath = '/api/business-console/v1/wms/outbound-orders'
export type WmsListPath = WmsInboundListPath | WmsOutboundListPath

type WmsExpectedInboundQuery = WmsInboundListQueryFacts | WmsInboundKeywordQueryFacts
type WmsExpectedOutboundQuery = WmsOutboundListQueryFacts | WmsOutboundKeywordQueryFacts

export type WmsPageSelection =
  | Readonly<{ label: string; option: string; optionCode?: never }>
  | Readonly<{ label: string; option?: never; optionCode: string }>

export type WmsScopeSelection = Readonly<{
  label: '作业范围'
  option: string
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
  expectedQuery: WmsExpectedInboundQuery
  forbiddenQueryKeys: readonly []
}>

export type WmsOutboundListQueryProof = Readonly<{
  kind: 'outbound'
  listPath: WmsOutboundListPath
  expectedQuery: WmsExpectedOutboundQuery
  forbiddenQueryKeys: readonly ['siteCode']
}>

export type WmsListQueryProof = WmsInboundListQueryProof | WmsOutboundListQueryProof

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
  expectedQuery: WmsExpectedOutboundQuery,
): void {
  assertWmsOutboundPageSelection(selection)
  if (selection.scope.scopeId !== expectedQuery.scopeId) {
    throw new Error(
      `WMS outbound scope selection ${selection.scope.scopeId} did not match expected scopeId ${expectedQuery.scopeId}`,
    )
  }
}

function assertWmsScopeSelection(selection: WmsScopeSelection): void {
  if (
    selection.label !== '作业范围' ||
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
  return kind === 'inbound'
    ? '/api/business-console/v1/wms/inbound-orders'
    : '/api/business-console/v1/wms/outbound-orders'
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

  const expected = proof.expectedQuery
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
  if (proof.kind === 'inbound') {
    requiredText('siteCode', proof.expectedQuery.siteCode)
  } else if ('siteCode' in expected && expected.siteCode !== undefined) {
    throw new Error('WMS outbound proof must not define a siteCode fact')
  }
  if ('keyword' in expected) requiredText('keyword', expected.keyword)
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

  const expectedUrl = queryPath(proof.listPath, proof.expectedQuery)
  const expectedAbsoluteUrl = new URL(expectedUrl, 'http://walkthrough.expected').toString()
  const expectedKeyword =
    'keyword' in proof.expectedQuery
      ? requiredText('keyword', proof.expectedQuery.keyword)
      : undefined
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

const wmsListPaths: readonly WmsListPath[] = [
  '/api/business-console/v1/wms/inbound-orders',
  '/api/business-console/v1/wms/outbound-orders',
]

function isWmsListPath(path: string): path is WmsListPath {
  return wmsListPaths.includes(path as WmsListPath)
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
  timeoutMs = 120_000,
  navigationRoute?: string,
): Promise<Readonly<{ result: T; firstList: Response }>> {
  let resolveFirst!: (response: Response) => void
  let rejectFirst!: (error: unknown) => void
  const firstResponse = new Promise<Response>((resolve, reject) => {
    resolveFirst = resolve
    rejectFirst = reject
  })
  let firstCandidateSeen = false
  let documentCommitted = navigationRoute === undefined
  const listRequests = new WeakSet<Request>()
  const frameNavigationObserver = (frame: { url: () => string }) => {
    if (frame !== page.mainFrame() || !navigationRoute) return
    const current = new URL(frame.url(), page.url())
    const expected = new URL(navigationRoute, page.url())
    documentCommitted = current.pathname === expected.pathname && current.search === expected.search
  }
  const requestObserver = (request: Request) => {
    const path = new URL(request.url()).pathname
    if (
      documentCommitted &&
      request.method() === 'GET' &&
      isWmsListPath(path) &&
      request.frame() === page.mainFrame()
    ) {
      listRequests.add(request)
    }
  }
  const responseObserver = (response: Response) => {
    if (firstCandidateSeen) return
    const request = response.request()
    const path = new URL(response.url()).pathname
    if (
      !listRequests.has(request) ||
      request.method() !== 'GET' ||
      !isWmsListPath(path) ||
      request.frame() !== page.mainFrame()
    ) {
      return
    }
    firstCandidateSeen = true
    try {
      assertWmsInitialListResponse({ url: response.url(), status: response.status() }, expectedPath)
      resolveFirst(response)
    } catch (error) {
      rejectFirst(error)
    }
  }
  const timeout = setTimeout(
    () => rejectFirst(new Error(`WMS initial list ${expectedPath} response was not observed`)),
    timeoutMs,
  )
  page.on('response', responseObserver)
  page.on('request', requestObserver)
  page.on('framenavigated', frameNavigationObserver)
  try {
    const actionResult = action()
    const [result, firstList] = await Promise.all([actionResult, firstResponse])
    return { result, firstList }
  } finally {
    clearTimeout(timeout)
    page.off('response', responseObserver)
    page.off('request', requestObserver)
    page.off('framenavigated', frameNavigationObserver)
  }
}

async function waitForUniqueVisibleOption(
  option: ReturnType<Page['locator']>,
  label: string,
  timeoutMs: number,
): Promise<void> {
  try {
    let previousCount: number | undefined
    let stableSamples = 0
    await expect
      .poll(
        async () => {
          const currentCount = await option.count()
          if (currentCount === previousCount) stableSamples += 1
          else {
            previousCount = currentCount
            stableSamples = 1
          }
          return currentCount === 1 && stableSamples >= 2 ? 1 : 0
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
  await search.fill(selection.scopeId)
  const option = page.getByRole('option', { name: selection.option, exact: true })
  await waitForUniqueVisibleOption(option, selection.label, timeoutMs)
  await option.click({ timeout: timeoutMs })
  await expect(trigger).toHaveAttribute('aria-expanded', 'false', { timeout: timeoutMs })
  await expect(trigger).toContainText(selection.option, { timeout: timeoutMs })
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
    assertWmsInboundSelectionMatchesQuery(options.selection, options.query.expectedQuery)
    await selectWmsScopeOption(options.page, options.selection.scope)
    await selectWmsPageOption(options.page, options.selection.site)
  } else {
    assertWmsOutboundSelectionMatchesQuery(options.selection, options.query.expectedQuery)
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
