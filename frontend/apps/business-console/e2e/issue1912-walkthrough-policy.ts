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
const NAVIGATION_MARKER_HEADER = 'x-nerv-walkthrough-navigation'
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
type ObservedRequest = { event: number; pageUrl: string }
type LifecycleAttemptState = 'pending' | 'active' | 'cancelled' | 'closed'
type PendingFailure = { onResolved: (evidence: RequestCancellationEvidence | undefined) => void }
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

export type InitialPageNavigationOptions = { route: string; listPath: string; timeoutMs?: number }
function isListRequest(request: Request, listPath: string): boolean {
  return request.method() === 'GET' && new URL(request.url()).pathname === listPath
}
export function listQueryFingerprint(url: string): string {
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
  // Capture the fetch function during event propagation. Generated clients select globalThis.fetch
  // before async auth/interceptor continuations, so this function carries the action identity past
  // the event boundary without keeping a mutable global marker active for later timers.
  await target.evaluate((element, markerOptions) => {
    type ActionState = {
      actionCount: number
      active: boolean
      closedActionCount: number
      cleanup: () => void
      markedRequestCount: number
      actionFetch?: typeof window.fetch
      fetchBeforeAction?: typeof window.fetch
      disposed: boolean
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
      disposed: false,
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
    const markRequest = (headers: Headers) => {
      headers.set('x-nerv-walkthrough-action', markerOptions.marker)
      state.markedRequestCount += 1
    }

    const createActionFetch = () => {
      const actionFetch = function (
        this: typeof window,
        input: RequestInfo | URL,
        init?: RequestInit,
      ) {
        let markedInput: RequestInfo | URL = input
        let markedInit = init
        const request = new Request(input, init)
        if (!state.disposed && matchesActionRequest(request.method, request.url)) {
          const headers = new Headers(request.headers)
          markRequest(headers)
          markedInput = new Request(request, { headers })
          markedInit = undefined
        }

        return originalFetch.call(this, markedInput, markedInit)
      }
      return actionFetch
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
        state.active &&
        matchesActionRequest(metadata.method, metadata.url)
      if (marked) {
        this.setRequestHeader('x-nerv-walkthrough-action', markerOptions.marker)
        state.markedRequestCount += 1
      }
      return originalXhrSend.call(this, body)
    }

    const activate = (event: Event) => {
      if (!event.composedPath().includes(element)) return
      if (!state.active) state.fetchBeforeAction = window.fetch
      state.active = true
      state.actionCount += 1
      const actionFetch = createActionFetch()
      state.actionFetch = actionFetch
      window.fetch = actionFetch
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
            if (state.fetchBeforeAction && window.fetch === state.actionFetch) {
              window.fetch = state.fetchBeforeAction
            }
            state.actionFetch = undefined
            state.fetchBeforeAction = undefined
          }
        })
      })
    }
    document.addEventListener(markerOptions.eventName, activate, { capture: true })
    document.addEventListener(markerOptions.eventName, closeAfterEventPropagation)

    state.cleanup = () => {
      state.disposed = true
      document.removeEventListener(markerOptions.eventName, activate, { capture: true })
      document.removeEventListener(markerOptions.eventName, closeAfterEventPropagation)
      if (state.fetchBeforeAction && window.fetch === state.actionFetch) {
        window.fetch = state.fetchBeforeAction
      } else if (window.fetch === originalFetch) {
        window.fetch = originalFetch
      }
      if (XMLHttpRequest.prototype.open === wrappedXhrOpen) {
        XMLHttpRequest.prototype.open = originalXhrOpen
      }
      if (XMLHttpRequest.prototype.send === wrappedXhrSend) {
        XMLHttpRequest.prototype.send = originalXhrSend
      }
      delete actionMarkers[markerOptions.marker]
    }

    const wrappedXhrOpen = XMLHttpRequest.prototype.open
    const wrappedXhrSend = XMLHttpRequest.prototype.send
    actionMarkers[markerOptions.marker] = state
  }, options)
}
type ActionMarkerSnapshot = { actionCount: number; markedRequestCount: number }

type ActionResponseTrackerOptions = {
  requestMatches: (request: Request) => boolean
  responseMatches: (response: Response) => boolean
  timeoutMs: number
  requestTimeoutMessage: string
  ambiguousMessage: string
  bindingMessage: string
  statusMessage: (status: number) => string
}
function createActionResponseTracker(options: ActionResponseTrackerOptions) {
  const actionRequests = new Set<Request>()
  const responsesByRequest = new Map<Request, Response>()
  let actionRequest: Request | undefined
  let ambiguous = false
  let actionClosed = false
  let actionSnapshot: ActionMarkerSnapshot | undefined
  let resolveFirstRequest: (request: Request) => void = () => undefined
  let rejectFirstRequest: (error: Error) => void = () => undefined
  let resolveResponse: (response: Response) => void = () => undefined
  let rejectResponse: (error: Error) => void = () => undefined
  let firstRequestTimer: ReturnType<typeof setTimeout> | undefined
  let responseTimer: ReturnType<typeof setTimeout> | undefined
  const firstRequest = new Promise<Request>((resolve, reject) => {
    resolveFirstRequest = resolve
    rejectFirstRequest = reject
    firstRequestTimer = setTimeout(
      () => rejectFirstRequest(new Error(options.requestTimeoutMessage)),
      options.timeoutMs,
    )
  })
  const response = new Promise<Response>((resolve, reject) => {
    resolveResponse = resolve
    rejectResponse = reject
  })
  const settle = () => {
    if (!actionClosed || !actionRequest || !actionSnapshot) return
    if (
      ambiguous ||
      actionRequests.size !== 1 ||
      actionSnapshot.actionCount !== 1 ||
      actionSnapshot.markedRequestCount !== 1
    ) {
      rejectResponse(new Error(options.ambiguousMessage))
      return
    }
    const completedResponse = responsesByRequest.get(actionRequest)
    if (!completedResponse) return
    if (completedResponse.status() !== 200) {
      rejectResponse(new Error(options.statusMessage(completedResponse.status())))
      return
    }
    resolveResponse(completedResponse)
  }
  return {
    firstRequest,
    response,
    observeRequest: (request: Request) => {
      if (!options.requestMatches(request)) return
      actionRequests.add(request)
      if (!actionRequest) {
        actionRequest = request
        if (firstRequestTimer) clearTimeout(firstRequestTimer)
        resolveFirstRequest(request)
      } else {
        ambiguous = true
      }
      settle()
    },
    observeResponse: (completedResponse: Response) => {
      if (!options.responseMatches(completedResponse)) return
      responsesByRequest.set(completedResponse.request(), completedResponse)
      settle()
    },
    close: (snapshot: ActionMarkerSnapshot) => {
      actionSnapshot = snapshot
      actionClosed = true
      settle()
    },
    armResponseTimeout: () => {
      responseTimer = setTimeout(
        () => rejectResponse(new Error('action response timed out')),
        options.timeoutMs,
      )
    },
    clearTimers: () => {
      if (firstRequestTimer) clearTimeout(firstRequestTimer)
      if (responseTimer) clearTimeout(responseTimer)
    },
    assert: (completedResponse: Response, finalSnapshot: ActionMarkerSnapshot) => {
      if (
        ambiguous ||
        completedResponse.request() !== actionRequest ||
        completedResponse.status() !== 200 ||
        actionRequests.size !== 1 ||
        finalSnapshot.actionCount !== 1 ||
        finalSnapshot.markedRequestCount !== 1
      ) {
        throw new Error(options.bindingMessage)
      }
    },
  }
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
    XMLHttpRequest.prototype.open = function (
      method: string,
      url: string | URL,
      ...rest: unknown[]
    ) {
      xhrMetadata.set(this, { method, url: String(url) })
      return originalXhrOpen.apply(this, [method, url, ...rest] as never)
    }
    XMLHttpRequest.prototype.send = function (body?: Document | XMLHttpRequestBodyInit | null) {
      if (xhrMetadata.has(this)) {
        this.setRequestHeader('x-nerv-walkthrough-navigation', token)
      }
      return originalXhrSend.call(this, body)
    }
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
    return { waitedForResponse: true, reason: 'server-response' }
  } finally {
    tracker.clearTimers()
    page.off('request', requestObserver)
    page.off('response', responseObserver)
    await removeActionRequestMarker(page, actionMarker)
  }
}
