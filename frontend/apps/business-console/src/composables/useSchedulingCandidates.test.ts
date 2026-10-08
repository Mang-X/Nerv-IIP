import { effectScope, nextTick, shallowRef } from 'vue'
import { describe, expect, it, vi, beforeEach } from 'vitest'
import { useSchedulingCandidates } from './useSchedulingCandidates'

const api = vi.hoisted(() => ({ preview: vi.fn(), select: vi.fn(), failure: vi.fn() }))
vi.mock('@nerv-iip/api-client', () => ({
  previewBusinessConsoleSchedulingCandidates: api.preview,
  selectBusinessConsoleSchedulingCandidate: api.select,
}))
vi.mock('@/utils/notify', () => ({ notifyOperationFailure: api.failure }))

beforeEach(() => vi.resetAllMocks())
describe('当前事实候选生成与选定', () => {
  it('重预览采用新响应，选定传明确时点和指纹并恢复服务端保存的草稿', async () => {
    const scope = effectScope()
    const baseline = shallowRef('plan-production')
    const selected = vi.fn().mockResolvedValue(undefined)
    const local = scope.run(() =>
      useSchedulingCandidates({
        context: { organizationId: 'plant-sh', environmentId: 'production' },
        baselinePlanId: baseline,
        onSelected: selected,
      }),
    )!
    const set = {
      baselinePlanId: baseline.value,
      asOfUtc: '2026-10-08T00:00:00Z',
      inputFingerprint: 'current-input',
      candidates: [{ strategy: 'rightShift' as const, kpis: { candidateLateOrderCount: 2 } }],
    }
    api.preview.mockResolvedValue({ data: { success: true, data: set } })
    await local.preview()
    expect(local.candidates.value).toEqual(set)
    const result = {
      plan: { planId: 'plan-selected', status: 'generated' },
      workingDraft: { planId: 'plan-selected' },
    }
    api.select.mockResolvedValue({ data: { success: true, data: result } })
    await local.select(set.candidates[0])
    expect(api.select.mock.calls[0][0].body).toEqual({
      organizationId: 'plant-sh',
      environmentId: 'production',
      baselinePlanId: baseline.value,
      asOfUtc: set.asOfUtc,
      inputFingerprint: set.inputFingerprint,
      strategy: 'rightShift',
    })
    expect(selected).toHaveBeenCalledWith(result)
    api.preview.mockResolvedValue({
      data: { success: true, data: { ...set, inputFingerprint: 'new-facts' } },
    })
    await local.preview()
    expect(local.candidates.value?.inputFingerprint).toBe('new-facts')
    baseline.value = 'plan-new-baseline'
    await nextTick()
    expect(local.candidates.value).toBeUndefined()
    scope.stop()
  })
  it('旧输入被服务拒绝时保留在预览并提示重预览，不加载或发布草稿', async () => {
    const scope = effectScope()
    const selected = vi.fn()
    const local = scope.run(() =>
      useSchedulingCandidates({
        context: { organizationId: 'plant-sh', environmentId: 'production' },
        baselinePlanId: 'plan-production',
        onSelected: selected,
      }),
    )!
    api.preview.mockResolvedValue({
      data: {
        success: true,
        data: {
          baselinePlanId: 'plan-production',
          asOfUtc: '2026-10-08T00:00:00Z',
          inputFingerprint: 'old-input',
        },
      },
    })
    await local.preview()
    api.select.mockResolvedValue({
      data: { success: false, message: '排程输入已变化，请重预览后重新选择候选。' },
    })
    await local.select({ strategy: 'rightShift' })
    expect(selected).not.toHaveBeenCalled()
    expect(api.failure).toHaveBeenCalled()
    expect(local.pending.value).toBe(false)
    scope.stop()
  })
})
