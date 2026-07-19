import type { EffectScope } from 'vue'
import { effectScope, nextTick, reactive } from 'vue'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useInventoryExpiryView } from './useInventoryExpiryView'

const state = vi.hoisted(() => ({
  alerts: undefined as { value: unknown[] } | undefined,
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
  })

  it('成功响应前不把空数组报告成服务端零条结果', async () => {
    const view = createView(
      reactive({ siteCode: 'PLANT-SH-01', skuCode: '', uomCode: '', locationCode: '' }),
    )
    view.toggleNearExpiryView()

    expect(view.expirySummary.value.alertCount).toBe('—')
    state.successful!.value = true
    await nextTick()
    expect(view.expirySummary.value.alertCount).toBe(0)
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

  it('范围超过上限时提示用 SKU 或库位缩小范围', async () => {
    state.notifyError.mockReset()
    const view = createView(
      reactive({ siteCode: 'PLANT-SH-01', skuCode: '', uomCode: '', locationCode: '' }),
    )
    view.toggleNearExpiryView()

    const error = new Error('returned more than 1000 ledger lines. Add SKU or location filters')
    state.error!.value = error
    await nextTick()

    expect(state.notifyError).toHaveBeenCalledWith(
      error,
      '效期预警范围过大，请添加 SKU 或库位后重试。',
    )
  })

  it('识别生成客户端抛出的结构化错误消息', async () => {
    state.notifyError.mockReset()
    const view = createView(
      reactive({ siteCode: 'PLANT-SH-01', skuCode: '', uomCode: '', locationCode: '' }),
    )
    view.toggleNearExpiryView()

    const error = { message: 'returned more than 1000 ledger lines. Add SKU or location filters' }
    state.error!.value = error
    await nextTick()

    expect(state.notifyError).toHaveBeenCalledWith(
      error,
      '效期预警范围过大，请添加 SKU 或库位后重试。',
    )
  })
})
