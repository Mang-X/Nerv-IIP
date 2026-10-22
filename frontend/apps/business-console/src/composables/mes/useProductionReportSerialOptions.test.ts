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

  // #3747：码集成员判定的权威在网关（`production-serial-policy-invalid`），本地不再抄一份码集。
  // 非 on-production 的策略一律按「不需要标签模板」处理：不读标签目录，也不在这里把人挡死。
  it('未知策略按非 on-production 处理，不读取标签目录，由网关做码集判定', async () => {
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
    expect(result.serialOptionsReady.value).toBe(true)
    expect(result.serialPolicy.value).toBe('optional')
    expect(listBusinessConsoleBarcodeTemplates).not.toHaveBeenCalled()
    scope.stop()
  })

  it('策略缺失仍然拦下：没有策略就判不出要不要标签模板', async () => {
    vi.mocked(getBusinessConsoleMasterDataResourceDetail).mockResolvedValue({
      data: { success: true, data: { active: true, serialTrackingPolicy: '' } },
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
