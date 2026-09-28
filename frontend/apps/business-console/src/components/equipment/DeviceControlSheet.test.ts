import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import DeviceControlSheet from './DeviceControlSheet.vue'

const state = vi.hoisted(() => ({
  dispatch: vi.fn(() => Promise.resolve('cmd-1')),
  startTracking: vi.fn(),
  resetTracking: vi.fn(),
  tracked: undefined as unknown,
}))

const cvState = vi.hoisted(() => ({
  current: { hasSample: true, value: 55 } as
    | { hasSample: boolean; value: number | null }
    | undefined,
  pending: false,
  error: undefined as unknown,
  refresh: vi.fn(),
}))

const TERMINAL = new Set(['completed', 'failed', 'rejected', 'abandoned'])

vi.mock('@/composables/useBusinessDeviceControl', () => ({
  deviceControlApprovalLabel: (v?: string | null) => v ?? '未知',
  deviceControlCommandTypeLabel: (v?: string | null) => v ?? '未知',
  deviceControlStatusLabel: (v?: string | null) => v ?? '未知',
  deviceControlStatusTone: () => 'neutral',
  deviceReceiptLabel: (code?: string | null) => (code?.startsWith('Bad') ? '设备拒绝执行' : '—'),
  isTerminalDeviceControlStatus: (v?: string | null) => (v ? TERMINAL.has(v.toLowerCase()) : false),
  useBusinessDeviceControlCommands: () => ({
    dispatchCommand: state.dispatch,
    dispatchPending: shallowRef(false),
    trackedResult: computed(() => state.tracked),
    trackedPending: shallowRef(false),
    trackedError: shallowRef(),
    startTracking: state.startTracking,
    resetTracking: state.resetTracking,
  }),
}))

vi.mock('@/composables/useBusinessTelemetry', () => ({
  useBusinessTelemetryTags: () => ({
    filters: reactive({ deviceAssetId: 'DEV-CNC-01' }),
    tags: computed(() => [
      {
        telemetryTagId: 't1',
        tagKey: 'spindle.speed',
        valueType: 'number',
        unitCode: 'rpm',
        isWritable: true,
        controlMinValue: 0,
        controlMaxValue: 100,
        controlAllowedValues: [],
      },
    ]),
  }),
  useBusinessTelemetryHistory: () => ({
    filters: reactive({ deviceAssetId: 'DEV-CNC-01', tagKey: '' }),
    historyItems: computed(() => [
      {
        itemType: 'sample',
        tagKey: 'spindle.speed',
        value: '42',
        occurredAtUtc: '2026-07-01T06:00:00Z',
      },
    ]),
  }),
  useBusinessTelemetryTagCurrentValue: () => ({
    currentValue: computed(() => cvState.current),
    currentValueError: computed(() => cvState.error),
    currentValuePending: computed(() => cvState.pending),
    refreshCurrentValue: cvState.refresh,
  }),
}))

vi.mock('@/composables/useMasterDataDisplayNames', () => ({
  useMasterDataDisplayNames: () => ({
    resolveDevice: (reference?: string | null) =>
      reference === 'DEV-CNC-01' ? '数控车床 1 号' : undefined,
    resolveDeviceCode: (reference?: string | null) =>
      reference === 'DEV-CNC-01' ? 'DEV-CNC-01' : undefined,
    formatUom: (code?: string | null) => (code === 'rpm' ? '转/分 (rpm)' : (code ?? '')),
  }),
}))

vi.mock('@/utils/notify', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/utils/notify')>()),
  notifyError: vi.fn(),
  notifySuccess: vi.fn(),
  notifyOperationFailure: vi.fn(),
}))

const stubs = {
  NvSheet: { template: '<div><slot /></div>' },
  NvSheetContent: { template: '<div><slot /></div>' },
  NvSheetHeader: { template: '<div><slot /></div>' },
  NvSheetTitle: { template: '<h2><slot /></h2>' },
  NvSheetDescription: { template: '<p><slot /></p>' },
  NvSheetFooter: { template: '<div><slot /></div>' },
  NvTabs: { props: ['modelValue'], template: '<div><slot /></div>' },
  NvTabsList: { template: '<div><slot /></div>' },
  NvTabsTrigger: { props: ['value'], template: '<button type="button"><slot /></button>' },
  NvSelect: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template:
      '<select :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)"><slot /></select>',
  },
  NvSelectTrigger: { template: '<span><slot /></span>' },
  NvSelectValue: { template: '<span />' },
  NvSelectContent: { template: '<slot />' },
  NvSelectItem: { props: ['value'], template: '<option :value="value"><slot /></option>' },
}

function mountSheet() {
  return mount(DeviceControlSheet, {
    props: { deviceAssetId: 'DEV-CNC-01', open: true },
    global: { stubs },
  })
}

async function selectTag(wrapper: ReturnType<typeof mount>) {
  // The tag <NvSelect> stub renders as the native <select> (id lives on its trigger span).
  await wrapper.find('select').setValue('spindle.speed')
  await flushPromises()
}

beforeEach(() => {
  state.dispatch.mockClear()
  state.startTracking.mockClear()
  state.tracked = undefined
  cvState.current = { hasSample: true, value: 55 }
  cvState.pending = false
  cvState.error = undefined
  cvState.refresh.mockClear()
})

describe('DeviceControlSheet', () => {
  it('shows the tag value range and the real current value from the current-value read-face', async () => {
    const wrapper = mountSheet()
    await selectTag(wrapper)

    expect(wrapper.text()).toContain('设备控制 · 数控车床 1 号（DEV-CNC-01）')
    expect(wrapper.text()).toContain('值域：0 ~ 100 转/分 (rpm)')
    expect(wrapper.text()).toContain('当前值')
    expect(wrapper.text()).toContain('55')
    expect(wrapper.text()).not.toContain('暂无读数')
  })

  it('distinguishes current-value loading, read failure and no-sample states', async () => {
    cvState.pending = true
    let wrapper = mountSheet()
    await selectTag(wrapper)
    expect(wrapper.text()).toContain('读取中')

    cvState.pending = false
    cvState.error = new Error('boom')
    wrapper = mountSheet()
    await selectTag(wrapper)
    // A read failure must not be reported as a business "no sample"; it offers a retry.
    expect(wrapper.text()).toContain('读取失败，点击重试')
    expect(wrapper.text()).not.toContain('暂无读数')
    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('读取失败'))!
      .trigger('click')
    expect(cvState.refresh).toHaveBeenCalled()

    cvState.error = undefined
    cvState.current = { hasSample: false, value: null }
    wrapper = mountSheet()
    await selectTag(wrapper)
    expect(wrapper.text()).toContain('暂无读数')
  })

  it('blocks submit and shows an error for an out-of-range value', async () => {
    const wrapper = mountSheet()
    await selectTag(wrapper)
    await wrapper.find('#devctl-value').setValue('120')
    await wrapper.find('#devctl-reason').setValue('ramp up')
    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('确认下发'))!
      .trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('高于允许最大值 100')
    expect(state.dispatch).not.toHaveBeenCalled()
  })

  it('dispatches a valid write-tag command and enters the tracking phase', async () => {
    const wrapper = mountSheet()
    await selectTag(wrapper)
    await wrapper.find('#devctl-value').setValue('80')
    await wrapper.find('#devctl-reason').setValue('ramp to setpoint')
    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('确认下发'))!
      .trigger('click')
    await flushPromises()

    expect(state.dispatch).toHaveBeenCalledWith({
      commandType: 'write-tag',
      tagKey: 'spindle.speed',
      value: '80',
      reason: 'ramp to setpoint',
    })
    expect(state.startTracking).toHaveBeenCalledWith('cmd-1')
  })

  it('says the device receipt in business words and keeps raw codes off screen', async () => {
    state.tracked = {
      commandType: 'write-tag',
      status: 'failed',
      approval: { status: 'approved' },
      attempts: [
        {
          attemptId: 'a1',
          status: 'failed',
          failureCode: 'opcua.write.rejected',
          output: {
            deviceReceiptCode: 'BadOutOfRange',
            deviceReceiptMessage: 'value exceeds node range',
          },
        },
      ],
    }
    const wrapper = mountSheet()
    await selectTag(wrapper)
    await wrapper.find('#devctl-value').setValue('80')
    await wrapper.find('#devctl-reason').setValue('ramp to setpoint')
    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('确认下发'))!
      .trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('设备回执')
    expect(wrapper.text()).toContain('设备拒绝执行')
    expect(wrapper.text()).not.toContain('BadOutOfRange')
    expect(wrapper.text()).not.toContain('value exceeds node range')
    expect(wrapper.text()).not.toContain('opcua.write.rejected')
  })
})
