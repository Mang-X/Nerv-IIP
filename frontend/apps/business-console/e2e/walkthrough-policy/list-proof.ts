import { type Page, type Response, type Request, expect } from '@playwright/test'
import {
  nextActionMarker,
  installActionRequestMarker,
  createActionResponseTracker,
  ACTION_MARKER_HEADER,
  waitForActionMarkerClosed,
  removeActionRequestMarker,
} from './action-markers'
import { RequestFailureEvidenceTracker } from './request-failures'

const navigationEpochs = new WeakMap<Page, number>()
const listResponseOwnership = new WeakMap<Response, { page: Page; navigationEpoch: number }>()
const currentListResponses = new WeakMap<
  Page,
  Map<string, { response: Response; navigationEpoch: number }>
>()
const NAVIGATION_MARKER_HEADER = 'x-nerv-walkthrough-navigation'

export type InitialPageNavigationOptions = { route: string; listPath: string; timeoutMs?: number }
function isListRequest(request: Request, listPath: string): boolean {
  return request.method() === 'GET' && new URL(request.url()).pathname === listPath
}
export function listQueryFingerprint(url: string): string {
  const entries = [...new URL(url, 'http://walkthrough.expected').searchParams.entries()]
    .filter(([key]) => key !== 'keyword')
    .sort(([leftKey, leftValue], [rightKey, rightValue]) =>
      `${leftKey}\u0000${leftValue}`.localeCompare(`${rightKey}\u0000${rightValue}`),
    )
  return JSON.stringify(entries)
}
function routePathAndSearch(route: string): { pathname: string; search: string } {
  const url = new URL(route, 'http://walkthrough.fixture')
  return { pathname: url.pathname, search: url.search }
}
function isNavigationRequestForRoute(page: Page, request: Request, route: string): boolean {
  if (!request.isNavigationRequest() || request.frame() !== page.mainFrame()) return false
  const expected = routePathAndSearch(route)
  const url = new URL(request.url())
  return url.pathname === expected.pathname && url.search === expected.search
}
function isFrameAtRoute(frame: { url: () => string }, route: string): boolean {
  const expected = routePathAndSearch(route)
  const url = new URL(frame.url())
  return url.pathname === expected.pathname && url.search === expected.search
}
function isMatchingFilterRequest(
  request: Request,
  listPath: string,
  stableText: string,
  expectedQueryFingerprint?: string,
): boolean {
  if (!isListRequest(request, listPath)) return false
  const matchesKeyword =
    normalizedFilterValue(new URL(request.url()).searchParams.get('keyword') ?? '') ===
    normalizedFilterValue(stableText)
  return (
    matchesKeyword &&
    (expectedQueryFingerprint === undefined ||
      listQueryFingerprint(request.url()) === expectedQueryFingerprint)
  )
}
function startNavigationEpoch(page: Page): number {
  const navigationEpoch = (navigationEpochs.get(page) ?? 0) + 1
  navigationEpochs.set(page, navigationEpoch)
  return navigationEpoch
}

async function installNavigationRequestIdentity(
  page: Page,
  navigationToken: string,
): Promise<void> {
  await page.addInitScript(() => {
    const token = window.name
    if (!token.startsWith('__nerv_walkthrough_navigation_')) return
    type WindowWithNavigationMarker = Window & {
      __nervWalkthroughNavigationMarker?: true
    }
    const markerWindow = window as WindowWithNavigationMarker
    if (markerWindow.__nervWalkthroughNavigationMarker) return
    markerWindow.__nervWalkthroughNavigationMarker = true
    const originalFetch = window.fetch
    window.fetch = function (input: RequestInfo | URL, init?: RequestInit) {
      const request = new Request(input, init)
      const headers = new Headers(request.headers)
      headers.set('x-nerv-walkthrough-navigation', token)
      return originalFetch.call(this, new Request(request, { headers }))
    }
    const originalXhrOpen = XMLHttpRequest.prototype.open
    const originalXhrSend = XMLHttpRequest.prototype.send
    const xhrMetadata = new WeakMap<XMLHttpRequest, { method: string; url: string }>()
    type XhrOpen = typeof XMLHttpRequest.prototype.open
    const wrappedXhrOpen: XhrOpen = function (
      this: XMLHttpRequest,
      method: string,
      url: string | URL,
      async: boolean = true,
      username?: string | null,
      password?: string | null,
    ): void {
      xhrMetadata.set(this, { method, url: String(url) })
      originalXhrOpen.call(this, method, url, async, username, password)
    }
    const wrappedXhrSend: typeof XMLHttpRequest.prototype.send = function (
      this: XMLHttpRequest,
      body?: Document | XMLHttpRequestBodyInit | null,
    ): void {
      if (xhrMetadata.has(this)) {
        this.setRequestHeader('x-nerv-walkthrough-navigation', token)
      }
      originalXhrSend.call(this, body)
    }
    XMLHttpRequest.prototype.open = wrappedXhrOpen
    XMLHttpRequest.prototype.send = wrappedXhrSend
  })
  await page.evaluate((token) => {
    window.name = token
  }, navigationToken)
}

function rememberListResponseOwnership(
  page: Page,
  response: Response,
  navigationEpoch = navigationEpochs.get(page),
): void {
  if (navigationEpoch === undefined) return
  listResponseOwnership.set(response, { page, navigationEpoch })
  const listPath = new URL(response.url()).pathname
  const responses = currentListResponses.get(page) ?? new Map()
  responses.set(listPath, { response, navigationEpoch })
  currentListResponses.set(page, responses)
}
export async function navigateAndWaitForInitialList(
  page: Page,
  options: InitialPageNavigationOptions,
): Promise<{ firstList: Response; navigation: Response | null; navigationEpoch: number }> {
  const timeoutMs = options.timeoutMs ?? 120_000
  const navigationEpoch = startNavigationEpoch(page)
  const navigationToken = nextActionMarker('navigation')
  await installNavigationRequestIdentity(page, navigationToken)
  let navigationRequest: Request | undefined
  let documentCommitted = false
  const navigationListRequests = new WeakSet<Request>()
  const requestObserver = (request: Request) => {
    if (isNavigationRequestForRoute(page, request, options.route)) {
      navigationRequest = request
    }
    // A request from the previous document can share the path and keyword while the next
    // navigation is still pending. Only the committed target frame can own this epoch's list.
    if (
      documentCommitted &&
      isListRequest(request, options.listPath) &&
      request.headers()[NAVIGATION_MARKER_HEADER] === navigationToken &&
      request.frame() === page.mainFrame() &&
      isFrameAtRoute(request.frame(), options.route)
    ) {
      navigationListRequests.add(request)
    }
  }
  const frameNavigationObserver = (frame: { url: () => string }) => {
    if (frame === page.mainFrame() && isFrameAtRoute(frame, options.route)) {
      documentCommitted = true
    }
  }
  page.on('request', requestObserver)
  page.on('framenavigated', frameNavigationObserver)
  const initialListResponse = page.waitForResponse(
    (response) => navigationListRequests.has(response.request()) && response.status() === 200,
    { timeout: timeoutMs },
  )
  try {
    const navigation = await page.goto(options.route, {
      waitUntil: 'domcontentloaded',
      timeout: timeoutMs,
    })
    if (
      !navigationRequest &&
      navigation &&
      isNavigationRequestForRoute(page, navigation.request(), options.route)
    ) {
      navigationRequest = navigation.request()
    }
    if (!navigationRequest) {
      throw new Error('navigation did not emit the requested main-frame document request')
    }
    if (!documentCommitted) {
      throw new Error('navigation did not commit the requested main-frame document')
    }
    const firstList = await initialListResponse
    rememberListResponseOwnership(page, firstList, navigationEpoch)
    return { firstList, navigation, navigationEpoch }
  } finally {
    page.off('request', requestObserver)
    page.off('framenavigated', frameNavigationObserver)
  }
}
export async function clickRefreshAndWaitForListResponse(
  page: Page,
  listPath: string,
  timeoutMs = 120_000,
): Promise<Response> {
  const refreshButton = page.getByRole('button', { name: '刷新', exact: true })
  const actionMarker = nextActionMarker('refresh')
  await installActionRequestMarker(page, refreshButton, {
    eventName: 'click',
    listPath,
    marker: actionMarker,
  })
  const tracker = createActionResponseTracker({
    requestMatches: (request) =>
      isListRequest(request, listPath) && request.headers()[ACTION_MARKER_HEADER] === actionMarker,
    responseMatches: (response) => isListRequest(response.request(), listPath),
    timeoutMs,
    requestTimeoutMessage: 'refresh action did not emit a marked list request',
    ambiguousMessage:
      'refresh action emitted more than one marked list request; response ownership is ambiguous',
    bindingMessage:
      'refresh response was not bound to the completed request emitted by the refresh action',
    statusMessage: (status) => `refresh action list request returned HTTP ${status}`,
  })
  const requestObserver = tracker.observeRequest
  const responseObserver = tracker.observeResponse
  page.on('request', requestObserver)
  page.on('response', responseObserver)
  try {
    await refreshButton.click({ timeout: timeoutMs })
    await tracker.firstRequest
    tracker.close(await waitForActionMarkerClosed(page, actionMarker, timeoutMs))
    tracker.armResponseTimeout()
    const response = await tracker.response
    const finalActionSnapshot = await waitForActionMarkerClosed(page, actionMarker, timeoutMs)
    tracker.assert(response, finalActionSnapshot)
    rememberListResponseOwnership(page, response)
    return response
  } finally {
    tracker.clearTimers()
    page.off('request', requestObserver)
    page.off('response', responseObserver)
    await removeActionRequestMarker(page, actionMarker)
  }
}
export async function clickTabAndConfirmUnmount(
  page: Page,
  tabText: string | RegExp,
  tracker: RequestFailureEvidenceTracker,
  timeoutMs = 120_000,
): Promise<void> {
  const previousPanel = page.locator('[role="tabpanel"]:visible').first()
  const previousPanelHandle = await previousPanel.elementHandle()
  const previousContentCount = previousPanelHandle
    ? await previousPanelHandle.evaluate((element) => element.children.length)
    : 0
  const attempt = tracker.beginLifecycleAttempt(page.url())
  let confirmed = false
  try {
    await page.getByRole('tab', { name: tabText }).click({ timeout: timeoutMs })
    if (!previousPanelHandle) {
      throw new Error('component unmount could not be evidenced: no visible tab panel')
    }
    if (previousContentCount === 0) {
      throw new Error('component unmount could not be evidenced: tab panel has no content')
    }
    await expect
      .poll(
        () =>
          previousPanelHandle.evaluate(
            (element) =>
              element.getAttribute('data-state') === 'inactive' && element.hasAttribute('hidden'),
          ),
        { timeout: timeoutMs },
      )
      .toBe(true)
    await expect
      .poll(() => previousPanelHandle.evaluate((element) => element.children.length), {
        timeout: timeoutMs,
      })
      .toBe(0)
    attempt.confirm('component-unmount')
    confirmed = true
  } finally {
    if (confirmed) {
      attempt.complete()
    } else {
      attempt.cancel()
    }
  }
}
export type FilterResponseWaitOptions = {
  route: string
  listPath: string
  filterLabel: string
  stableText: string
  responseMode: 'server' | 'client'
  initialListResponse?: Response
  initialListNavigationEpoch?: number
  expectedListQueryFingerprint?: string
  timeoutMs?: number
}

export type FilterResponseWaitResult = {
  waitedForResponse: boolean
  reason: 'already-applied' | 'response-already-complete' | 'client-side-filter' | 'server-response'
  response?: Response
}

function normalizedFilterValue(value: string): string {
  return value.trim()
}

export function isFilterAlreadyApplied(
  route: string,
  currentFilterValue: string,
  stableText: string,
): boolean {
  const expected = normalizedFilterValue(stableText)
  if (normalizedFilterValue(currentFilterValue) !== expected) return false

  const routeKeyword = new URL(route, 'http://walkthrough.fixture').searchParams.get('keyword')
  return routeKeyword !== null && normalizedFilterValue(routeKeyword) === expected
}

function isMatchingListResponse(
  page: Page,
  response: Response | undefined,
  listPath: string,
  stableText: string,
  navigationEpoch: number | undefined,
  expectedQueryFingerprint?: string,
): boolean {
  if (!isCurrentOwnedListResponse(page, response, listPath, navigationEpoch)) return false
  if (expectedQueryFingerprint === undefined) return false
  const url = new URL(response.url())
  return (
    normalizedFilterValue(url.searchParams.get('keyword') ?? '') ===
      normalizedFilterValue(stableText) &&
    listQueryFingerprint(response.url()) === expectedQueryFingerprint
  )
}

function isOwnedListResponse(
  page: Page,
  response: Response | undefined,
  listPath: string,
  navigationEpoch: number | undefined,
): response is Response {
  if (!response) return false
  const ownership = listResponseOwnership.get(response)
  if (
    navigationEpoch === undefined ||
    navigationEpochs.get(page) !== navigationEpoch ||
    ownership?.page !== page ||
    ownership.navigationEpoch !== navigationEpoch
  ) {
    return false
  }
  const url = new URL(response.url())
  return (
    isListRequest(response.request(), listPath) &&
    url.pathname === listPath &&
    response.status() === 200
  )
}

function isCurrentOwnedListResponse(
  page: Page,
  response: Response | undefined,
  listPath: string,
  navigationEpoch: number | undefined,
): response is Response {
  if (!isOwnedListResponse(page, response, listPath, navigationEpoch)) return false
  const current = currentListResponses.get(page)?.get(listPath)
  return current?.response === response && current.navigationEpoch === navigationEpoch
}

export async function fillFilterAndWaitForListResponse(
  page: Page,
  options: FilterResponseWaitOptions,
): Promise<FilterResponseWaitResult> {
  const filter = page.getByLabel(options.filterLabel)
  const currentFilterValue = await filter.inputValue()
  const expectedQueryFingerprint = options.expectedListQueryFingerprint
  const initialResponseMatchesCurrentFilter =
    normalizedFilterValue(currentFilterValue) === normalizedFilterValue(options.stableText) &&
    isMatchingListResponse(
      page,
      options.initialListResponse,
      options.listPath,
      options.stableText,
      options.initialListNavigationEpoch,
      expectedQueryFingerprint,
    )
  if (
    isFilterAlreadyApplied(options.route, currentFilterValue, options.stableText) &&
    initialResponseMatchesCurrentFilter
  ) {
    return { waitedForResponse: false, reason: 'already-applied' }
  }

  if (options.responseMode === 'server' && initialResponseMatchesCurrentFilter) {
    return { waitedForResponse: false, reason: 'response-already-complete' }
  }

  if (options.responseMode === 'client') {
    await filter.fill(options.stableText)
    return { waitedForResponse: false, reason: 'client-side-filter' }
  }

  const timeoutMs = options.timeoutMs ?? 120_000
  const currentListResponse = currentListResponses.get(page)?.get(options.listPath)
  const baselineResponse = isCurrentOwnedListResponse(
    page,
    options.initialListResponse,
    options.listPath,
    options.initialListNavigationEpoch,
  )
    ? options.initialListResponse
    : currentListResponse !== undefined &&
        currentListResponse.navigationEpoch === navigationEpochs.get(page)
      ? currentListResponse.response
      : undefined
  if (
    !baselineResponse ||
    !isCurrentOwnedListResponse(
      page,
      baselineResponse,
      options.listPath,
      navigationEpochs.get(page),
    )
  ) {
    throw new Error(
      'server filter requires an owned HTTP 200 initial list response from the current navigation',
    )
  }
  const actionQueryFingerprint =
    expectedQueryFingerprint ?? listQueryFingerprint(baselineResponse.url())
  const actionMarker = nextActionMarker('filter')
  // A non-200 response is terminal for this explicit fill. A later background request has no
  // fill-event ownership and is never inferred to be a retry.
  await installActionRequestMarker(page, filter, {
    eventName: 'input',
    expectedKeyword: options.stableText,
    expectedQueryFingerprint: actionQueryFingerprint,
    listPath: options.listPath,
    marker: actionMarker,
  })
  const tracker = createActionResponseTracker({
    requestMatches: (request) =>
      isMatchingFilterRequest(
        request,
        options.listPath,
        options.stableText,
        actionQueryFingerprint,
      ) && request.headers()[ACTION_MARKER_HEADER] === actionMarker,
    responseMatches: (response) =>
      isMatchingFilterRequest(
        response.request(),
        options.listPath,
        options.stableText,
        actionQueryFingerprint,
      ),
    timeoutMs,
    requestTimeoutMessage: 'filter action did not emit a marked list request',
    ambiguousMessage:
      'filter response was not bound to the completed request emitted by the fill action',
    bindingMessage:
      'filter response was not bound to the completed request emitted by the fill action',
    statusMessage: (status) => `filter action list request returned HTTP ${status}`,
  })
  const requestObserver = tracker.observeRequest
  const responseObserver = tracker.observeResponse
  page.on('request', requestObserver)
  page.on('response', responseObserver)
  try {
    await filter.fill(options.stableText)
    await tracker.firstRequest
    tracker.close(await waitForActionMarkerClosed(page, actionMarker, timeoutMs))
    tracker.armResponseTimeout()
    const response = await tracker.response
    const finalActionSnapshot = await waitForActionMarkerClosed(page, actionMarker, timeoutMs)
    tracker.assert(response, finalActionSnapshot)
    rememberListResponseOwnership(page, response)
    return { waitedForResponse: true, reason: 'server-response', response }
  } finally {
    tracker.clearTimers()
    page.off('request', requestObserver)
    page.off('response', responseObserver)
    await removeActionRequestMarker(page, actionMarker)
  }
}
