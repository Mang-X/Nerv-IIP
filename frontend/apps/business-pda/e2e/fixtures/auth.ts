import { type Route, type Page } from '@playwright/test'
import { fulfillJson, envelope } from './http'

export const STORAGE_KEY = 'nerv-iip.business-pda.auth'

export const principal = {
  principalId: 'principal-1',
  principalType: 'User',
  loginName: 'operator01',
  email: 'operator01@example.test',
  organizationId: 'org-001',
  environmentId: 'env-dev',
  permissionVersion: 1,
  // 首页按权限裁剪板块/应用墙 —— e2e 主体给全量 PDA 读写权限（等价产线+仓储+质检复合）。
  permissionCodes: [
    'business.mes.dispatch.read',
    'business.mes.work-orders.read',
    'business.mes.operations.read',
    'business.mes.reporting.read',
    'business.mes.materials.read',
    'business.mes.receipts.read',
    'business.wms.receipts.read',
    'business.wms.shipments.read',
    'business.wms.counts.read',
    'business.inventory.counts.manage',
    'business.quality.inspection-records.read',
    'business.iiot.alarms.read',
    'business.maintenance.work-orders.read',
    'business.maintenance.plans.read',
    'business.masterdata.resources.read',
    'business.barcodes.read',
    'business.barcodes.scans.write',
  ],
  roleIds: [],
}

/** 首页身份行的员工目录档案（master-data workers 精确查 userId 命中）。 */
export const workerProfile = {
  userId: principal.principalId,
  employeeNo: 'EMP-012',
  displayName: '李秀英',
  jobTitle: '操作工',
  employmentStatus: 'active',
  active: true,
  snapshotVersion: 'worker-v1',
  teams: [{ teamCode: 'TEAM-WB-AS-A', teamName: '装配车间早班组' }],
  skills: [],
}

export const session = {
  accessToken: 'access-token',
  refreshToken: 'refresh-token',
  sessionId: 'session-1',
  expiresAtUtc: '2099-01-01T00:00:00.000Z',
  principal,
}

/** Mock the console auth endpoints the PDA app depends on (login + refresh + me). */
export async function routeConsoleApi(route: Route) {
  const { pathname } = new URL(route.request().url())
  if (pathname === '/api/console/v1/auth/login' || pathname === '/api/console/v1/auth/refresh') {
    return fulfillJson(route, envelope(session))
  }
  if (pathname === '/api/console/v1/auth/me') {
    return fulfillJson(route, envelope(principal))
  }
  if (pathname === '/api/console/v1/auth/logout') {
    return fulfillJson(route, envelope({}))
  }
  // Don't fake-succeed unmatched paths — fall back so a future un-mocked endpoint
  // surfaces loudly instead of being silently swallowed (aligns with console e2e).
  return route.fallback()
}

/** Seed a stored session so guarded routes load without going through the login form. */
export async function seedStoredSession(page: Page, colorMode?: 'light' | 'dark') {
  await page.addInitScript(
    ({ key, stored, mode }) => {
      localStorage.setItem(key, JSON.stringify(stored))
      if (mode) localStorage.setItem('nerv-iip-color-mode', mode)
    },
    {
      key: STORAGE_KEY,
      stored: { principal, refreshToken: session.refreshToken, sessionId: session.sessionId },
      mode: colorMode,
    },
  )
}
