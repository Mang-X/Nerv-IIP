import { expect, type Page, type Response } from '@playwright/test'
import { join } from 'node:path'
import {
  clickRefreshAndWaitForListResponse,
  clickTabAndConfirmUnmount,
  fillFilterAndWaitForListResponse,
  listQueryFingerprint,
  navigateAndWaitForInitialList,
} from './issue1912-walkthrough-policy'
import {
  assertWmsPageProofOptions,
  assertWmsInitialListResponse,
  fillWmsKeywordAndConfirm,
  proveWmsListPage,
  withWmsInitialListResponseGuard,
  type WmsInboundListPageProofInput,
  type WmsOutboundListPageProofInput,
} from './issue1912-wms-walkthrough-facts'
import {
  safeText,
  type NodeName,
  type JsonRecord,
  type UiProof,
  type WalkthroughActor,
  type AutomationMode,
} from './issue1912-walkthrough-evidence'
import type { ActorRuntime } from './issue1912-walkthrough-session'
import { queryPath as canonicalQueryPath } from './issue1912-walkthrough-query'

export function createWalkthroughPageProofs({
  adminRuntime,
  workerRuntime,
  screenshotDirectory,
  uiEvidence,
  markFailure,
  baseURL,
}: {
  adminRuntime: ActorRuntime
  workerRuntime: ActorRuntime
  screenshotDirectory: string
  uiEvidence: UiProof[]
  markFailure: (node: NodeName, error: unknown, mode?: AutomationMode) => void
  baseURL: string
}) {
  const queryPath = (path: string, query: JsonRecord) => canonicalQueryPath(path, query, baseURL)
  type PageProofOptions = {
    actor?: WalkthroughActor
    route: string
    listPath: string
    stableText: string
    filterLabel?: string
    // `client` proves the rendered table after a local filter; `server` requires an exact 200 list response.
    filterResponseMode?: 'server' | 'client'
    // The expected list scope is an independent walkthrough fact, not copied from the response.
    // Server-filter proofs fail closed when this query is omitted.
    expectedListQuery?: JsonRecord
    tabText?: string | RegExp
    // Reuse a settled route when a tab proof only needs refreshed data; a full reload can supersede API work.
    reuseCurrentRoute?: boolean
    refreshListBeforeProof?: boolean
    selectOptions?: Array<{ label: string; option: string }>
    emptyText: string
    screenshotName: string
  }

  type EstablishedPage = {
    runtime: ActorRuntime
    targetPage: Page
    listPath: string
    navigation: Response
    firstList: Response
    firstListNavigationEpoch: number | undefined
  }

  type ListProofContext = Readonly<{
    page: Page
    listPath: string
    response: Response
    navigationEpoch: number | undefined
  }>

  type WmsPageProofOptions = Readonly<{
    actor: 'wms-worker'
    route: string
    filterLabel: string
    emptyText: string
    screenshotName: string
    wms: WmsInboundListPageProofInput | WmsOutboundListPageProofInput
  }>

  const samePageRoute = (currentUrl: string, route: string): boolean => {
    if (!currentUrl) return false
    const current = new URL(currentUrl)
    const expected = new URL(route, currentUrl)
    return (
      current.origin === expected.origin &&
      current.pathname === expected.pathname &&
      current.search === expected.search
    )
  }

  const establishPage = async (options: PageProofOptions): Promise<EstablishedPage> => {
    const runtime = options.actor === 'wms-worker' ? workerRuntime : adminRuntime
    const targetPage = runtime.page
    const reuseCurrentRoute = options.reuseCurrentRoute === true
    if (reuseCurrentRoute && !samePageRoute(targetPage.url(), options.route)) {
      throw new Error(`page ${options.route} cannot reuse the current route ${targetPage.url()}`)
    }
    let navigation: Response | null = null
    let firstList: Response | null = null
    let firstListNavigationEpoch: number | undefined

    if (reuseCurrentRoute) {
      if (
        !runtime.lastNavigationRoute ||
        !samePageRoute(runtime.lastNavigationRoute, options.route)
      ) {
        throw new Error(`page ${options.route} has no matching completed navigation to reuse`)
      }
      navigation = runtime.lastNavigationResponse
      if (!navigation || navigation.status() !== 200) {
        throw new Error(`page ${options.route} has no completed HTTP 200 navigation to reuse`)
      }
      firstList = runtime.successfulListResponses.get(options.listPath) ?? null
      firstListNavigationEpoch = runtime.lastNavigationEpoch ?? undefined
    } else {
      const navigationAttempt = runtime.requestFailureEvidence.beginLifecycleAttempt(
        targetPage.url(),
      )
      let navigationConfirmed = false
      try {
        const initialPage = await navigateAndWaitForInitialList(targetPage, {
          route: options.route,
          listPath: options.listPath,
          timeoutMs: 120_000,
        })
        navigation = initialPage.navigation
        firstList = initialPage.firstList
        firstListNavigationEpoch = initialPage.navigationEpoch
        runtime.lastNavigationResponse = navigation
        runtime.lastNavigationRoute = targetPage.url()
        runtime.lastNavigationEpoch = initialPage.navigationEpoch
        runtime.successfulListResponses.set(options.listPath, firstList)
        expect(navigation?.status(), `page ${options.route} must return HTTP 200`).toBe(200)
        expect(firstList.status(), `list ${options.listPath} must return HTTP 200`).toBe(200)
        navigationAttempt.confirm('navigation')
        navigationConfirmed = true
      } finally {
        if (navigationConfirmed) navigationAttempt.complete()
        else navigationAttempt.cancel()
      }
    }

    if (!navigation || !firstList) {
      throw new Error(`list ${options.listPath} has no completed HTTP 200 response to prove`)
    }
    return {
      runtime,
      targetPage,
      listPath: options.listPath,
      navigation,
      firstList,
      firstListNavigationEpoch,
    }
  }

  const initialListProofContext = (established: EstablishedPage): ListProofContext => ({
    page: established.targetPage,
    listPath: established.listPath,
    response: established.firstList,
    navigationEpoch: established.firstListNavigationEpoch,
  })

  const refreshListProofContext = async (
    established: EstablishedPage,
    listPath: string,
  ): Promise<ListProofContext> => {
    if (listPath !== established.listPath) {
      throw new Error(
        `refresh proof path ${listPath} did not match established list path ${established.listPath}`,
      )
    }
    // A data refresh is not a lifecycle transition: any API abort remains an unexpected failure.
    const response = await clickRefreshAndWaitForListResponse(established.targetPage, listPath)
    const navigationEpoch = established.runtime.lastNavigationEpoch ?? undefined
    established.runtime.successfulListResponses.set(listPath, response)
    return {
      page: established.targetPage,
      listPath,
      response,
      navigationEpoch,
    }
  }

  const completePageProof = async (
    node: NodeName,
    options: PageProofOptions,
    established: EstablishedPage,
    listProof: ListProofContext,
  ): Promise<UiProof> => {
    const { runtime, targetPage, navigation } = established
    if (listProof.page !== targetPage || listProof.listPath !== options.listPath) {
      throw new Error(
        `list proof context did not match established page/path for ${options.listPath}`,
      )
    }
    if (
      new URL(listProof.response.url()).pathname !== listProof.listPath ||
      listProof.response.request().frame() !== targetPage.mainFrame()
    ) {
      throw new Error(
        `list proof response was not emitted by the established page/path for ${options.listPath}`,
      )
    }
    const firstList = listProof.response
    let finalList = firstList
    const firstListNavigationEpoch = listProof.navigationEpoch

    expect(navigation.status(), `page ${options.route} must return HTTP 200`).toBe(200)
    expect(firstList.status(), `list ${options.listPath} must return HTTP 200`).toBe(200)

    if (options.filterLabel) {
      const filterResponseMode = options.filterResponseMode ?? 'server'
      const expectedListQueryFingerprint =
        filterResponseMode === 'server'
          ? options.expectedListQuery === undefined
            ? (() => {
                throw new Error(
                  `server filter proof for ${options.listPath} requires explicit expected list query facts`,
                )
              })()
            : listQueryFingerprint(queryPath(options.listPath, options.expectedListQuery))
          : undefined
      const filtered = await fillFilterAndWaitForListResponse(targetPage, {
        route: targetPage.url(),
        listPath: options.listPath,
        filterLabel: options.filterLabel,
        stableText: options.stableText,
        responseMode: filterResponseMode,
        initialListResponse: firstList,
        initialListNavigationEpoch: firstListNavigationEpoch,
        expectedListQueryFingerprint,
        timeoutMs: 120_000,
      })
      finalList = filtered.response ?? firstList
    }

    if (options.tabText) {
      await clickTabAndConfirmUnmount(targetPage, options.tabText, runtime.requestFailureEvidence)
    }

    for (const selectOption of options.selectOptions ?? []) {
      const listResponse = targetPage.waitForResponse(
        (response) => {
          const url = new URL(response.url())
          return (
            response.request().method() === 'GET' &&
            url.pathname === options.listPath &&
            response.status() === 200
          )
        },
        { timeout: 120_000 },
      )
      await targetPage.getByLabel(selectOption.label).click()
      await targetPage.getByRole('option', { name: selectOption.option, exact: true }).click()
      finalList = await listResponse
    }

    const row = targetPage.locator('tbody tr').filter({ hasText: options.stableText }).first()
    await expect(row, `page ${options.route} must render a stable business row`).toBeVisible({
      timeout: 120_000,
    })
    await expect(row).toContainText(options.stableText)
    await expect(targetPage.getByText(options.emptyText, { exact: true })).toHaveCount(0)
    const screenshot = join(screenshotDirectory, options.screenshotName)
    await targetPage.screenshot({ path: screenshot, fullPage: true })
    const proof: UiProof = {
      node,
      actor: runtime.actor,
      principalId: runtime.principalId,
      page: options.route,
      pageHttpStatus: navigation.status(),
      listPath: options.listPath,
      listHttpStatus: finalList.status(),
      listQuery: Object.fromEntries(new URL(finalList.url()).searchParams.entries()),
      stableKey: options.stableText,
      renderedRowText: safeText(await row.innerText()),
      emptyText: options.emptyText,
      screenshot,
    }
    uiEvidence.push(proof)
    return proof
  }

  const provePage = async (node: NodeName, options: PageProofOptions): Promise<UiProof> => {
    const established = await establishPage(options)
    const listProof = options.refreshListBeforeProof
      ? await refreshListProofContext(established, options.listPath)
      : initialListProofContext(established)
    return completePageProof(node, options, established, listProof)
  }

  const proveWmsPage = async (node: NodeName, options: WmsPageProofOptions): Promise<UiProof> => {
    assertWmsPageProofOptions(options)
    const listPath = options.wms.query.listPath
    const pageOptions: PageProofOptions = {
      actor: 'wms-worker',
      route: options.route,
      listPath,
      stableText: options.wms.query.keywordQuery.keyword,
      emptyText: options.emptyText,
      screenshotName: options.screenshotName,
    }
    const guarded = await withWmsInitialListResponseGuard(
      workerRuntime.page,
      listPath,
      () => establishPage(pageOptions),
      120_000,
      pageOptions.route,
    )
    const established = guarded.result
    if (established.firstList !== guarded.firstList) {
      throw new Error(
        `WMS initial list response was not bound to the first response for ${listPath}`,
      )
    }
    assertWmsInitialListResponse(
      { url: guarded.firstList.url(), status: guarded.firstList.status() },
      listPath,
    )
    const refreshedList = await proveWmsListPage({
      ...options.wms,
      page: established.targetPage,
    })
    const keywordList = await fillWmsKeywordAndConfirm(
      established.targetPage,
      options.wms.query,
      refreshedList,
      established.firstListNavigationEpoch,
      options.filterLabel,
    )
    established.runtime.successfulListResponses.set(listPath, keywordList)
    return completePageProof(node, pageOptions, established, {
      page: established.targetPage,
      listPath,
      response: keywordList,
      navigationEpoch: established.runtime.lastNavigationEpoch ?? undefined,
    })
  }

  const runProofSafely = async <T>(node: NodeName, proof: () => Promise<T>): Promise<T> => {
    try {
      return await proof()
    } catch (error) {
      markFailure(node, error, 'mixed')
      throw error
    }
  }

  const provePageSafely = (node: NodeName, options: PageProofOptions) =>
    runProofSafely(node, () => provePage(node, options))

  const proveWmsPageSafely = (node: NodeName, options: WmsPageProofOptions) =>
    runProofSafely(node, () => proveWmsPage(node, options))

  return { provePageSafely, proveWmsPageSafely }
}
