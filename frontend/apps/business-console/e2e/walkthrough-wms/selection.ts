import { type WmsWalkthroughPageWindowInput } from '../issue1912-wms-walkthrough-authority'
import { type Page, expect } from '@playwright/test'
import {
  assertWmsPageWindow,
  type WmsInboundListQueryFacts,
  type WmsOutboundListQueryFacts,
  type WmsOutboundKeywordQueryFacts,
} from './query-facts'
import { formatWorkScopeKey } from '@nerv-iip/business-core'

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

type WmsPageWindowReadback = Readonly<{
  skip: 0
  take: number
}>

async function assertWmsDefaultPageWindow(
  page: Page,
  expectedWindow: Extract<WmsWalkthroughPageWindowInput, { mode: 'default' }>,
  timeoutMs: number,
): Promise<void> {
  await expect(page.locator('table')).toBeVisible({ timeout: timeoutMs })
  const trigger = page.getByLabel('每页条数', { exact: true })
  const triggerCount = await trigger.count()
  if (triggerCount === 0) return
  if (triggerCount !== 1) {
    throw new Error(
      `WMS default page window expected zero or one page-size control, found ${triggerCount}`,
    )
  }
  await expect(trigger).toBeVisible({ timeout: timeoutMs })
  await expect(trigger).toContainText(String(expectedWindow.take), { timeout: timeoutMs })
  const currentPage = page.locator('[aria-current="page"][aria-label^="第 "]')
  await expect(currentPage).toHaveCount(1, { timeout: timeoutMs })
  await expect(currentPage).toHaveAttribute('aria-label', '第 1 页', { timeout: timeoutMs })
}

/**
 * Resolves the expected page window from the mounted table's public state. Default mode accepts a
 * low-cardinality table with no pagination control (or reads back the untouched default control);
 * selected mode requires an explicit option action and independent selected-state readback.
 */
export async function selectWmsPageWindow(
  page: Page,
  expectedWindow: WmsWalkthroughPageWindowInput,
  timeoutMs = 120_000,
): Promise<WmsPageWindowReadback> {
  assertWmsPageWindow(expectedWindow)
  if (expectedWindow.mode === 'default') {
    await assertWmsDefaultPageWindow(page, expectedWindow, timeoutMs)
    return { skip: 0, take: expectedWindow.take }
  }

  const trigger = page.getByLabel('每页条数', { exact: true })
  await expect(trigger).toHaveCount(1, { timeout: timeoutMs })
  await expect(trigger).toBeVisible({ timeout: timeoutMs })

  const openOptions = () =>
    page.locator('[role="listbox"]:visible').getByRole('option', {
      name: String(expectedWindow.take),
      exact: true,
    })
  await trigger.click({ timeout: timeoutMs })
  await expect(page.locator('[role="listbox"]:visible')).toHaveCount(1, { timeout: timeoutMs })
  const option = openOptions()
  await waitForUniqueVisibleOption(option, `每页条数 ${expectedWindow.take}`, timeoutMs)
  await option.click({ timeout: timeoutMs })
  await expect(trigger).toHaveAttribute('aria-expanded', 'false', { timeout: timeoutMs })

  // The trigger text is the first public projection of the selected model value.
  await expect(trigger).toContainText(String(expectedWindow.take), { timeout: timeoutMs })

  // Re-open the control and require the option's selected state as an independent readback.
  await trigger.click({ timeout: timeoutMs })
  await expect(page.locator('[role="listbox"]:visible')).toHaveCount(1, { timeout: timeoutMs })
  const selectedOption = openOptions()
  await waitForUniqueVisibleOption(
    selectedOption,
    `每页条数 ${expectedWindow.take} 的已选项`,
    timeoutMs,
  )
  await expect(selectedOption).toHaveAttribute('aria-selected', 'true', { timeout: timeoutMs })
  await page.keyboard.press('Escape')
  await expect(trigger).toHaveAttribute('aria-expanded', 'false', { timeout: timeoutMs })

  const currentPage = page.locator('[aria-current="page"][aria-label^="第 "]')
  await expect(currentPage).toHaveCount(1, { timeout: timeoutMs })
  await expect(currentPage).toHaveAttribute('aria-label', '第 1 页', { timeout: timeoutMs })
  return { skip: 0, take: expectedWindow.take }
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
