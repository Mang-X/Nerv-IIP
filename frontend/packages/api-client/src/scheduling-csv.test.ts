import { expect, it } from 'vitest'
import { exportBusinessConsoleSchedulingPlanCsv } from './index'

it('downloads the owner CSV as a Blob through the stable generated client with scope and attachment metadata', async () => {
  // #4084 PublicContract: the stable download must match the binary response type and preserve the owner bytes.
  const csv = 'PlanId,OrderReference\r\nplan-001,订单一\r\n'
  let request: Request | undefined
  const result = await exportBusinessConsoleSchedulingPlanCsv({
    baseUrl: 'https://gateway.example.test',
    path: { planId: 'plan-001' },
    query: { organizationId: 'org-001', environmentId: 'env-dev' },
    fetch: async (input) => {
      request = input as Request
      return new Response(csv, {
        headers: {
          'content-type': 'text/csv; charset=utf-8',
          'content-disposition': 'attachment; filename=schedule-plan.csv',
        },
      })
    },
  })
  expect(request?.url).toBe(
    'https://gateway.example.test/api/business-console/v1/scheduling/plans/plan-001/csv?organizationId=org-001&environmentId=env-dev',
  )
  expect(result.data).toHaveProperty('size', new TextEncoder().encode(csv).length)
  expect(await result.data?.text()).toBe(csv)
  expect(result.response?.headers.get('content-disposition')).toBe(
    'attachment; filename=schedule-plan.csv',
  )
})
