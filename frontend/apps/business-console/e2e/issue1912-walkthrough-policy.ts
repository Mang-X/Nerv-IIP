import { expect, type Page, type Request, type Response } from '@playwright/test'

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
let actionMarkerSequence = 0

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

function nextActionMarker(kind: string): string {
  actionMarkerSequence += 1
  return `__nerv_walkthrough_${kind}_${actionMarkerSequence}`
}

function isMatchingFilterRequest(request: Request, listPath: string, stableText: string): boolean {
  if (!isListRequest(request, listPath)) return false
  return (
    normalizedFilterValue(new URL(request.url()).searchParams.get('keyword') ?? '') ===
    normalizedFilterValue(stableText)
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
}

export async function navigateAndWaitForInitialList(
  page: Page,
  options: InitialPageNavigationOptions,
): Promise<{ firstList: Response; navigation: Response | null; navigationEpoch: number }> {
  const timeoutMs = options.timeoutMs ?? 120_000
  const navigationEpoch = startNavigationEpoch(page)
  let navigationStarted = false
  const navigationListRequests = new WeakSet<Request>()
  const requestObserver = (request: Request) => {
    if (navigationStarted && isListRequest(request, options.listPath)) {
      navigationListRequests.add(request)
    }
  }
  page.on('request', requestObserver)
  const initialListResponse = page.waitForResponse(
    (response) => navigationListRequests.has(response.request()) && response.status() === 200,
    { timeout: timeoutMs },
  )
  try {
    navigationStarted = true
    const navigation = await page.goto(options.route, {
      waitUntil: 'domcontentloaded',
      timeout: timeoutMs,
    })
    const firstList = await initialListResponse
    rememberListResponseOwnership(page, firstList, navigationEpoch)
    return { firstList, navigation, navigationEpoch }
  } finally {
    page.off('request', requestObserver)
  }
}

export async function clickRefreshAndWaitForListResponse(
  page: Page,
  listPath: string,
  timeoutMs = 120_000,
): Promise<Response> {
  const refreshButton = page.getByRole('button', { name: '刷新', exact: true })
  const actionMarker = nextActionMarker('refresh')
  await refreshButton.evaluate((element, marker) => {
    const listener = () => queueMicrotask(() => console.debug(marker))
    element.addEventListener('click', listener, { capture: true, once: true })
    ;(element as HTMLElement & Record<string, EventListener>)[marker] = listener
  }, actionMarker)
  const observedListRequests: Request[] = []
  const responsesByRequest = new Map<Request, Response>()
  const requestObserver = (request: Request) => {
    if (isListRequest(request, listPath)) observedListRequests.push(request)
  }
  const responseObserver = (response: Response) => {
    if (isListRequest(response.request(), listPath)) {
      responsesByRequest.set(response.request(), response)
    }
  }
  let refreshRequest: Request | undefined
  let resolveRefreshedListResponse: (response: Response) => void = () => undefined
  let rejectRefreshedListResponse: (error: Error) => void = () => undefined
  let responseTimer: ReturnType<typeof setTimeout> | undefined
  const refreshedListResponse = new Promise<Response>((resolve, reject) => {
    resolveRefreshedListResponse = resolve
    rejectRefreshedListResponse = reject
  })
  page.on('request', requestObserver)
  const responseObserverWithOwnership = (response: Response) => {
    responseObserver(response)
    if (
      refreshRequest !== undefined &&
      response.request() === refreshRequest &&
      response.status() === 200
    ) {
      resolveRefreshedListResponse(response)
    }
  }
  page.on('response', responseObserverWithOwnership)
  const requestsBeforeAction = new Set(observedListRequests)
  const actionMarkerEvent = page.waitForEvent('console', {
    predicate: (message) => message.text() === actionMarker,
    timeout: timeoutMs,
  })
  const refreshedListRequest = page.waitForRequest(
    (request) => isListRequest(request, listPath) && !requestsBeforeAction.has(request),
    { timeout: timeoutMs },
  )
  try {
    await refreshButton.click({ timeout: timeoutMs })
    await actionMarkerEvent
    await refreshedListRequest
    await page.waitForTimeout(0)
    const actionRequests = observedListRequests.filter(
      (request) => !requestsBeforeAction.has(request),
    )
    refreshRequest = actionRequests.at(-1)
    if (!refreshRequest) {
      throw new Error('refresh action did not emit a list request')
    }
    const observedResponse = responsesByRequest.get(refreshRequest)
    if (observedResponse?.status() === 200) {
      resolveRefreshedListResponse(observedResponse)
    } else {
      responseTimer = setTimeout(
        () => rejectRefreshedListResponse(new Error('refresh response timed out')),
        timeoutMs,
      )
    }
    const response = await refreshedListResponse
    if (response.request() !== refreshRequest || response.status() !== 200) {
      throw new Error(
        'refresh response was not bound to the completed request emitted by the refresh action',
      )
    }
    rememberListResponseOwnership(page, response)
    return response
  } finally {
    if (responseTimer) clearTimeout(responseTimer)
    page.off('request', requestObserver)
    page.off('response', responseObserverWithOwnership)
    await refreshButton.evaluate((element, marker) => {
      const typedElement = element as HTMLElement & Record<string, EventListener>
      const listener = typedElement[marker]
      if (listener) {
        element.removeEventListener('click', listener, { capture: true })
        delete typedElement[marker]
      }
    }, actionMarker)
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
    response.status() === 200 &&
    normalizedFilterValue(url.searchParams.get('keyword') ?? '') ===
      normalizedFilterValue(stableText)
  )
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
  let fillStarted = false
  let fillRequest: Request | undefined
  let retryRequestAllowed = false
  const fillRequests = new WeakSet<Request>()
  const requestObserver = (request: Request) => {
    if (!fillStarted || !isMatchingFilterRequest(request, options.listPath, options.stableText)) {
      return
    }
    if (!fillRequest) {
      fillRequest = request
      fillRequests.add(request)
      return
    }
    if (retryRequestAllowed) {
      fillRequests.add(request)
      retryRequestAllowed = false
    }
  }
  page.on('request', requestObserver)
  const filteredListRequest = page.waitForRequest((request) => fillRequests.has(request), {
    timeout: timeoutMs,
  })
  const filteredListResponse = page.waitForResponse(
    (response) => {
      const request = response.request()
      if (!fillRequests.has(request)) return false
      if (response.status() >= 400) {
        retryRequestAllowed = true
        return false
      }
      return response.status() === 200
    },
    { timeout: timeoutMs },
  )
  try {
    fillStarted = true
    await filter.fill(options.stableText)
    const request = await filteredListRequest
    if (request !== fillRequest) {
      throw new Error('filter response was not bound to the request emitted by the fill action')
    }
    await filteredListResponse
    return { waitedForResponse: true, reason: 'server-response' }
  } finally {
    page.off('request', requestObserver)
  }
}
