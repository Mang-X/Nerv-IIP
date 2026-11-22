import { type Page, type Response, type Request, expect } from '@playwright/test'
import {
  type WmsInboundPageSelection,
  type WmsOutboundPageSelection,
  assertWmsInboundSelectionMatchesQuery,
  selectWmsScopeOption,
  selectWmsPageOption,
  assertWmsOutboundSelectionMatchesQuery,
  selectWmsPageWindow,
} from './selection'
import { type WmsWalkthroughPageWindowInput } from '../issue1912-wms-walkthrough-authority'
import {
  type WmsInboundListQueryProof,
  type WmsOutboundListQueryProof,
  assertExpectedQueryFacts,
  assertWmsPageWindow,
  type WmsListQueryProof,
  assertWmsListQueryFacts,
} from './query-facts'
import {
  clickRefreshAndWaitForListResponse,
  listQueryFingerprint,
  fillFilterAndWaitForListResponse,
} from '../issue1912-walkthrough-policy'
import { queryPath } from '../issue1912-walkthrough-query'

export type WmsListPageProofOptions =
  | Readonly<{
      kind: 'inbound'
      page: Page
      selection: WmsInboundPageSelection
      pageWindow: WmsWalkthroughPageWindowInput
      query: WmsInboundListQueryProof
    }>
  | Readonly<{
      kind: 'outbound'
      page: Page
      selection: WmsOutboundPageSelection
      pageWindow: WmsWalkthroughPageWindowInput
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
  assertWmsPageWindow(options.pageWindow)
  if (
    options.pageWindow.skip !== options.query.selectionQuery.skip ||
    options.pageWindow.take !== options.query.selectionQuery.take
  ) {
    throw new Error('WMS page window input did not match the selection query facts')
  }

  if (options.kind === 'inbound') {
    assertWmsInboundSelectionMatchesQuery(options.selection, options.query.selectionQuery)
    await selectWmsScopeOption(options.page, options.selection.scope)
    await selectWmsPageOption(options.page, options.selection.site)
  } else {
    assertWmsOutboundSelectionMatchesQuery(options.selection, options.query.selectionQuery)
    await selectWmsScopeOption(options.page, options.selection.scope)
  }

  const selectedPageWindow = await selectWmsPageWindow(options.page, options.pageWindow)
  if (
    selectedPageWindow.skip !== options.query.selectionQuery.skip ||
    selectedPageWindow.take !== options.query.selectionQuery.take
  ) {
    throw new Error('WMS page window readback did not match the selection query facts')
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
