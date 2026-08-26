import type { Page } from '@playwright/test'

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
    !isApi
    && observation.failure === 'net::ERR_ABORTED'
    && EXPECTED_ABORT_RESOURCE_TYPES.has(observation.resourceType)
  const expectedApiAbort =
    isApi
    && observation.failure === 'net::ERR_ABORTED'
    && API_ABORT_RESOURCE_TYPES.has(observation.resourceType)
    && observation.cancellationEvidence?.requestStartedBeforeTransition === true
    && (observation.cancellationEvidence.kind === 'navigation'
      || observation.cancellationEvidence.kind === 'component-unmount')
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
  generation: number
  pageUrl: string
}

type LifecycleTransition = {
  event: number
  generation: number
  id: number
  kind: RequestCancellationKind
  pageUrl: string
}

export type LifecycleTransitionHandle = {
  id: number
  complete: () => void
}

export class RequestFailureEvidenceTracker {
  private event = 0

  private generation = 0

  private transitionId = 0

  private readonly requests = new WeakMap<object, ObservedRequest>()

  private readonly transitions: LifecycleTransition[] = []

  observeRequest(request: object, pageUrl: string): void {
    this.requests.set(request, {
      event: ++this.event,
      generation: this.generation,
      pageUrl,
    })
  }

  beginTransition(kind: RequestCancellationKind, pageUrl: string): LifecycleTransitionHandle {
    const transition: LifecycleTransition = {
      event: ++this.event,
      generation: ++this.generation,
      id: ++this.transitionId,
      kind,
      pageUrl,
    }
    this.transitions.push(transition)
    let completed = false
    return {
      id: transition.id,
      complete: () => {
        if (completed) return
        completed = true
        this.event += 1
      },
    }
  }

  cancellationEvidenceFor(request: object): RequestCancellationEvidence | undefined {
    const observed = this.requests.get(request)
    if (!observed) return undefined

    const failureEvent = ++this.event
    const transition = [...this.transitions]
      .reverse()
      .find(
        candidate =>
          candidate.pageUrl === observed.pageUrl
          && candidate.generation > observed.generation
          && candidate.event < failureEvent
          && observed.event < candidate.event,
      )
    if (!transition) return undefined

    return {
      kind: transition.kind,
      requestStartedBeforeTransition: true,
      transitionId: transition.id,
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
    response => (
      response.request().method() === 'GET'
      && new URL(response.url()).pathname === options.listPath
      && response.status() === 200
    ),
    { timeout: options.timeoutMs ?? 120_000 },
  )
  await filter.fill(options.stableText)
  await filteredListResponse
  return { waitedForResponse: true }
}
