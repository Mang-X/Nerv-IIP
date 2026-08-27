import { expect, type Page, type Response } from '@playwright/test'

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

export type WmsPageSelection =
  | Readonly<{ label: string; option: string; optionCode?: never }>
  | Readonly<{ label: string; option?: never; optionCode: string }>

export type WmsScopeSelection = Readonly<{
  label: '作业范围'
  option: string
  optionCode?: never
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
): WmsInboundListQueryFacts {
  return {
    ...listQueryFacts(facts),
    siteCode: requiredText('siteCode', facts.siteCode),
  }
}

export function buildWmsOutboundListQueryFacts(
  facts: WmsWalkthroughScenarioFacts,
): WmsOutboundListQueryFacts {
  return listQueryFacts(facts)
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
  assertWmsPageSelection(selection.scope)
  assertWmsPageSelection(selection.site)
}

export function assertWmsOutboundPageSelection(selection: WmsOutboundPageSelection): void {
  if (!selection?.scope) {
    throw new Error('WMS outbound proof requires exactly one scope selection')
  }
  if (selection.scope.label !== '作业范围') {
    throw new Error('WMS outbound proof received an unexpected selection label')
  }
  assertWmsPageSelection(selection.scope)
}

/**
 * Validates the public list request against the NERV-1571 facts. A 200 response alone is not
 * evidence: tenant, scope and pagination must all match, and outbound may explicitly forbid
 * siteCode. The expected query is supplied by scenario input, never by the response.
 */
export function assertWmsListQueryFacts(
  response: WmsListResponseFacts,
  listPath: string,
  expectedQuery: WmsInboundListQueryFacts | WmsOutboundListQueryFacts,
  forbiddenQueryKeys: readonly 'siteCode'[] = [],
): void {
  if (response.status !== 200) {
    throw new Error(`WMS list ${listPath} returned HTTP ${response.status}`)
  }

  const actualUrl = new URL(response.url)
  if (actualUrl.pathname !== listPath) {
    throw new Error(`WMS list response path ${actualUrl.pathname} did not match ${listPath}`)
  }

  for (const key of forbiddenQueryKeys) {
    if (actualUrl.searchParams.has(key)) {
      throw new Error(`WMS list ${listPath} must not send query field ${key}`)
    }
  }

  const expectedUrl = queryPath(listPath, expectedQuery)
  const expectedAbsoluteUrl = new URL(expectedUrl, 'http://walkthrough.expected').toString()
  if (listQueryFingerprint(actualUrl.toString()) !== listQueryFingerprint(expectedAbsoluteUrl)) {
    throw new Error(
      `WMS list ${listPath} query facts did not match: expected=${new URL(expectedAbsoluteUrl).search} actual=${actualUrl.search}`,
    )
  }
}

async function waitForUniqueVisibleOption(
  option: ReturnType<Page['locator']>,
  label: string,
  timeoutMs: number,
): Promise<void> {
  try {
    await expect
      .poll(() => option.count(), {
        timeout: timeoutMs,
        message: `WMS page selection ${label} catalog did not settle on one visible option`,
      })
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
}

export type WmsListPageProofOptions =
  | Readonly<{
      kind: 'inbound'
      page: Page
      listPath: string
      selection: WmsInboundPageSelection
      expectedQuery: WmsInboundListQueryFacts
    }>
  | Readonly<{
      kind: 'outbound'
      page: Page
      listPath: string
      selection: WmsOutboundPageSelection
      expectedQuery: WmsOutboundListQueryFacts
    }>

function expectedWmsListPath(kind: WmsListPageProofOptions['kind']): string {
  return kind === 'inbound'
    ? '/api/business-console/v1/wms/inbound-orders'
    : '/api/business-console/v1/wms/outbound-orders'
}

/**
 * WMS-only proof layer: explicit scope/site selection, then action-bound refresh and exact facts.
 * Keeping this sequence outside generic ERP/MES page proof makes removal of either WMS step a
 * directly testable regression instead of an optional branch hidden in a shared helper.
 */
export async function proveWmsListPage(options: WmsListPageProofOptions): Promise<Response> {
  if (options.listPath !== expectedWmsListPath(options.kind)) {
    throw new Error(`WMS ${options.kind} proof received unexpected list path ${options.listPath}`)
  }

  if (options.kind === 'inbound') {
    assertWmsInboundPageSelection(options.selection)
    if (options.selection.site.optionCode !== options.expectedQuery.siteCode) {
      throw new Error(
        `WMS inbound site selection ${options.selection.site.optionCode} did not match expected siteCode ${options.expectedQuery.siteCode}`,
      )
    }
    await selectWmsPageOption(options.page, options.selection.scope)
    await selectWmsPageOption(options.page, options.selection.site)
  } else {
    assertWmsOutboundPageSelection(options.selection)
    await selectWmsPageOption(options.page, options.selection.scope)
  }

  return refreshWmsListAndConfirm(
    options.page,
    options.listPath,
    options.expectedQuery,
    options.kind === 'outbound' ? ['siteCode'] : [],
  )
}

export async function refreshWmsListAndConfirm(
  page: Page,
  listPath: string,
  expectedQuery: WmsInboundListQueryFacts | WmsOutboundListQueryFacts,
  forbiddenQueryKeys: readonly 'siteCode'[] = [],
  timeoutMs = 120_000,
): Promise<Response> {
  const response = await clickRefreshAndWaitForListResponse(page, listPath, timeoutMs)
  assertWmsListQueryFacts(
    { url: response.url(), status: response.status() },
    listPath,
    expectedQuery,
    forbiddenQueryKeys,
  )
  return response
}
