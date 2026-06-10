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
  return fulfillJson(route, envelope({}))
}

/** Mock any business-console gateway call the home may make (none required for the foundation home). */
export async function routeBusinessConsoleApi(route: Route) {
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
 * The measured hit area is the control's own box OR—when the control delegates
 * its tap surface to a wrapper (e.g. ScanBar's input fills a `min-h-touch` row)—
 * the nearest ancestor that already provides a >=44px-tall hit area. We climb at
 * most a few levels so a bare small control is still caught.
 */
export async function expectTouchTargets(page: Page) {
  const tooSmall = await page.evaluate(() => {
    const FLOOR = 44
    const els = [...document.querySelectorAll<HTMLElement>('button:not([disabled]), a[href], input')]
    return els
      .map((el) => {
        // Effective hit area: self, or the nearest ancestor (<=3 levels up) whose box
        // already meets the floor in both dimensions (e.g. ScanBar's `min-h-touch` row).
        let node: HTMLElement | null = el
        let hit = el.getBoundingClientRect()
        for (let depth = 0; node && depth < 3; depth++, node = node.parentElement) {
          const r = node.getBoundingClientRect()
          if (r.width >= FLOOR && r.height >= FLOOR) {
            hit = r
            break
          }
        }
        return { tag: el.tagName, w: hit.width, h: hit.height }
      })
      .filter((m) => m.w > 0 && m.h > 0 && (m.w < FLOOR || m.h < FLOOR))
  })
  expect(tooSmall).toEqual([])
}
