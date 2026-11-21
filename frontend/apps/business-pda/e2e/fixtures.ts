import { type Route } from '@playwright/test'
import { fulfillJson, envelope } from './fixtures/http'
import { workerProfile } from './fixtures/auth'
import { routeWms } from './fixtures/wms'
import { routeEquipment } from './fixtures/equipment'
import { routeMes } from './fixtures/mes'

/**
 * Mock the business-console gateway. MES list/action/create endpoints the
 * operation + report flows hit get realistic envelopes, and the equipment
 * maintenance/alarms endpoints the repair/inspect/alarms pages hit get realistic
 * envelopes (item shapes mirror BusinessConsoleMaintenanceWorkOrderItem,
 * BusinessConsoleMaintenancePlan*, BusinessConsoleMaintenanceInspectionItem and
 * EquipmentRuntimeAlarmSummary so the pages render real Chinese labels). WMS
 * lists return `{ items, total }`; completes return a bare success.
 *
 * Any unmatched path falls back (does NOT fake-succeed) so a future un-mocked /
 * mistyped endpoint surfaces loudly instead of being silently swallowed (aligns
 * with routeConsoleApi). Every endpoint a spec hits must be explicitly mocked here.
 */
function routeShared(
  route: Route,
  requestUrl: URL,
  pathname: string,
  method: string,
  isPost: boolean,
) {
  const reportSku = pathname.match(
    /^\/api\/business-console\/v1\/master-data\/resources\/sku\/([^/]+)$/,
  )
  if (method === 'GET' && reportSku) {
    return fulfillJson(
      route,
      envelope({
        resourceType: 'sku',
        code: decodeURIComponent(reportSku[1]),
        active: true,
        serialTrackingPolicy: 'none',
      }),
    )
  }

  if (isPost && pathname === '/api/business-console/v1/barcode/resolve') {
    return fulfillJson(
      route,
      envelope({ status: 'unknown', reasonCode: 'NO_MATCH', candidates: [], total: 0 }),
    )
  }

  if (method === 'GET' && pathname === '/api/business-console/v1/search') {
    return fulfillJson(route, envelope({ query: requestUrl.searchParams.get('q'), results: [] }))
  }
}
function routeHome(route: Route, pathname: string) {
  if (pathname === '/api/business-console/v1/master-data/workers') {
    return fulfillJson(
      route,
      envelope({ pageIndex: 1, pageSize: 1, totalCount: 1, items: [workerProfile] }),
    )
  }
  if (pathname === '/api/business-console/v1/quality/inspection-tasks') {
    return fulfillJson(route, envelope({ items: [], total: 0 }))
  }
}

export async function routeBusinessConsoleApi(route: Route) {
  const requestUrl = new URL(route.request().url())
  const { pathname } = requestUrl
  const method = route.request().method()
  const isPost = method === 'POST'

  return (
    routeShared(route, requestUrl, pathname, method, isPost) ??
    routeWms(route, requestUrl, pathname, isPost) ??
    routeEquipment(route, requestUrl, pathname, method) ??
    routeHome(route, pathname) ??
    routeMes(route, requestUrl, pathname, method) ??
    route.fallback()
  )
}

export {
  STORAGE_KEY,
  principal,
  workerProfile,
  session,
  routeConsoleApi,
  seedStoredSession,
} from './fixtures/auth'
export { CREATED_MAINTENANCE_WORK_ORDER_ID, DOWNTIME_REASON_DIRECTORY } from './fixtures/equipment'
export { envelope } from './fixtures/http'
export { expectNoHorizontalOverflow, expectTouchTargets } from './fixtures/layout'
export {
  authorizedWorkScopes,
  mesDispatchTasks,
  mesMaterialIssueRequests,
  mesOperationTasks,
  mesReceiptRequests,
  mesWorkOrders,
  productionReportReceipt,
} from './fixtures/mes'
