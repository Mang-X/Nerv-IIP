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

export async function clickTabAndConfirmUnmount(
  page: Page,
  tabText: string | RegExp,
  tracker: RequestFailureEvidenceTracker,
  timeoutMs = 120_000,
): Promise<void> {
  const previousPanel = page.locator('[role="tabpanel"]:visible').first()
  const previousPanelHandle = await previousPanel.elementHandle()
  const attempt = tracker.beginLifecycleAttempt(page.url())
  let confirmed = false
  try {
    await page.getByRole('tab', { name: tabText }).click({ timeout: timeoutMs })
    if (!previousPanelHandle) {
      throw new Error('component unmount could not be evidenced: no visible tab panel')
    }
    await expect
      .poll(() => previousPanelHandle.evaluate((element) => element.isConnected), {
        timeout: timeoutMs,
      })
      .toBe(false)
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
  timeoutMs?: number
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

export async function fillFilterAndWaitForListResponse(
  page: Page,
  options: FilterResponseWaitOptions,
): Promise<{ waitedForResponse: boolean }> {
  const filter = page.getByLabel(options.filterLabel)
  const currentFilterValue = await filter.inputValue()
  if (isFilterAlreadyApplied(options.route, currentFilterValue, options.stableText)) {
    return { waitedForResponse: false }
  }

  const filteredListResponse = page.waitForResponse(
    (response) =>
      response.request().method() === 'GET' &&
      new URL(response.url()).pathname === options.listPath &&
      response.status() === 200,
    { timeout: options.timeoutMs ?? 120_000 },
  )
  await filter.fill(options.stableText)
  await filteredListResponse
  return { waitedForResponse: true }
}
