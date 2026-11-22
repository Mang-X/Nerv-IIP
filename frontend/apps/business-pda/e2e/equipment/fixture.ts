import { type Locator, expect, type Browser } from '@playwright/test'
import {
  principal,
  routeConsoleApi,
  routeBusinessConsoleApi,
  session,
  STORAGE_KEY,
} from '../fixtures'

export const selfWorkOrderId = (index: number) =>
  `019f1000-0000-7000-8000-${String(index).padStart(12, '0')}`

export async function expectMinimumTouchHeight(locator: Locator, minimum = 44) {
  await expect(locator).toBeVisible()
  const box = await locator.boundingBox()
  expect(
    box,
    `missing touch box for ${await locator.evaluate((element) => element.outerHTML)}`,
  ).not.toBeNull()
  expect(box!.height).toBeGreaterThanOrEqual(minimum)
}

export async function createContextWithPrincipal(
  browser: Browser,
  overrides: Partial<typeof principal>,
) {
  const context = await browser.newContext({ viewport: { width: 375, height: 812 } })
  const page = await context.newPage()
  const scopedPrincipal = { ...principal, ...overrides }
  await page.route('**/api/console/v1/**', routeConsoleApi)
  await page.route('**/api/business-console/v1/**', routeBusinessConsoleApi)
  await page.route('**/api/business-console/v2/**', routeBusinessConsoleApi)
  await page.route('**/api/console/v1/auth/refresh', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ success: true, data: { ...session, principal: scopedPrincipal } }),
    }),
  )
  await page.route('**/api/console/v1/auth/me', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ success: true, data: scopedPrincipal }),
    }),
  )
  await page.addInitScript(({ key, stored }) => localStorage.setItem(key, JSON.stringify(stored)), {
    key: STORAGE_KEY,
    stored: {
      principal: scopedPrincipal,
      refreshToken: session.refreshToken,
      sessionId: session.sessionId,
    },
  })
  return { context, page }
}
