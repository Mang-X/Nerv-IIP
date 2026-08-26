import { expect, type Page, type Response } from '@playwright/test'

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

export async function navigateAndWaitForInitialList(
  page: Page,
  options: InitialPageNavigationOptions,
): Promise<{ firstList: Response; navigation: Response | null }> {
  const timeoutMs = options.timeoutMs ?? 120_000
  const initialListResponse = page.waitForResponse(
    (response) =>
      response.request().method() === 'GET' &&
      new URL(response.url()).pathname === options.listPath &&
      response.status() === 200,
    { timeout: timeoutMs },
  )
  const navigation = await page.goto(options.route, {
    waitUntil: 'domcontentloaded',
    timeout: timeoutMs,
  })
  const firstList = await initialListResponse
  return { firstList, navigation }
}

export async function clickRefreshAndWaitForListResponse(
  page: Page,
  listPath: string,
  timeoutMs = 120_000,
): Promise<Response> {
  const refreshedListRequest = page.waitForRequest(
    (request) => request.method() === 'GET' && new URL(request.url()).pathname === listPath,
    { timeout: timeoutMs },
  )
  await page.getByRole('button', { name: '刷新', exact: true }).click({ timeout: timeoutMs })
  const request = await refreshedListRequest
  return page.waitForResponse(
    (response) => response.request() === request && response.status() === 200,
    { timeout: timeoutMs },
  )
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
  response: Response | undefined,
  listPath: string,
  stableText: string,
): boolean {
  if (!response) return false
  const url = new URL(response.url())
  return (
    response.request().method() === 'GET' &&
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
  if (isFilterAlreadyApplied(options.route, currentFilterValue, options.stableText)) {
    return { waitedForResponse: false, reason: 'already-applied' }
  }

  if (
    options.responseMode === 'server' &&
    normalizedFilterValue(currentFilterValue) === normalizedFilterValue(options.stableText) &&
    isMatchingListResponse(options.initialListResponse, options.listPath, options.stableText)
  ) {
    return { waitedForResponse: false, reason: 'response-already-complete' }
  }

  if (options.responseMode === 'client') {
    await filter.fill(options.stableText)
    return { waitedForResponse: false, reason: 'client-side-filter' }
  }

  const filteredListResponse = page.waitForResponse(
    (response) =>
      response.request().method() === 'GET' &&
      new URL(response.url()).pathname === options.listPath &&
      normalizedFilterValue(new URL(response.url()).searchParams.get('keyword') ?? '') ===
        normalizedFilterValue(options.stableText) &&
      response.status() === 200,
    { timeout: options.timeoutMs ?? 120_000 },
  )
  await filter.fill(options.stableText)
  await filteredListResponse
  return { waitedForResponse: true, reason: 'server-response' }
}
