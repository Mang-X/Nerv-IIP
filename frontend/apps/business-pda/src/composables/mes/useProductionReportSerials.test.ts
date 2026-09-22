import { flushPromises, mount } from '@vue/test-utils'
import { computed, defineComponent, ref } from 'vue'
import { beforeEach, expect, it, vi } from 'vitest'
import { useProductionReportSerials } from './useProductionReportSerials'

const api = vi.hoisted(() => ({ sku: vi.fn(), templates: vi.fn() }))
vi.mock('@nerv-iip/api-client', async (original) => ({
  ...(await original<typeof import('@nerv-iip/api-client')>()),
  getBusinessConsoleMasterDataResourceDetail: api.sku,
  listBusinessConsoleBarcodeTemplates: api.templates,
}))
const principal = ref({
  permissionCodes: ['business.masterdata.resources.read', 'business.barcodes.read'],
})
vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({
    get principal() {
      return principal.value
    },
  }),
}))
function setup() {
  const context = ref({
    principalId: 'user-1',
    organizationId: 'org-1',
    environmentId: 'env-1',
    scopeKind: 'organization',
    scopeId: 'org-1',
    generation: 1,
  })
  const pair = ref({ workOrderId: 'WO-1', operationTaskId: 'OP-1' })
  const good = ref(2)
  let serials!: ReturnType<typeof useProductionReportSerials>
  mount(
    defineComponent({
      setup() {
        serials = useProductionReportSerials(
          computed(() => pair.value),
          computed(() => 'SKU-1'),
          computed(() => context.value),
          good,
        )
        return () => null
      },
    }),
  )
  return { serials, context, pair, good }
}
beforeEach(() => {
  principal.value = {
    permissionCodes: ['business.masterdata.resources.read', 'business.barcodes.read'],
  }
  api.sku.mockReset().mockResolvedValue({
    data: {
      success: true,
      data: {
        resourceType: 'sku',
        code: 'SKU-1',
        active: true,
        serialTrackingPolicy: 'on-production',
        defaultBarcodeRuleCode: 'UNIT',
      },
    },
  })
  api.templates.mockReset().mockResolvedValue({
    data: {
      success: true,
      data: {
        templates: [
          {
            templateId: 'tpl-1',
            templateName: '成品标签',
            templateCode: 'FINISHED',
            status: 'active',
          },
        ],
        total: 1,
      },
    },
  })
})
it('requires a named template and integral good quantity only for on-production', async () => {
  const { serials, good } = setup()
  await flushPromises()
  expect(serials.required.value).toBe(true)
  expect(serials.valid.value).toBe(false)
  serials.templateId.value = 'tpl-1'
  expect(serials.valid.value).toBe(true)
  expect(serials.pendingCount.value).toBe(2)
  good.value = 2.5
  expect(serials.valid.value).toBe(false)
  good.value = 0
  expect(serials.valid.value).toBe(true)
  expect(serials.pendingCount.value).toBe(0)
})
it.each(['none', 'on-receipt', 'on-shipment'])('does not allocate for %s', async (policy) => {
  api.sku.mockResolvedValue({
    data: {
      success: true,
      data: { resourceType: 'sku', code: 'SKU-1', active: true, serialTrackingPolicy: policy },
    },
  })
  const { serials, good } = setup()
  good.value = 1.5
  await flushPromises()
  expect(serials.required.value).toBe(false)
  expect(serials.valid.value).toBe(true)
  expect(serials.pendingCount.value).toBe(0)
  expect(api.templates).not.toHaveBeenCalled()
})
// #3747：码集成员判定的权威在网关，PDA 不再抄一份码集——非 on-production 一律按「不赋序」处理。
it('treats an unknown policy as non-serialized instead of blocking the operator', async () => {
  api.sku.mockResolvedValue({
    data: {
      success: true,
      data: { code: 'SKU-1', active: true, serialTrackingPolicy: 'optional' },
    },
  })
  const { serials } = setup()
  await flushPromises()
  expect(serials.required.value).toBe(false)
  expect(serials.valid.value).toBe(true)
  expect(api.templates).not.toHaveBeenCalled()
})
it('still rejects a missing policy and a missing resource permission before submitting', async () => {
  api.sku.mockResolvedValue({
    data: { success: true, data: { code: 'SKU-1', active: true, serialTrackingPolicy: '' } },
  })
  const { serials } = setup()
  await flushPromises()
  expect(serials.valid.value).toBe(false)
  principal.value = { permissionCodes: [] }
  await flushPromises()
  expect(serials.message.value).toContain('权限')
  expect(serials.valid.value).toBe(false)
})
it('discards late policy and template selections after context changes', async () => {
  let finish!: (value: unknown) => void
  api.sku.mockImplementationOnce(
    () =>
      new Promise((resolve) => {
        finish = resolve
      }),
  )
  const { serials, context } = setup()
  context.value = { ...context.value, generation: 2, principalId: 'user-2' }
  await flushPromises()
  serials.templateId.value = 'tpl-1'
  finish({
    data: { success: true, data: { code: 'SKU-1', active: true, serialTrackingPolicy: 'none' } },
  })
  await flushPromises()
  expect(serials.required.value).toBe(true)
  expect(serials.templateId.value).toBe('tpl-1')
})

it('allows zero good quantity without a barcode rule but blocks positive allocation', async () => {
  api.sku.mockResolvedValue({
    data: {
      success: true,
      data: {
        resourceType: 'sku',
        code: 'SKU-1',
        active: true,
        serialTrackingPolicy: 'on-production',
      },
    },
  })
  const { serials, good } = setup()
  good.value = 0
  await flushPromises()
  expect(serials.valid.value).toBe(true)
  good.value = 1
  expect(serials.valid.value).toBe(false)
  expect(serials.message.value).toContain('条码规则')
})
