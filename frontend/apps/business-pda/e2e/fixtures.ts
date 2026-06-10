import { expect, type Page, type Route } from '@playwright/test'

export const STORAGE_KEY = 'nerv-iip.business-pda.auth'

export const principal = {
  principalId: 'principal-1',
  principalType: 'User',
  loginName: 'operator01',
  email: 'operator01@example.test',
  organizationId: 'org-001',
  environmentId: 'env-dev',
  permissionVersion: 1,
}

export const session = {
  accessToken: 'access-token',
  refreshToken: 'refresh-token',
  sessionId: 'session-1',
  expiresAtUtc: '2099-01-01T00:00:00.000Z',
  principal,
}

export function envelope<T>(data: T) {
  return { success: true, message: null, data }
}

async function fulfillJson(route: Route, body: unknown, status = 200) {
  await route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) })
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

/**
 * Mock the business-console gateway calls the PDA equipment pages make.
 *
 * Item shapes mirror the real generated row types (BusinessConsoleMaintenanceWorkOrderItem,
 * BusinessConsoleMaintenancePlan*, BusinessConsoleMaintenanceInspectionItem,
 * EquipmentRuntimeAlarmSummary) so the pages render realistic Chinese labels (severity/priority/
 * status → 中文 via business-core equipmentLabels). Unmatched paths fall back to the default
 * empty envelope (the home + foundation pages need nothing more).
 */
export async function routeBusinessConsoleApi(route: Route) {
  const { pathname } = new URL(route.request().url())
  const method = route.request().method()

  // 报修：维修工单 list / create
  if (pathname === '/api/business-console/v1/maintenance/work-orders') {
    if (method === 'POST') {
      return fulfillJson(route, envelope({ workOrderId: 'WO-M-new' }))
    }
    return fulfillJson(
      route,
      envelope({
        items: [
          {
            workOrderId: 'WO-M1',
            deviceAssetId: 'DEV-A',
            priority: 'high',
            status: 'open',
            openedBy: principal.loginName,
            openedAtUtc: '2026-06-10T01:00:00.000Z',
          },
        ],
        total: 1,
      }),
    )
  }

  // 点检：保养计划 list（点检页先选计划）
  if (pathname === '/api/business-console/v1/maintenance/plans') {
    return fulfillJson(
      route,
      envelope({
        items: [
          {
            planId: 'PLAN-1',
            planCode: 'PM-001',
            deviceAssetId: 'DEV-A',
            interval: 'P7D',
          },
        ],
        total: 1,
      }),
    )
  }

  // 点检：记录 list（空）/ record
  if (pathname === '/api/business-console/v1/maintenance/inspections') {
    if (method === 'POST') {
      return fulfillJson(route, envelope({ inspectionId: 'INS-new' }))
    }
    return fulfillJson(route, envelope({ items: [], total: 0 }))
  }

  // 报警查看（只读）
  if (pathname === '/api/business-console/v1/equipment/alarms') {
    return fulfillJson(
      route,
      envelope({
        items: [
          {
            alarmEventId: 'ALM-1',
            deviceAssetId: 'DEV-A',
            alarmCode: 'E-101',
            severity: 'critical',
            raisedAtUtc: '2026-06-10T02:30:00.000Z',
          },
        ],
        total: 1,
      }),
    )
  }

  return fulfillJson(route, envelope({}))
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

export async function expectNoHorizontalOverflow(page: Page) {
  const overflow = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
  )
  expect(overflow).toBeLessThanOrEqual(1)
}

/**
 * Every enabled interactive control must meet the 44px touch-target floor.
 *
 * Query set covers real-world interactive shapes — including `<div role="button">`
 * (e.g. ListRow) and `role="link"`, which are the forms most likely to render too
 * small. Disabled controls (native `:disabled` or `aria-disabled="true"`) are excluded.
 *
 * Pass rule — deliberately strict so a large layout container can't mask a genuinely
 * small control:
 *  - The control's OWN box meets the floor (>=44 in both dimensions) → pass.
 *  - ONLY for a bare `<input>` declared full-width (CSS `width:100%`, i.e. a flex
 *    full-row input) whose parent row is >=44px tall does the row supply the tap
 *    surface (e.g. ScanBar, where the input shares a 48px `min-h-touch` row with a
 *    decorative icon). The width-intent check is read from the declared style, not the
 *    rendered box, so an icon/padding sibling can't fail it — yet a small `role=button`
 *    tucked inside a tall wrapper is NOT excused.
 *  - Everything else (including role=button / div) is judged by its own box.
 */
export async function expectTouchTargets(page: Page) {
  const tooSmall = await page.evaluate(() => {
    const FLOOR = 44
    const els = [
      ...document.querySelectorAll<HTMLElement>(
        'button:not([disabled]), a[href], input, [role="button"]:not([aria-disabled="true"]), [role="link"]',
      ),
    ]
    return els
      .map((el) => {
        const own = el.getBoundingClientRect()
        let effW = own.width
        let effH = own.height
        // Legal full-row input: a full-width (`width:100%`) <input> whose >=44px-tall
        // parent row carries the tap surface. Restricted to <input> + declared
        // full-width intent so it cannot excuse a small control inside a tall wrapper.
        if (el.tagName === 'INPUT' && el.parentElement) {
          const row = el.parentElement.getBoundingClientRect()
          const declaredFullWidth = getComputedStyle(el).width === '100%' || el.classList.contains('w-full')
          if (row.height >= FLOOR && own.width > 0 && declaredFullWidth) {
            effW = Math.max(own.width, row.width)
            effH = row.height
          }
        }
        return { tag: el.tagName, role: el.getAttribute('role'), w: effW, h: effH }
      })
      .filter((m) => m.w > 0 && m.h > 0 && (m.w < FLOOR || m.h < FLOOR))
  })
  expect(tooSmall).toEqual([])
}
