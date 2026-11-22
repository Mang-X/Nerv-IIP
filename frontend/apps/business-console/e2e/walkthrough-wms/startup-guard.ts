import {
  type WmsListPath,
  WMS_INBOUND_LIST_PATH,
  WMS_OUTBOUND_LIST_PATH,
  type WmsListResponseFacts,
} from './query-facts'
import { type Page, type Response, type Request } from '@playwright/test'

type WmsInitialEndpointKind = 'target' | 'auxiliary' | 'unknown' | 'outside'

/**
 * A mounted page has a page-specific WMS startup surface. The registry is keyed by the expected
 * target path so an outbound catalog request can be auxiliary on inbound, while the inverse path
 * remains an unexpected request on outbound. Exact paths only are accepted; nested, trailing-slash
 * and otherwise unregistered WMS paths stay fail-closed.
 */
const wmsInitialEndpointRegistry: Readonly<
  Record<WmsListPath, Readonly<Record<string, Exclude<WmsInitialEndpointKind, 'outside'>>>>
> = {
  [WMS_INBOUND_LIST_PATH]: {
    [WMS_INBOUND_LIST_PATH]: 'target',
    [WMS_OUTBOUND_LIST_PATH]: 'auxiliary',
    '/api/business-console/v1/wms/work-scopes/receipts': 'auxiliary',
    '/api/business-console/v1/wms/operational-candidates/receipts': 'auxiliary',
    '/api/business-console/v1/wms/putaway-tasks': 'auxiliary',
    '/api/business-console/v1/wms/picking-tasks': 'auxiliary',
    '/api/business-console/v1/wms/count-executions': 'auxiliary',
    '/api/business-console/v1/wms/receiving-quality-gates': 'auxiliary',
    '/api/business-console/v1/wms/supplier-return-requests': 'auxiliary',
  },
  [WMS_OUTBOUND_LIST_PATH]: {
    [WMS_OUTBOUND_LIST_PATH]: 'target',
    '/api/business-console/v1/wms/work-scopes/shipments': 'auxiliary',
    '/api/business-console/v1/wms/operational-candidates/shipments': 'auxiliary',
    '/api/business-console/v1/wms/putaway-tasks': 'auxiliary',
    '/api/business-console/v1/wms/picking-tasks': 'auxiliary',
    '/api/business-console/v1/wms/count-executions': 'auxiliary',
  },
}

/**
 * 首个列表请求属于页面建立阶段，是目标端点和状态的公开生命周期证据，但不是选择证据：
 * 精确的范围/工厂事实必须由显式选择后的 action-bound 刷新证明。
 */
export function assertWmsInitialListResponse(
  response: WmsListResponseFacts,
  listPath: WmsListPath,
): void {
  if (response.status !== 200) {
    throw new Error(`WMS initial list ${listPath} returned HTTP ${response.status}`)
  }
  const actualPath = new URL(response.url).pathname
  if (actualPath !== listPath) {
    throw new Error(`WMS initial list response path ${actualPath} did not match ${listPath}`)
  }
}

function classifyWmsInitialEndpoint(
  expectedPath: WmsListPath,
  path: string,
): WmsInitialEndpointKind {
  if (!path.startsWith('/api/business-console/v1/wms/')) return 'outside'
  return wmsInitialEndpointRegistry[expectedPath][path] ?? 'unknown'
}

type WmsInitialGuardContext = Readonly<{
  navigationRoute: string
  documentAlreadyCommitted: boolean
}>

async function observeWmsInitialListResponse<T>(
  page: Page,
  expectedPath: WmsListPath,
  action: () => Promise<T>,
  timeoutMs: number,
  context: WmsInitialGuardContext,
): Promise<Readonly<{ result: T; firstList: Response }>> {
  const { navigationRoute } = context
  let resolveFirst!: (response: Response) => void
  let rejectFirst!: (error: unknown) => void
  const firstResponse = new Promise<Response>((resolve, reject) => {
    resolveFirst = resolve
    rejectFirst = reject
  })
  let firstCandidateSeen = false
  let documentCommitted = context.documentAlreadyCommitted
  let firstListRequest: Request | undefined
  const failFirst = (message: string) => {
    if (firstCandidateSeen) return
    firstCandidateSeen = true
    rejectFirst(new Error(message))
  }
  const frameNavigationObserver = (frame: { url: () => string }) => {
    if (frame !== page.mainFrame()) return
    const frameUrl = frame.url()
    if (!frameUrl || frameUrl === 'about:blank') return
    const current = new URL(frameUrl)
    const expected = new URL(navigationRoute, current.toString())
    documentCommitted = current.pathname === expected.pathname && current.search === expected.search
  }
  const refreshDocumentCommitState = () => {
    const currentUrl = page.url()
    if (documentCommitted || !currentUrl || currentUrl === 'about:blank') return
    const current = new URL(currentUrl)
    const expected = new URL(navigationRoute, current.toString())
    documentCommitted = current.pathname === expected.pathname && current.search === expected.search
  }
  const classifyRequest = (request: Request): WmsInitialEndpointKind =>
    classifyWmsInitialEndpoint(expectedPath, new URL(request.url()).pathname)
  const failForUnexpectedRequest = (
    request: Request,
    endpointKind: WmsInitialEndpointKind,
  ): boolean => {
    const path = new URL(request.url()).pathname
    if (endpointKind === 'unknown') {
      failFirst(`unexpected WMS list-like request path ${path}`)
      return true
    }
    if (request.method() !== 'GET') {
      failFirst(`unexpected WMS initial list request method ${request.method()}`)
      return true
    }
    return false
  }
  const requestObserver = (request: Request) => {
    if (firstCandidateSeen) return
    refreshDocumentCommitState()
    if (!documentCommitted || request.frame() !== page.mainFrame()) return
    const endpointKind = classifyRequest(request)
    if (endpointKind === 'outside') return
    if (failForUnexpectedRequest(request, endpointKind)) return
    if (endpointKind === 'auxiliary') return
    firstListRequest ??= request
  }
  const responseObserver = (response: Response) => {
    if (firstCandidateSeen) return
    refreshDocumentCommitState()
    if (!documentCommitted) return
    const request = response.request()
    if (request.frame() !== page.mainFrame()) return
    const path = new URL(response.url()).pathname
    const endpointKind = classifyWmsInitialEndpoint(expectedPath, path)
    if (endpointKind === 'outside') return
    if (failForUnexpectedRequest(request, endpointKind)) return
    if (endpointKind === 'unknown') return
    if (endpointKind === 'auxiliary') {
      if (response.status() !== 200) {
        failFirst(`WMS initial auxiliary ${path} returned HTTP ${response.status()}`)
      }
      return
    }
    try {
      assertWmsInitialListResponse({ url: response.url(), status: response.status() }, expectedPath)
    } catch (error) {
      firstCandidateSeen = true
      rejectFirst(error)
      return
    }
    if (firstListRequest && request !== firstListRequest) return
    firstListRequest ??= request
    firstCandidateSeen = true
    resolveFirst(response)
  }
  const requestFailedObserver = (request: Request) => {
    if (firstCandidateSeen) return
    refreshDocumentCommitState()
    if (!documentCommitted || request.frame() !== page.mainFrame()) return
    const endpointKind = classifyRequest(request)
    if (endpointKind === 'outside') return
    if (failForUnexpectedRequest(request, endpointKind)) return
    if (endpointKind === 'target') firstListRequest ??= request
    const path = new URL(request.url()).pathname
    const failure = request.failure()?.errorText ?? 'unknown network failure'
    failFirst(`WMS initial ${endpointKind} ${path} request failed: ${failure}`)
  }
  const timeout = setTimeout(
    () => rejectFirst(new Error(`WMS initial list ${expectedPath} response was not observed`)),
    timeoutMs,
  )
  page.on('response', responseObserver)
  page.on('request', requestObserver)
  page.on('requestfailed', requestFailedObserver)
  page.on('framenavigated', frameNavigationObserver)
  try {
    const actionResult = action()
    const [result, firstList] = await Promise.all([actionResult, firstResponse])
    return { result, firstList }
  } finally {
    clearTimeout(timeout)
    page.off('response', responseObserver)
    page.off('request', requestObserver)
    page.off('requestfailed', requestFailedObserver)
    page.off('framenavigated', frameNavigationObserver)
  }
}

/**
 * Adds the NERV-1571 lifecycle boundary around an existing navigation action. The generic
 * walkthrough policy intentionally waits for a successful target response; this guard observes
 * the first WMS list response itself so an initial 503 or wrong WMS path cannot be hidden by a
 * later 200. It is deliberately an action wrapper, keeping the NERV-1456 policy unchanged.
 */
export async function withWmsInitialListResponseGuard<T>(
  page: Page,
  expectedPath: WmsListPath,
  action: () => Promise<T>,
  timeoutMs: number,
  navigationRoute: string,
): Promise<Readonly<{ result: T; firstList: Response }>> {
  return observeWmsInitialListResponse(page, expectedPath, action, timeoutMs, {
    navigationRoute,
    documentAlreadyCommitted: false,
  })
}

/**
 * Fixture-only lifecycle wrapper. It is intentionally separate from the production navigation
 * wrapper: synthetic tests start from an already committed document, while real walkthroughs must
 * always provide their exact navigation route to the guard above.
 */
export async function withWmsInitialListResponseGuardForTest<T>(
  page: Page,
  expectedPath: WmsListPath,
  action: () => Promise<T>,
  timeoutMs = 120_000,
): Promise<Readonly<{ result: T; firstList: Response }>> {
  return observeWmsInitialListResponse(page, expectedPath, action, timeoutMs, {
    navigationRoute: page.url(),
    documentAlreadyCommitted: true,
  })
}
