import { flushPromises } from '@vue/test-utils'
import { effectScope, ref } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  getBusinessConsoleMasterDataResourceDetail,
  getBusinessConsoleMesWorkOrderDetail,
  listBusinessConsoleBarcodeTemplates,
} from '@nerv-iip/api-client'
import { useProductionReportSerialOptions } from './useProductionReportSerialOptions'

vi.mock('@nerv-iip/api-client', () => ({
  getBusinessConsoleMasterDataResourceDetail: vi.fn(),
  getBusinessConsoleMesWorkOrderDetail: vi.fn(),
  listBusinessConsoleBarcodeTemplates: vi.fn(),
}))
vi.mock('@/composables/businessContextBinding', () => ({
  bindBusinessContext: (value: object) =>
    Object.assign(value, { organizationId: 'org-1', environmentId: 'env-1' }),
}))
vi.mock('@/composables/useBusinessMes', () => ({
  useMesPrincipalWorkScope: () => ({
    scopeReady: ref(true),
    scopePending: ref(false),
    principalIdentity: ref('operator-1'),
    selectedScope: ref({ kind: 'work-center', id: 'WC-CNC-01' }),
    requireSelectedScope: () => ({ kind: 'work-center', id: 'WC-CNC-01' }),
  }),
}))
vi.mock('@/utils/notify', () => ({ notifyError: vi.fn() }))

describe('报工标签权威选项（#2889 PublicContract）', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    vi.mocked(getBusinessConsoleMesWorkOrderDetail).mockResolvedValue({
      data: { success: true, data: { skuId: 'SKU-HOUSING' } },
    } as never)
    vi.mocked(getBusinessConsoleMasterDataResourceDetail).mockResolvedValue({
      data: { success: true, data: { active: true, serialTrackingPolicy: 'on-production' } },
    } as never)
  })

  it('启用模板目录失败仍保留生产追踪策略，让零良品报工不依赖标签目录', async () => {
    vi.mocked(listBusinessConsoleBarcodeTemplates).mockRejectedValue({ status: 403 })
    const scope = effectScope()
    const result = scope.run(() =>
      useProductionReportSerialOptions(() => ({
        workOrderId: 'WO-0142',
        operationTaskId: 'OP-20',
      })),
    )!
    await flushPromises()
    expect(result.serialPolicy.value).toBe('on-production')
    expect(result.serialOptionsReady.value).toBe(true)
    expect(result.labelTemplates.value).toEqual([])
    expect(result.labelTemplatesStatus?.value).toBe('failed')
    vi.mocked(listBusinessConsoleBarcodeTemplates).mockResolvedValueOnce({
      data: {
        success: true,
        data: {
          templates: [
            {
              templateId: 'template-housing',
              templateName: '壳体标签',
              templateCode: 'HOUSING',
              status: 'active',
            },
          ],
          total: 1,
        },
      },
    } as never)
    await result.refreshSerialOptions()
    expect(result.labelTemplatesStatus.value).toBe('ready')
    expect(result.labelTemplates.value.map((template) => template.templateId)).toEqual([
      'template-housing',
    ])
    scope.stop()
  })

  it.each(['none', 'on-receipt', 'on-shipment'])(
    '%s 读取权威物料后不读取标签目录',
    async (policy) => {
      vi.mocked(getBusinessConsoleMasterDataResourceDetail).mockResolvedValue({
        data: { success: true, data: { active: true, serialTrackingPolicy: policy } },
      } as never)
      const scope = effectScope()
      const result = scope.run(() =>
        useProductionReportSerialOptions(() => ({
          workOrderId: 'WO-0142',
          operationTaskId: 'OP-20',
        })),
      )!
      await flushPromises()
      expect(result.serialPolicy.value).toBe(policy)
      expect(getBusinessConsoleMesWorkOrderDetail).toHaveBeenCalledWith(
        expect.objectContaining({
          query: {
            organizationId: 'org-1',
            environmentId: 'env-1',
            scopeKind: 'work-center',
            scopeId: 'WC-CNC-01',
          },
        }),
      )
      expect(listBusinessConsoleBarcodeTemplates).not.toHaveBeenCalled()
      scope.stop()
    },
  )

  it('非法 optional 策略不被猜成可报工，且不读取标签目录', async () => {
    vi.mocked(getBusinessConsoleMasterDataResourceDetail).mockResolvedValue({
      data: { success: true, data: { active: true, serialTrackingPolicy: 'optional' } },
    } as never)
    const scope = effectScope()
    const result = scope.run(() =>
      useProductionReportSerialOptions(() => ({
        workOrderId: 'WO-0142',
        operationTaskId: 'OP-20',
      })),
    )!
    await flushPromises()
    expect(result.serialOptionsReady.value).toBe(false)
    expect(listBusinessConsoleBarcodeTemplates).not.toHaveBeenCalled()
    scope.stop()
  })
})
