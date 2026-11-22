import { type Page, type Locator, type Request, type Response } from '@playwright/test'

let actionMarkerSequence = 0
export const ACTION_MARKER_HEADER = 'x-nerv-walkthrough-action'
type ActionRequestMarkerOptions = {
  eventName: 'click' | 'input'
  expectedKeyword?: string
  expectedQueryFingerprint?: string
  listPath: string
  marker: string
}
export async function installActionRequestMarker(
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
      const metadata = xhrMetadata.get(this)
      const marked =
        metadata !== undefined &&
        state.active &&
        matchesActionRequest(metadata.method, metadata.url)
      if (marked) {
        this.setRequestHeader('x-nerv-walkthrough-action', markerOptions.marker)
        state.markedRequestCount += 1
      }
      originalXhrSend.call(this, body)
    }
    XMLHttpRequest.prototype.open = wrappedXhrOpen
    XMLHttpRequest.prototype.send = wrappedXhrSend
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
export function createActionResponseTracker(options: ActionResponseTrackerOptions) {
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
export async function waitForActionMarkerClosed(
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
export async function removeActionRequestMarker(page: Page, marker: string): Promise<void> {
  await page.evaluate((actionMarker) => {
    const markerWindow = window as Window & {
      __nervWalkthroughActionMarkers?: Record<string, { cleanup: () => void }>
    }
    markerWindow.__nervWalkthroughActionMarkers?.[actionMarker]?.cleanup()
  }, marker)
}
export function nextActionMarker(kind: string): string {
  actionMarkerSequence += 1
  return `__nerv_walkthrough_${kind}_${actionMarkerSequence}`
}
