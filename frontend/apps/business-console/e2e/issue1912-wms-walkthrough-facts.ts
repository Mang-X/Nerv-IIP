import { expect, type Page, type Response } from '@playwright/test'

import {
  clickRefreshAndWaitForListResponse,
  listQueryFingerprint,
} from './issue1912-walkthrough-policy'

export type JsonRecord = Record<string, unknown>

export type WmsPageSelection = Readonly<{
  label: string
  option?: string
  optionCode?: string
}>

export type WmsListResponseFacts = Readonly<{
  url: string
  status: number
}>

function expectedQueryUrl(listPath: string, expectedQuery: JsonRecord): URL {
  const url = new URL(listPath, 'http://walkthrough.expected')
  for (const [key, value] of Object.entries(expectedQuery)) {
    if (value !== null && value !== undefined && value !== '') {
      url.searchParams.set(key, String(value))
    }
  }
  return url
}

/**
 * Validates the public list request against scenario facts. A 200 response alone is not evidence:
 * tenant, scope and pagination must all match, and callers may explicitly forbid a field such as
 * `siteCode` when that page has no site filter.
 */
export function assertWmsListQueryFacts(
  response: WmsListResponseFacts,
  listPath: string,
  expectedQuery: JsonRecord,
  forbiddenQueryKeys: readonly string[] = [],
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

  const expectedUrl = expectedQueryUrl(listPath, expectedQuery)
  if (listQueryFingerprint(actualUrl.toString()) !== listQueryFingerprint(expectedUrl.toString())) {
    throw new Error(
      `WMS list ${listPath} query facts did not match: expected=${expectedUrl.search} actual=${actualUrl.search}`,
    )
  }
}

/**
 * Selects a real page option and fails closed when a catalog-backed code is absent or ambiguous.
 * `optionCode` is used for entity pickers whose visible label is catalog data; no label is guessed.
 */
export async function selectWmsPageOption(
  page: Page,
  selection: WmsPageSelection,
  timeoutMs = 120_000,
): Promise<void> {
  const hasOption = selection.option !== undefined
  const hasOptionCode = selection.optionCode !== undefined
  if (hasOption === hasOptionCode) {
    throw new Error(`WMS page selection ${selection.label} must provide exactly one option fact`)
  }

  const trigger = page.getByLabel(selection.label, { exact: true })
  await trigger.click({ timeout: timeoutMs })
  const option = hasOption
    ? page.getByRole('option', { name: selection.option, exact: true })
    : page.locator('[role="option"]:visible').filter({
        has: page.getByText(selection.optionCode!, { exact: true }),
      })
  const optionCount = await option.count()
  if (optionCount !== 1) {
    throw new Error(
      `WMS page selection ${selection.label} expected one catalog option, found ${optionCount}`,
    )
  }
  await option.click({ timeout: timeoutMs })
  await expect(trigger).toHaveAttribute('aria-expanded', 'false', { timeout: timeoutMs })
}

export async function refreshWmsListAndConfirm(
  page: Page,
  listPath: string,
  expectedQuery: JsonRecord,
  forbiddenQueryKeys: readonly string[] = [],
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
