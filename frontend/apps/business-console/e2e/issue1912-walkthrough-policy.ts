import { expect, type Locator, type Page, type Request, type Response } from '@playwright/test'

const EXPECTED_ABORT_RESOURCE_TYPES = new Set([
  'document',
  'stylesheet',
  'script',
  'image',
  'font',
  'media',
  'manifest',
  'texttrack',
])

const API_ABORT_RESOURCE_TYPES = new Set(['fetch', 'xhr'])

const navigationEpochs = new WeakMap<Page, number>()
const listResponseOwnership = new WeakMap<Response, { page: Page; navigationEpoch: number }>()
const currentListResponses = new WeakMap<
  Page,
  Map<string, { response: Response; navigationEpoch: number }>
>()
let actionMarkerSequence = 0
const ACTION_MARKER_HEADER = 'x-nerv-walkthrough-action'

export type RequestCancellationKind = 'navigation' | 'component-unmount'

export type RequestCancellationEvidence = {
  kind: RequestCancellationKind
  requestStartedBeforeTransition: boolean
  transitionId: number
}

export type RequestFailureObservation = {
  method: string
  url: string
  failure: string
  resourceType: string
  isNavigationRequest: boolean
  cancellationEvidence?: RequestCancellationEvidence
}

export type RequestFailureClassification = {
  expected: boolean
  record: Record<string, unknown>
}

export function classifyRequestFailure(
  observation: RequestFailureObservation,
): RequestFailureClassification {
  const url = new URL(observation.url)
  const isApi = url.pathname.startsWith('/api/')
  const expectedDocumentOrResourceAbort =
    !isApi &&
    observation.failure === 'net::ERR_ABORTED' &&
    EXPECTED_ABORT_RESOURCE_TYPES.has(observation.resourceType)
  const expectedApiAbort =
    isApi &&
    observation.failure === 'net::ERR_ABORTED' &&
    API_ABORT_RESOURCE_TYPES.has(observation.resourceType) &&
    observation.cancellationEvidence?.requestStartedBeforeTransition === true &&
    (observation.cancellationEvidence.kind === 'navigation' ||
      observation.cancellationEvidence.kind === 'component-unmount')
  const expected = expectedDocumentOrResourceAbort || expectedApiAbort

  return {
    expected,
    record: {
      kind: 'requestfailed',
      method: observation.method,
      path: url.pathname + url.search,
      failure: observation.failure,
      resourceType: observation.resourceType,
      isNavigationRequest: observation.isNavigationRequest,
      ...(observation.cancellationEvidence
        ? { cancellationEvidence: observation.cancellationEvidence }
        : {}),
      classification: expected
        ? expectedApiAbort
          ? 'expected-superseded-api-request'
          : 'expected-superseded-document-or-resource'
        : isApi
          ? 'api-request-failure'
          : observation.isNavigationRequest
            ? 'page-navigation-failure'
            : 'resource-failure',
    },
  }
}

type ObservedRequest = {
  event: number
  pageUrl: string
}

type LifecycleAttemptState = 'pending' | 'active' | 'cancelled' | 'closed'

type PendingFailure = {
  onResolved: (evidence: RequestCancellationEvidence | undefined) => void
}

type LifecycleAttempt = {
  event: number
  id: number
  kind?: RequestCancellationKind
  pageUrl: string
  pendingFailures: PendingFailure[]
  state: LifecycleAttemptState
}

export type LifecycleAttemptHandle = {
  cancel: () => void
  complete: () => void
  confirm: (kind: RequestCancellationKind) => void
  id: number
}

export class RequestFailureEvidenceTracker {
  private event = 0

  private transitionId = 0

  private readonly requests = new WeakMap<object, ObservedRequest>()

  private readonly attempts = new Map<number, LifecycleAttempt>()

  observeRequest(request: object, pageUrl: string): void {
    this.requests.set(request, {
      event: ++this.event,
      pageUrl,
    })
  }

  beginLifecycleAttempt(pageUrl: string): LifecycleAttemptHandle {
    const attempt: LifecycleAttempt = {
      event: ++this.event,
      id: ++this.transitionId,
      pageUrl,
      pendingFailures: [],
      state: 'pending',
    }
    this.attempts.set(attempt.id, attempt)
    return {
      id: attempt.id,
      cancel: () => this.cancelAttempt(attempt),
      confirm: (kind) => this.confirmAttempt(attempt, kind),
      complete: () => this.completeAttempt(attempt),
    }
  }

  resolveFailureEvidence(
    request: object,
    onResolved: (evidence: RequestCancellationEvidence | undefined) => void,
  ): void {
    const observed = this.requests.get(request)
    if (!observed) {
      onResolved(undefined)
      return
    }

    ++this.event
    const activeAttempt = [...this.attempts.values()]
      .reverse()
      .find(
        (attempt) =>
          attempt.state === 'active' &&
          attempt.pageUrl === observed.pageUrl &&
          observed.event < attempt.event,
      )
    if (activeAttempt) {
      onResolved(this.evidenceFor(activeAttempt))
      return
    }

    const pendingAttempt = [...this.attempts.values()]
      .reverse()
      .find(
        (attempt) =>
          attempt.state === 'pending' &&
          attempt.pageUrl === observed.pageUrl &&
          observed.event < attempt.event,
      )
    if (pendingAttempt) {
      pendingAttempt.pendingFailures.push({ onResolved })
      return
    }

    onResolved(undefined)
  }

  private confirmAttempt(attempt: LifecycleAttempt, kind: RequestCancellationKind): void {
    if (attempt.state !== 'pending') return
    attempt.kind = kind
    attempt.state = 'active'
    ++this.event
    const pendingFailures = attempt.pendingFailures.splice(0)
    const evidence = this.evidenceFor(attempt)
    for (const failure of pendingFailures) failure.onResolved(evidence)
  }

  private cancelAttempt(attempt: LifecycleAttempt): void {
    if (attempt.state === 'cancelled' || attempt.state === 'closed') return
    attempt.state = 'cancelled'
    ++this.event
    const pendingFailures = attempt.pendingFailures.splice(0)
    for (const failure of pendingFailures) failure.onResolved(undefined)
    this.attempts.delete(attempt.id)
  }

  private completeAttempt(attempt: LifecycleAttempt): void {
    if (attempt.state === 'pending') {
      this.cancelAttempt(attempt)
      return
    }
    if (attempt.state !== 'active') return
    attempt.state = 'closed'
    ++this.event
    this.attempts.delete(attempt.id)
  }

  private evidenceFor(attempt: LifecycleAttempt): RequestCancellationEvidence {
    if (!attempt.kind) throw new Error('lifecycle attempt has no confirmed kind')
    return {
      kind: attempt.kind,
      requestStartedBeforeTransition: true,
      transitionId: attempt.id,
    }
  }
}

export type InitialPageNavigationOptions = {
  route: string
  listPath: string
  timeoutMs?: number
}

function isListRequest(request: Request, listPath: string): boolean {
  return request.method() === 'GET' && new URL(request.url()).pathname === listPath
}

function listQueryFingerprint(url: string): string {
  const entries = [...new URL(url).searchParams.entries()]
    .filter(([key]) => key !== 'keyword')
    .sort(([leftKey, leftValue], [rightKey, rightValue]) =>
      `${leftKey}\u0000${leftValue}`.localeCompare(`${rightKey}\u0000${rightValue}`),
    )
  return JSON.stringify(entries)
}

type ActionRequestMarkerOptions = {
  eventName: 'click' | 'input'
  expectedKeyword?: string
  expectedQueryFingerprint?: string
  listPath: string
  marker: string
}

async function installActionRequestMarker(
  page: Page,
  target: Locator,
  options: ActionRequestMarkerOptions,
): Promise<void> {
  // The marker is attached by the browser-side event boundary, not inferred from request order.
  // It covers event propagation plus two explicit microtask turns; requests outside that bounded
  // action window remain unmarked and therefore cannot satisfy the waiter.
  await target.evaluate((element, markerOptions) => {
    type ActionState = {
      actionCount: number
      active: boolean
      closedActionCount: number
      cleanup: () => void
      markedRequestCount: number
    }
    type WindowWithActionMarkers = Window & {
      __nervWalkthroughActionMarkers?: Record<string, ActionState>
    }

    const markerWindow = window as WindowWithActionMarkers
    const actionMarkers = (markerWindow.__nervWalkthroughActionMarkers ??= {})
    if (actionMarkers[markerOptions.marker]) {
      throw new Error(`duplicate walkthrough action marker ${markerOptions.marker}`)
    }

    const state: ActionState = {
      actionCount: 0,
      active: false,
      closedActionCount: 0,
      cleanup: () => undefined,
      markedRequestCount: 0,
    }
    const originalFetch = window.fetch
    const originalXhrOpen = XMLHttpRequest.prototype.open
    const originalXhrSend = XMLHttpRequest.prototype.send
    const xhrMetadata = new WeakMap<XMLHttpRequest, { method: string; url: string }>()

    const queryFingerprint = (url: string) => {
      const entries = [...new URL(url, location.href).searchParams.entries()]
        .filter(([key]) => key !== 'keyword')
        .sort(([leftKey, leftValue], [rightKey, rightValue]) =>
          `${leftKey}\u0000${leftValue}`.localeCompare(`${rightKey}\u0000${rightValue}`),
        )
      return JSON.stringify(entries)
    }
    const matchesActionRequest = (method: string, url: string) => {
      if (method.toUpperCase() !== 'GET') return false
      const parsed = new URL(url, location.href)
      if (parsed.pathname !== markerOptions.listPath) return false
      if (
        markerOptions.expectedKeyword !== undefined &&
        parsed.searchParams.get('keyword')?.trim() !== markerOptions.expectedKeyword.trim()
      ) {
        return false
      }
      return (
        markerOptions.expectedQueryFingerprint === undefined ||
        queryFingerprint(parsed.toString()) === markerOptions.expectedQueryFingerprint
      )
    }
    const actionIsArmed = () => state.active
    const markRequest = (headers: Headers) => {
      headers.set('x-nerv-walkthrough-action', markerOptions.marker)
      state.markedRequestCount += 1
    }

    window.fetch = function (input: RequestInfo | URL, init?: RequestInit) {
      let marked = false
      let markedInput: RequestInfo | URL = input
      let markedInit = init
      if (actionIsArmed()) {
        const request = new Request(input, init)
        if (matchesActionRequest(request.method, request.url)) {
          const headers = new Headers(request.headers)
          markRequest(headers)
          markedInput = new Request(request, { headers })
          markedInit = undefined
          marked = true
        }
      }

      const response = originalFetch.call(this, markedInput, markedInit)
      if (!marked) return response
      return response
    }

    XMLHttpRequest.prototype.open = function (
      method: string,
      url: string | URL,
      ...rest: unknown[]
    ) {
      xhrMetadata.set(this, { method, url: String(url) })
      return originalXhrOpen.apply(this, [method, url, ...rest] as never)
    }
    XMLHttpRequest.prototype.send = function (body?: Document | XMLHttpRequestBodyInit | null) {
      const metadata = xhrMetadata.get(this)
      const marked =
        metadata !== undefined &&
        actionIsArmed() &&
        matchesActionRequest(metadata.method, metadata.url)
      if (marked) {
        this.setRequestHeader('x-nerv-walkthrough-action', markerOptions.marker)
        state.markedRequestCount += 1
      }
      return originalXhrSend.call(this, body)
    }

    const activate = (event: Event) => {
      if (!event.composedPath().includes(element)) return
      state.active = true
      state.actionCount += 1
    }
    const closeAfterEventPropagation = (event: Event) => {
      if (!state.active || !event.composedPath().includes(element)) return
      const actionCountAtBoundary = state.actionCount
      // The boundary is the end of this event's propagation plus two explicit microtask turns.
      // No timer is included: a zero-delay timer is observably outside this action.
      queueMicrotask(() => {
        queueMicrotask(() => {
          if (state.active && state.actionCount === actionCountAtBoundary) {
            state.active = false
            state.closedActionCount = actionCountAtBoundary
          }
        })
      })
    }
    document.addEventListener(markerOptions.eventName, activate, { capture: true })
    document.addEventListener(markerOptions.eventName, closeAfterEventPropagation)

    state.cleanup = () => {
      document.removeEventListener(markerOptions.eventName, activate, { capture: true })
      document.removeEventListener(markerOptions.eventName, closeAfterEventPropagation)
      if (window.fetch === wrappedFetch) window.fetch = originalFetch
      if (XMLHttpRequest.prototype.open === wrappedXhrOpen) {
        XMLHttpRequest.prototype.open = originalXhrOpen
      }
      if (XMLHttpRequest.prototype.send === wrappedXhrSend) {
        XMLHttpRequest.prototype.send = originalXhrSend
      }
      delete actionMarkers[markerOptions.marker]
    }

    const wrappedFetch = window.fetch
    const wrappedXhrOpen = XMLHttpRequest.prototype.open
    const wrappedXhrSend = XMLHttpRequest.prototype.send
    actionMarkers[markerOptions.marker] = state
  }, options)
}

type ActionMarkerSnapshot = {
  actionCount: number
  markedRequestCount: number
}

async function waitForActionMarkerClosed(
  page: Page,
  marker: string,
  timeoutMs: number,
): Promise<ActionMarkerSnapshot> {
  await page.waitForFunction(
    (actionMarker) => {
      const markerWindow = window as Window & {
        __nervWalkthroughActionMarkers?: Record<
          string,
          { active: boolean; actionCount: number; closedActionCount: number }
        >
      }
      const state = markerWindow.__nervWalkthroughActionMarkers?.[actionMarker]
      return (
        state !== undefined &&
        state.actionCount > 0 &&
        !state.active &&
        state.closedActionCount === state.actionCount
      )
    },
    marker,
    { timeout: timeoutMs },
  )
  return page.evaluate((actionMarker) => {
    const markerWindow = window as Window & {
      __nervWalkthroughActionMarkers?: Record<
        string,
        { actionCount: number; markedRequestCount: number }
      >
    }
    const state = markerWindow.__nervWalkthroughActionMarkers?.[actionMarker]
    if (!state) throw new Error(`walkthrough action marker ${actionMarker} was removed early`)
    return { actionCount: state.actionCount, markedRequestCount: state.markedRequestCount }
  }, marker)
}

async function removeActionRequestMarker(page: Page, marker: string): Promise<void> {
  await page.evaluate((actionMarker) => {
    const markerWindow = window as Window & {
      __nervWalkthroughActionMarkers?: Record<string, { cleanup: () => void }>
    }
    markerWindow.__nervWalkthroughActionMarkers?.[actionMarker]?.cleanup()
  }, marker)
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

function nextActionMarker(kind: string): string {
  actionMarkerSequence += 1
  return `__nerv_walkthrough_${kind}_${actionMarkerSequence}`
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
  let refreshRequest: Request | undefined
  let ambiguousActionRequest = false
  let actionClosed = false
  let actionSnapshot: ActionMarkerSnapshot | undefined
  const actionRequests = new Set<Request>()
  const responsesByRequest = new Map<Request, Response>()
  let resolveRefreshedListResponse: (response: Response) => void = () => undefined
  let rejectRefreshedListResponse: (error: Error) => void = () => undefined
  let responseTimer: ReturnType<typeof setTimeout> | undefined
  const refreshedListResponse = new Promise<Response>((resolve, reject) => {
    resolveRefreshedListResponse = resolve
    rejectRefreshedListResponse = reject
  })
  const settleRefreshedListResponse = () => {
    if (!actionClosed || !refreshRequest || !actionSnapshot) return
    if (
      ambiguousActionRequest ||
      actionRequests.size !== 1 ||
      actionSnapshot.actionCount !== 1 ||
      actionSnapshot.markedRequestCount !== 1
    ) {
      rejectRefreshedListResponse(
        new Error(
          'refresh action emitted more than one marked list request; response ownership is ambiguous',
        ),
      )
      return
    }
    const response = responsesByRequest.get(refreshRequest)
    if (!response) return
    if (response.status() !== 200) {
      rejectRefreshedListResponse(
        new Error(`refresh action list request returned HTTP ${response.status()}`),
      )
      return
    }
    resolveRefreshedListResponse(response)
  }
  const requestObserver = (request: Request) => {
    if (
      !isListRequest(request, listPath) ||
      request.headers()[ACTION_MARKER_HEADER] !== actionMarker
    ) {
      return
    }
    actionRequests.add(request)
    if (refreshRequest) {
      ambiguousActionRequest = true
    } else {
      refreshRequest = request
    }
    settleRefreshedListResponse()
  }
  const responseObserver = (response: Response) => {
    const request = response.request()
    if (!isListRequest(request, listPath)) return
    responsesByRequest.set(request, response)
    settleRefreshedListResponse()
  }
  page.on('request', requestObserver)
  page.on('response', responseObserver)
  const refreshedListRequest = page.waitForRequest(
    (request) =>
      isListRequest(request, listPath) && request.headers()[ACTION_MARKER_HEADER] === actionMarker,
    { timeout: timeoutMs },
  )
  try {
    await refreshButton.click({ timeout: timeoutMs })
    const request = await refreshedListRequest
    if (request !== refreshRequest) {
      throw new Error(
        'refresh response was not bound to the completed request emitted by the refresh action',
      )
    }
    actionSnapshot = await waitForActionMarkerClosed(page, actionMarker, timeoutMs)
    actionClosed = true
    settleRefreshedListResponse()
    responseTimer = setTimeout(
      () => rejectRefreshedListResponse(new Error('refresh response timed out')),
      timeoutMs,
    )
    const response = await refreshedListResponse
    const finalActionSnapshot = await waitForActionMarkerClosed(page, actionMarker, timeoutMs)
    if (
      ambiguousActionRequest ||
      response.request() !== refreshRequest ||
      response.status() !== 200 ||
      actionRequests.size !== 1 ||
      finalActionSnapshot.actionCount !== 1 ||
      finalActionSnapshot.markedRequestCount !== 1
    ) {
      throw new Error(
        'refresh response was not bound to the completed request emitted by the refresh action',
      )
    }
    rememberListResponseOwnership(page, response)
    return response
  } finally {
    if (responseTimer) clearTimeout(responseTimer)
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
  timeoutMs?: number
}

export type FilterResponseWaitResult = {
  waitedForResponse: boolean
  reason: 'already-applied' | 'response-already-complete' | 'client-side-filter' | 'server-response'
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
): boolean {
  if (!isCurrentOwnedListResponse(page, response, listPath, navigationEpoch)) return false
  const url = new URL(response.url())
  return (
    normalizedFilterValue(url.searchParams.get('keyword') ?? '') ===
    normalizedFilterValue(stableText)
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
  const initialResponseMatchesCurrentFilter =
    normalizedFilterValue(currentFilterValue) === normalizedFilterValue(options.stableText) &&
    isMatchingListResponse(
      page,
      options.initialListResponse,
      options.listPath,
      options.stableText,
      options.initialListNavigationEpoch,
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
  const expectedQueryFingerprint = listQueryFingerprint(baselineResponse.url())
  const actionMarker = nextActionMarker('filter')
  // A non-200 response is terminal for this explicit fill. A later background request has no
  // fill-event ownership and is never inferred to be a retry.
  await installActionRequestMarker(page, filter, {
    eventName: 'input',
    expectedKeyword: options.stableText,
    expectedQueryFingerprint,
    listPath: options.listPath,
    marker: actionMarker,
  })
  let fillRequest: Request | undefined
  let ambiguousActionRequest = false
  const fillRequests = new Set<Request>()
  const responsesByRequest = new Map<Request, Response>()
  let actionClosed = false
  let actionSnapshot: ActionMarkerSnapshot | undefined
  let resolveFilteredListResponse: (response: Response) => void = () => undefined
  let rejectFilteredListResponse: (error: Error) => void = () => undefined
  let responseTimer: ReturnType<typeof setTimeout> | undefined
  const filteredListResponse = new Promise<Response>((resolve, reject) => {
    resolveFilteredListResponse = resolve
    rejectFilteredListResponse = reject
  })
  const settleFilteredListResponse = () => {
    if (!actionClosed || !fillRequest || !actionSnapshot) return
    if (
      ambiguousActionRequest ||
      fillRequests.size !== 1 ||
      actionSnapshot.actionCount !== 1 ||
      actionSnapshot.markedRequestCount !== 1
    ) {
      rejectFilteredListResponse(
        new Error(
          'filter response was not bound to the completed request emitted by the fill action',
        ),
      )
      return
    }
    const response = responsesByRequest.get(fillRequest)
    if (!response) return
    if (response.status() !== 200) {
      rejectFilteredListResponse(
        new Error(`filter action list request returned HTTP ${response.status()}`),
      )
      return
    }
    resolveFilteredListResponse(response)
  }
  const requestObserver = (request: Request) => {
    if (
      !isMatchingFilterRequest(
        request,
        options.listPath,
        options.stableText,
        expectedQueryFingerprint,
      ) ||
      request.headers()[ACTION_MARKER_HEADER] !== actionMarker
    ) {
      return
    }
    if (!fillRequest) {
      fillRequest = request
      fillRequests.add(request)
      return
    }
    fillRequests.add(request)
    ambiguousActionRequest = true
    settleFilteredListResponse()
  }
  const responseObserver = (response: Response) => {
    const request = response.request()
    if (
      !isMatchingFilterRequest(
        request,
        options.listPath,
        options.stableText,
        expectedQueryFingerprint,
      )
    ) {
      return
    }
    responsesByRequest.set(request, response)
    settleFilteredListResponse()
  }
  page.on('request', requestObserver)
  page.on('response', responseObserver)
  const filteredListRequest = page.waitForRequest(
    (request) =>
      isMatchingFilterRequest(
        request,
        options.listPath,
        options.stableText,
        expectedQueryFingerprint,
      ) && request.headers()[ACTION_MARKER_HEADER] === actionMarker,
    { timeout: timeoutMs },
  )
  try {
    await filter.fill(options.stableText)
    const request = await filteredListRequest
    if (request !== fillRequest) {
      throw new Error('filter response was not bound to the request emitted by the fill action')
    }
    actionSnapshot = await waitForActionMarkerClosed(page, actionMarker, timeoutMs)
    actionClosed = true
    settleFilteredListResponse()
    responseTimer = setTimeout(
      () => rejectFilteredListResponse(new Error('filter response timed out')),
      timeoutMs,
    )
    const response = await filteredListResponse
    const finalActionSnapshot = await waitForActionMarkerClosed(page, actionMarker, timeoutMs)
    if (
      ambiguousActionRequest ||
      response.request() !== fillRequest ||
      response.status() !== 200 ||
      fillRequests.size !== 1 ||
      finalActionSnapshot.actionCount !== 1 ||
      finalActionSnapshot.markedRequestCount !== 1
    ) {
      throw new Error(
        'filter response was not bound to the completed request emitted by the fill action',
      )
    }
    rememberListResponseOwnership(page, response)
    return { waitedForResponse: true, reason: 'server-response' }
  } finally {
    if (responseTimer) clearTimeout(responseTimer)
    page.off('request', requestObserver)
    page.off('response', responseObserver)
    await removeActionRequestMarker(page, actionMarker)
  }
}
