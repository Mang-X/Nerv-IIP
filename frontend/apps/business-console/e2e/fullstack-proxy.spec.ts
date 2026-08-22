import { expect, test } from '@playwright/test'

const baseURL = process.env.NERV_IIP_PLAYWRIGHT_BASE_URL
const adminPassword = process.env.NERV_IIP_FULLSTACK_ADMIN_PASSWORD

test.skip(!baseURL || !adminPassword, 'requires a managed full-stack session')

test('dynamic origin uses both same-origin gateway proxies @smoke', async ({ page, request }) => {
  const viteOrigin = new URL(baseURL!).origin
  const apiRequests: string[] = []
  page.on('request', (request) => {
    const url = new URL(request.url())
    if (url.pathname.startsWith('/api/')) apiRequests.push(request.url())
  })

  let consecutiveReadyResponses = 0
  await expect
    .poll(
      async () => {
        try {
          const response = await request.get(new URL('/login', baseURL!).toString(), {
            failOnStatusCode: false,
            timeout: 5_000,
          })
          consecutiveReadyResponses = response.status() < 500 ? consecutiveReadyResponses + 1 : 0
        } catch {
          consecutiveReadyResponses = 0
        }
        return consecutiveReadyResponses
      },
      {
        message: `business-console endpoint did not become stable before navigation: ${viteOrigin}`,
        timeout: 30_000,
        intervals: [500, 1_000, 1_000],
      },
    )
    .toBeGreaterThanOrEqual(3)

  await page.goto('/login')
  const loginResponse = page.waitForResponse(
    (response) => new URL(response.url()).pathname === '/api/console/v1/auth/login',
  )
  await page.getByLabel('登录名').fill('admin')
  await page.getByLabel('密码').fill(adminPassword!)
  await page.getByRole('button', { name: '登录' }).click()
  const login = await loginResponse
  expect(login.status()).toBeGreaterThanOrEqual(200)
  expect(login.status()).toBeLessThan(300)
  // Each full-stack session has a unique admin password and JWT signing key. A platform
  // proxy routed to another session fails this login; a business proxy routed to another
  // session rejects the resulting bearer token. The two successful responses therefore
  // prove both proxy targets belong to this session, not only that they are same-origin.
  await expect(page).toHaveURL(new URL('/', baseURL!).toString())

  const skuResponse = page.waitForResponse(
    (response) => new URL(response.url()).pathname === '/api/business-console/v1/master-data/skus',
  )
  await page.goto('/master-data/skus')
  const sku = await skuResponse
  expect(sku.status()).toBeGreaterThanOrEqual(200)
  expect(sku.status()).toBeLessThan(300)
  expect(apiRequests.length).toBeGreaterThanOrEqual(2)
  expect(apiRequests.every((url) => new URL(url).origin === viteOrigin)).toBe(true)
})
