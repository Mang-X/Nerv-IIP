import { expect, test } from '@playwright/test'

const baseURL = process.env.NERV_IIP_PLAYWRIGHT_BASE_URL
const adminPassword = process.env.NERV_IIP_FULLSTACK_ADMIN_PASSWORD

test.skip(!baseURL || !adminPassword, 'requires a managed full-stack session')
// Authentication requests contain session credentials; retain only the explicit read evidence below.
test.use({ trace: 'off' })

test('dynamic origin authenticates MBOM and inventory reads through both gateway proxies @smoke', async ({ page, request }, testInfo) => {
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

  const accessToken: string = (await login.json()).data.accessToken
  const scope = 'organizationId=org-001&environmentId=env-dev'
  const read = async (path: string) => {
    const response = await page.evaluate(async ({ path, accessToken }) => {
      const response = await fetch(path, { headers: { Authorization: `Bearer ${accessToken}` } })
      return { status: response.status, body: await response.json() }
    }, { path, accessToken })
    expect(response.status, path).toBe(200)
    return { path, ...response }
  }
  const mbomList = await read(`/api/business-console/v1/engineering/manufacturing-boms?${scope}&skuCode=FG-QJ-P1-L&status=Published&skip=0&take=100`)
  expect(mbomList.body.data.items).toEqual(expect.arrayContaining([
    expect.objectContaining({ bomCode: 'MBOM-FG-QJ-P1-L', revision: '2' }),
  ]))
  const mbomDetail = await read(`/api/business-console/v1/engineering/manufacturing-boms/MBOM-FG-QJ-P1-L/2?${scope}`)
  const material = mbomDetail.body.data.materialLines.find((line: { isPhantom: boolean }) => !line.isPhantom)
  expect(material).toMatchObject({ skuCode: 'PK-BOX-01', unitOfMeasureCode: 'pcs' })
  const availability = await read(`/api/business-console/v1/inventory/availability?${scope}&skuCode=${material.skuCode}&uomCode=${material.unitOfMeasureCode}&siteCode=SITE-001`)
  const movements = await read(`/api/business-console/v1/inventory/movements?${scope}&skuCode=${material.skuCode}&siteCode=SITE-001&movementType=inbound&page=1&pageSize=100`)
  await testInfo.attach('mbom-inventory-browser-reads', {
    contentType: 'application/json',
    body: JSON.stringify({ origin: viteOrigin, mbomList, mbomDetail, availability, movements }),
  })
  expect(apiRequests.length).toBeGreaterThanOrEqual(2)
  expect(apiRequests.every((url) => new URL(url).origin === viteOrigin)).toBe(true)
})
