import { configureApiClient } from '@nerv-iip/api-client'
import { afterEach, expect, it, vi } from 'vitest'
import { useSchedulingPlanCsv } from './useSchedulingPlanCsv'

afterEach(() => {
  configureApiClient()
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

it('downloads the complete owner CSV bytes for the selected plan and current business scope', async () => {
  // #4085: rebuilding from filtered table rows or dropping scope would violate this request/byte boundary.
  const bytes = '\uFEFFPlanId,OrderReference\r\nPLAN-01,"工单,一"\r\n'
  let request: Request | undefined
  configureApiClient({
    baseUrl: 'https://gateway.local',
    fetch: (async (input: Request) => {
      request = input
      return new Response(bytes, { headers: { 'content-type': 'text/csv; charset=utf-8' } })
    }) as typeof fetch,
  })
  let downloaded: Blob | undefined
  vi.stubGlobal(
    'URL',
    class extends URL {
      static createObjectURL = vi.fn((blob: Blob) => {
        downloaded = blob
        return 'blob:schedule'
      })
      static revokeObjectURL = vi.fn()
    },
  )
  let filename = ''
  vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(
    function (this: HTMLAnchorElement) {
      filename = this.download
    },
  )
  const csv = useSchedulingPlanCsv()
  await csv.download({ planId: 'PLAN-01', organizationId: 'org-a', environmentId: 'production' })
  expect(request?.url).toBe(
    'https://gateway.local/api/business-console/v1/scheduling/plans/PLAN-01/csv?organizationId=org-a&environmentId=production',
  )
  expect(Array.from(new Uint8Array(await downloaded!.arrayBuffer()))).toEqual(
    Array.from(new TextEncoder().encode(bytes)),
  )
  expect(downloaded?.size).toBe(new TextEncoder().encode(bytes).length)
  expect(filename).toBe('schedule-PLAN-01.csv')
  expect(csv.pending.value).toBe(false)
})

it('propagates an owner failure without starting a browser download', async () => {
  configureApiClient({
    baseUrl: 'https://gateway.local',
    fetch: async () => Response.json({ detail: '方案不存在' }, { status: 404 }),
  })
  const create = vi.fn()
  vi.stubGlobal(
    'URL',
    class extends URL {
      static createObjectURL = create
      static revokeObjectURL = vi.fn()
    },
  )
  const csv = useSchedulingPlanCsv()
  await expect(
    csv.download({ planId: 'PLAN-MISSING', organizationId: 'org-a', environmentId: 'production' }),
  ).rejects.toMatchObject({ detail: '方案不存在' })
  expect(create).not.toHaveBeenCalled()
  expect(csv.pending.value).toBe(false)
})
