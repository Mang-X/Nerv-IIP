import type { BusinessConsoleBarcodeResolveCandidate } from '@nerv-iip/api-client'
import { mount } from '@vue/test-utils'
import { computed, shallowRef } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const state = {
  status: shallowRef<
    | 'idle'
    | 'pending'
    | 'resolved'
    | 'ambiguous'
    | 'unknown'
    | 'unsupported'
    | 'forbidden'
    | 'rejected'
    | 'error'
  >('idle'),
  scannedValue: shallowRef(''),
  message: shallowRef(''),
  candidates: shallowRef<BusinessConsoleBarcodeResolveCandidate[]>([]),
  scan: vi.fn(),
  selectCandidate: vi.fn(),
  reset: vi.fn(),
}
const useOptions = vi.fn()

vi.mock('@/composables/mes/useMesScanPrevalidation', () => ({
  useMesScanPrevalidation: (options: unknown) => {
    useOptions(options)
    return {
      ...state,
      pending: computed(() => state.status.value === 'pending'),
    }
  },
}))

import MesScanPrevalidation from './MesScanPrevalidation.vue'

describe('MesScanPrevalidation', () => {
  beforeEach(() => {
    state.status.value = 'idle'
    state.scannedValue.value = ''
    state.message.value = ''
    state.candidates.value = []
    state.scan.mockReset()
    state.selectCandidate.mockReset()
    state.reset.mockReset()
    useOptions.mockReset()
  })

  it('emits the accepted strong-ID context and reports pending state', async () => {
    const accepted = { kind: 'work-order' as const, candidate: {}, workOrderId: 'WO-1' }
    state.scan.mockImplementation(async (value: string) => {
      expect(value).toBe('WO-CODE')
      state.status.value = 'pending'
      await Promise.resolve()
      state.status.value = 'resolved'
      state.message.value = '扫码对象已通过当前工单工序预校验。'
      return accepted
    })
    const wrapper = mount(MesScanPrevalidation, {
      props: {
        organizationId: 'org-1',
        environmentId: 'env-1',
        acceptedKinds: ['work-order'],
        placeholder: '扫描工单 / 工序 / 物料 / 设备 / 工牌',
      },
    })

    await wrapper.find('input').setValue('WO-CODE')
    await wrapper.find('input').trigger('keydown.enter')
    await Promise.resolve()
    await wrapper.vm.$nextTick()

    expect(wrapper.emitted('accepted')?.[0]).toEqual([accepted])
    expect(wrapper.emitted('pendingChange')).toEqual([[true], [false]])
    expect(wrapper.get('[data-testid="mes-scan-status"]').text()).toContain('已通过')
  })

  it('renders ambiguous candidates and validates only the chosen candidate', async () => {
    const candidate = {
      objectType: 'personnel',
      strongIds: { userId: 'USER-1' },
    } satisfies BusinessConsoleBarcodeResolveCandidate
    state.status.value = 'ambiguous'
    state.message.value = '找到多个候选，请手动选择；系统不会猜测。'
    state.candidates.value = [
      candidate,
      { objectType: 'personnel', strongIds: { userId: 'USER-2' } },
    ]
    state.selectCandidate.mockResolvedValue({
      kind: 'personnel',
      candidate,
      workOrderId: 'WO-1',
      operationTaskId: 'OP-1',
      scannedObjectId: 'USER-1',
    })
    const wrapper = mount(MesScanPrevalidation, {
      props: {
        organizationId: 'org-1',
        environmentId: 'env-1',
        acceptedKinds: ['personnel'],
      },
    })

    expect(wrapper.get('[data-testid="mes-scan-candidate-0"]').text()).toContain('USER-1')
    expect(wrapper.get('[data-testid="mes-scan-candidate-1"]').text()).toContain('USER-2')

    await wrapper.get('[data-testid="mes-scan-candidate-0"]').trigger('click')

    expect(state.selectCandidate).toHaveBeenCalledWith(candidate)
    expect(wrapper.emitted('accepted')?.[0]?.[0]).toMatchObject({ kind: 'personnel' })
  })

  it('uses an alert for rejected and source-failure states', async () => {
    state.status.value = 'rejected'
    state.message.value = '工牌与当前工序指派人员不匹配。'
    const wrapper = mount(MesScanPrevalidation, {
      props: {
        organizationId: 'org-1',
        environmentId: 'env-1',
        acceptedKinds: ['personnel'],
      },
    })

    expect(wrapper.get('[data-testid="mes-scan-status"]').attributes('role')).toBe('alert')
    expect(wrapper.text()).toContain('工牌与当前工序指派人员不匹配')

    state.status.value = 'error'
    state.message.value = '扫码预校验来源暂不可用，已阻止当前操作，请稍后重试。'
    await wrapper.vm.$nextTick()
    expect(wrapper.get('[data-testid="mes-scan-status"]').attributes('role')).toBe('alert')
  })

  it('invalidates pending scan state when the page leaves', () => {
    const wrapper = mount(MesScanPrevalidation, {
      props: {
        organizationId: 'org-1',
        environmentId: 'env-1',
        acceptedKinds: ['work-order'],
      },
    })

    wrapper.unmount()

    expect(state.reset).toHaveBeenCalledOnce()
  })

  it('abandons the scanner intent when its page context becomes inactive', async () => {
    const wrapper = mount(MesScanPrevalidation, {
      props: {
        organizationId: 'org-1',
        environmentId: 'env-1',
        acceptedKinds: ['work-order'],
        active: true,
      },
    })

    await wrapper.setProps({ active: false })

    expect(state.reset).toHaveBeenCalledOnce()
  })
})
