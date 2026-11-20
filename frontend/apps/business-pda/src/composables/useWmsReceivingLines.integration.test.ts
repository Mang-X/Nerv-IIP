import { configureApiClient } from '@nerv-iip/api-client'
import { PiniaColada } from '@pinia/colada'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { afterEach, expect, it } from 'vitest'
import { defineComponent, h, ref } from 'vue'
import { useAuthStore } from '@/stores/auth'
import { useWmsReceivingLines } from './useBusinessWms'

afterEach(() => configureApiClient())

it('keeps receiving lines bound to the selected order and work scope when requests overlap', async () => {
  const pending: { url: URL; respond: (response: Response) => void }[] = []
  configureApiClient({
    baseUrl: 'https://gateway.test',
    fetch: (input) => {
      const url = new URL(input instanceof Request ? input.url : input.toString())
      const { promise, resolve } = Promise.withResolvers<Response>()
      pending.push({ url, respond: resolve })
      return promise
    },
  })

  const pinia = createPinia()
  useAuthStore(pinia).principal = {
    principalId: 'operator-1',
    organizationId: 'org-001',
    environmentId: 'env-dev',
  } as never
  const orderNo = ref('IN-1')
  const scopeId = ref('POOL-RECEIVING')
  let result!: ReturnType<typeof useWmsReceivingLines>
  const Harness = defineComponent({
    setup() {
      result = useWmsReceivingLines(orderNo, { scopeKind: 'work-pool', scopeId })
      return () => h('div')
    },
  })
  mount(Harness, {
    global: { plugins: [pinia, [PiniaColada, { queryOptions: { gcTime: 300_000 } }]] },
  })
  await flushPromises()

  expect(pending).toHaveLength(1)
  const orderA = pending[0]!
  expect(orderA.url.pathname).toBe('/api/business-console/v1/wms/receiving-quality-gates')
  expect(Object.fromEntries(orderA.url.searchParams)).toEqual({
    organizationId: 'org-001',
    environmentId: 'env-dev',
    scopeKind: 'work-pool',
    scopeId: 'POOL-RECEIVING',
    inboundOrderNo: 'IN-1',
    includeNotRequired: 'true',
    skip: '0',
    take: '500',
  })

  orderNo.value = 'IN-2'
  expect(result.lines.value).toEqual([])
  expect(result.complete.value).toBe(false)
  await flushPromises()
  expect(pending).toHaveLength(2)
  const orderB = pending[1]!
  expect(orderB.url.searchParams.get('inboundOrderNo')).toBe('IN-2')

  const lineB = { inboundOrderLineId: 'line-b', inboundOrderNo: 'IN-2', gateStatus: 'not-required' }
  orderB.respond(Response.json({ success: true, data: { items: [lineB], total: 1 } }))
  await flushPromises()
  expect(result.lines.value).toEqual([lineB])
  expect(result.complete.value).toBe(true)

  orderA.respond(
    Response.json({
      success: true,
      data: { items: [{ inboundOrderLineId: 'line-a', inboundOrderNo: 'IN-1' }], total: 1 },
    }),
  )
  await flushPromises()
  expect(result.lines.value).toEqual([lineB])

  scopeId.value = 'POOL-OTHER'
  expect(result.lines.value).toEqual([])
  expect(result.complete.value).toBe(false)
  await flushPromises()
  expect(pending).toHaveLength(3)
  expect(pending[2]!.url.searchParams.get('scopeId')).toBe('POOL-OTHER')
  pending[2]!.respond(Response.json({ title: 'Forbidden' }, { status: 403 }))
  await flushPromises()
  expect(result.error.value).toBeTruthy()
  expect(result.lines.value).toEqual([])
  expect(result.complete.value).toBe(false)
})
