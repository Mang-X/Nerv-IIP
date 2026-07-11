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

const TERMINAL = new Set(['completed', 'failed', 'rejected', 'abandoned'])

vi.mock('@/composables/useBusinessDeviceControl', () => ({
  deviceControlApprovalLabel: (v?: string | null) => v ?? '未知',
  deviceControlCommandTypeLabel: (v?: string | null) => v ?? '未知',
  deviceControlStatusLabel: (v?: string | null) => v ?? '未知',
  deviceControlStatusTone: () => 'neutral',
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
    currentValue: computed(() => ({
      deviceAssetId: 'DEV-CNC-01',
      tagKey: 'spindle.speed',
      hasSample: true,
      value: 55,
      occurredAtUtc: '2026-07-01T07:00:00Z',
    })),
    currentValuePending: shallowRef(false),
  }),
}))

vi.mock('@/utils/notify', () => ({ notifyError: vi.fn(), notifySuccess: vi.fn() }))

const stubs = {
  SheetPro: { template: '<div><slot /></div>' },
  SheetProContent: { template: '<div><slot /></div>' },
  SheetProHeader: { template: '<div><slot /></div>' },
  SheetProTitle: { template: '<h2><slot /></h2>' },
  SheetProDescription: { template: '<p><slot /></p>' },
  SheetProFooter: { template: '<div><slot /></div>' },
  TabsPro: { props: ['modelValue'], template: '<div><slot /></div>' },
  TabsProList: { template: '<div><slot /></div>' },
  TabsProTrigger: { props: ['value'], template: '<button type="button"><slot /></button>' },
  SelectPro: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template:
      '<select :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)"><slot /></select>',
  },
  SelectProTrigger: { template: '<span><slot /></span>' },
  SelectValue: { template: '<span />' },
  SelectProContent: { template: '<slot />' },
  SelectProItem: { props: ['value'], template: '<option :value="value"><slot /></option>' },
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
})

describe('DeviceControlSheet', () => {
  it('shows the tag value range and the real current value from the current-value read-face', async () => {
    const wrapper = mountSheet()
    await selectTag(wrapper)

    expect(wrapper.text()).toContain('值域：0 ~ 100 rpm')
    expect(wrapper.text()).toContain('当前值')
    expect(wrapper.text()).toContain('55')
  })

  it('blocks submit and shows an error for an out-of-range value', async () => {
    const wrapper = mountSheet()
    await selectTag(wrapper)
    await wrapper.find('#devctl-value').setValue('120')
    await wrapper.find('#devctl-reason').setValue('ramp up')
    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('提交下发'))!
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
      .find((b) => b.text().includes('提交下发'))!
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

  it('renders the device receipt code from the attempt output on failure', async () => {
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
      .find((b) => b.text().includes('提交下发'))!
      .trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('设备回执码')
    expect(wrapper.text()).toContain('BadOutOfRange')
    expect(wrapper.text()).toContain('value exceeds node range')
    // The generic connector code is shown as secondary, not as the primary receipt.
    expect(wrapper.text()).toContain('opcua.write.rejected')
  })
})
