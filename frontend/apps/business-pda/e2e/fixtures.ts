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

/** A `{ items, total }` list payload wrapped in the standard success envelope. */
function listEnvelope<T>(items: T[]) {
  return envelope({ items, total: items.length })
}

const nowUtc = '2026-06-11T00:00:00.000Z'

// Realistic WMS row shapes mirroring the real api-client item types — just enough
// fields for the PDA pages to render business codes + Chinese status (no raw codes/GUIDs).
const inboundOrders = [
  { inboundOrderId: 'in-1', inboundOrderNo: 'IN-1', status: 'pending', createdAtUtc: nowUtc },
  { inboundOrderId: 'in-2', inboundOrderNo: 'IN-2', status: 'pending', createdAtUtc: nowUtc },
]

const outboundOrders = [
  { outboundOrderId: 'out-1', outboundOrderNo: 'OUT-1', status: 'pending', createdAtUtc: nowUtc },
  { outboundOrderId: 'out-2', outboundOrderNo: 'OUT-2', status: 'pending', createdAtUtc: nowUtc },
]

const pickingTasks = [
  {
    warehouseTaskId: 'wt-pk-1',
    taskNo: 'PK-1',
    taskType: 'picking',
    sourceOrderNo: 'OUT-1',
    skuCode: 'SKU-1',
    fromLocationCode: 'A1',
    toLocationCode: 'B2',
    plannedQuantity: 10,
    status: 'pending',
    createdAtUtc: nowUtc,
  },
]

const putawayTasks = [
  {
    warehouseTaskId: 'wt-pa-1',
    taskNo: 'PA-1',
    taskType: 'putaway',
    sourceOrderNo: 'IN-1',
    skuCode: 'SKU-1',
    fromLocationCode: 'A1',
    toLocationCode: 'B2',
    plannedQuantity: 10,
    status: 'pending',
    createdAtUtc: nowUtc,
  },
]

const countExecutions = [
  {
    countExecutionId: 'ce-1',
    countNo: 'CN-1',
    skuCode: 'SKU-1',
    locationCode: 'A1',
    expectedQuantity: 100,
    status: 'pending',
    createdAtUtc: nowUtc,
  },
]

/**
 * Mock the business-console WMS gateway endpoints the PDA pages depend on.
 * Lists return `{ success, data: { items, total } }`; completes return a bare success.
 * Any other path falls back (does NOT fake-succeed) so a complete-endpoint URL/method/path
 * regression surfaces loudly instead of being silently swallowed (aligns with routeConsoleApi).
 */
export async function routeBusinessConsoleApi(route: Route) {
  const { pathname } = new URL(route.request().url())
  const method = route.request().method()
  const isPost = method === 'POST'

  // complete endpoints (POST .../{id}/complete) — match before the list paths.
  if (isPost && /\/wms\/inbound-orders\/[^/]+\/complete$/.test(pathname)) {
    return fulfillJson(route, envelope({}))
  }
  if (isPost && /\/wms\/outbound-orders\/[^/]+\/complete$/.test(pathname)) {
    return fulfillJson(route, envelope({}))
  }
  if (isPost && /\/wms\/count-executions\/[^/]+\/complete$/.test(pathname)) {
    return fulfillJson(route, envelope({}))
  }

  // list endpoints (GET).
  if (pathname.endsWith('/wms/inbound-orders')) {
    return fulfillJson(route, listEnvelope(inboundOrders))
  }
  if (pathname.endsWith('/wms/outbound-orders')) {
    return fulfillJson(route, listEnvelope(outboundOrders))
  }
  if (pathname.endsWith('/wms/picking-tasks')) {
    return fulfillJson(route, listEnvelope(pickingTasks))
  }
  if (pathname.endsWith('/wms/putaway-tasks')) {
    return fulfillJson(route, listEnvelope(putawayTasks))
  }
  if (pathname.endsWith('/wms/count-executions')) {
    return fulfillJson(route, listEnvelope(countExecutions))
  }

  // Don't fake-succeed unmatched paths — fall back so a future un-mocked / mistyped
  // endpoint surfaces loudly instead of being silently swallowed (aligns with routeConsoleApi).
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
