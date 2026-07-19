import type { EffectScope } from 'vue'
import { effectScope, nextTick, reactive } from 'vue'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useInventoryExpiryView } from './useInventoryExpiryView'

const state = vi.hoisted(() => ({
  alerts: undefined as { value: unknown[] } | undefined,
  response: undefined as { value: unknown } | undefined,
  enabledWhen: undefined as (() => boolean) | undefined,
  error: undefined as { value: unknown } | undefined,
  successful: undefined as { value: boolean } | undefined,
  expiryFilters: undefined as
    | {
        organizationId: string
        environmentId: string
        siteCode: string
        skuCode?: string
        locationCode?: string
      }
    | undefined,
  notifyError: vi.fn(),
}))

vi.mock('./useBusinessInventory', async () => {
  const { reactive, shallowRef } = await import('vue')
  state.error = shallowRef<unknown>()
  state.expiryFilters = reactive({
    organizationId: 'org-001',
    environmentId: 'env-dev',
    siteCode: '',
    skuCode: undefined as string | undefined,
    locationCode: undefined as string | undefined,
  })
  return {
    useInventoryExpiryAlerts: (enabledWhen: () => boolean) => {
      state.enabledWhen = enabledWhen
      return {
        expiryAlerts: (state.alerts = shallowRef([])),
        expiryAlertsResponse: (state.response = shallowRef(undefined)),
        expiryAlertsPage: shallowRef(1),
        expiryAlertsPageSize: shallowRef(50),
        expiryAlertsTotal: shallowRef(0),
        expiryAlertsError: state.error,
        expiryAlertsPending: shallowRef(false),
        expiryAlertsSuccessful: (state.successful = shallowRef(false)),
        filters: state.expiryFilters,
        refreshExpiryAlerts: vi.fn(),
      }
    },
  }
})

vi.mock('@/utils/notify', () => ({ notifyError: state.notifyError }))

describe('useInventoryExpiryView', () => {
  let scope: EffectScope

  beforeEach(() => {
    scope = effectScope()
  })

  afterEach(() => {
    scope.stop()
  })

  function createView(sourceFilters: Parameters<typeof useInventoryExpiryView>[0]) {
    return scope.run(() => useInventoryExpiryView(sourceFilters))!
  }

  it('只在近效期模式启用查询，并允许在模式内用服务端支持字段缩小范围', async () => {
    const sourceFilters = reactive({
      siteCode: 'PLANT-SH-01',
      skuCode: '',
      uomCode: 'DRUM',
      locationCode: '',
      lotNo: 'LOT-240719-A',
      serialNo: 'SN-001',
      qualityStatus: 'blocked',
      ownerType: 'owned',
      ownerId: 'OWNER-01',
    })
    const view = createView(sourceFilters)

    expect(state.expiryFilters?.siteCode).toBe('PLANT-SH-01')
    expect(state.enabledWhen?.()).toBe(false)
    view.toggleNearExpiryView()
    expect(state.enabledWhen?.()).toBe(true)
    sourceFilters.skuCode = 'SKU-COOLANT-20L'
    await nextTick()

    expect(state.expiryFilters?.skuCode).toBe('SKU-COOLANT-20L')
    expect(view.nearExpiryOnly.value).toBe(true)
    expect(sourceFilters).toMatchObject({
      uomCode: '',
      lotNo: undefined,
      serialNo: undefined,
      qualityStatus: undefined,
      ownerType: undefined,
      ownerId: undefined,
    })

    view.toggleNearExpiryView()
    expect(sourceFilters).toMatchObject({
      uomCode: 'DRUM',
      lotNo: 'LOT-240719-A',
      serialNo: 'SN-001',
      qualityStatus: 'blocked',
      ownerType: 'owned',
      ownerId: 'OWNER-01',
    })
  })

  it('成功响应前不把空数组报告成服务端零条结果', async () => {
    const view = createView(
      reactive({ siteCode: 'PLANT-SH-01', skuCode: '', uomCode: '', locationCode: '' }),
    )
    view.toggleNearExpiryView()

    expect(view.expirySummary.value.alertCount).toBe('—')
    state.successful!.value = true
    state.response!.value = {
      items: [],
      totalCount: 27,
      expiredCount: 9,
      nearExpiryCount: 18,
      skuCount: 6,
    }
    await nextTick()
    expect(view.expirySummary.value).toEqual({
      alertCount: 27,
      expiredCount: 9,
      nearCount: 18,
      skuCount: 6,
    })
  })

  it('组织、环境和工厂均就绪后才报告效期查询范围可用', async () => {
    const sourceFilters = reactive({
      siteCode: 'PLANT-SH-01',
      skuCode: '',
      uomCode: '',
      locationCode: '',
    })
    const view = createView(sourceFilters)

    expect(view.hasExpiryScope.value).toBe(true)
    state.expiryFilters!.organizationId = ''
    await nextTick()
    expect(view.hasExpiryScope.value).toBe(false)

    state.expiryFilters!.organizationId = 'org-001'
    state.expiryFilters!.environmentId = ''
    await nextTick()
    expect(view.hasExpiryScope.value).toBe(false)

    state.expiryFilters!.environmentId = 'env-dev'
    sourceFilters.siteCode = ''
    await nextTick()
    expect(view.hasExpiryScope.value).toBe(false)
  })

  it('把近效期查询错误转为统一友好 toast', async () => {
    state.notifyError.mockReset()
    const view = createView(
      reactive({ siteCode: 'PLANT-SH-01', skuCode: '', uomCode: '', locationCode: '' }),
    )
    view.toggleNearExpiryView()

    const error = new Error('downstream-invalid-response')
    state.error!.value = error
    await nextTick()

    expect(state.notifyError).toHaveBeenCalledWith(error, '近效期批次加载失败，请稍后重试。')
  })
})
