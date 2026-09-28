import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, shallowRef } from 'vue'

import DeviceTelemetryPointsSheet from './DeviceTelemetryPointsSheet.vue'

const state = vi.hoisted(() => ({
  points: [] as Record<string, unknown>[],
  savePoint: vi.fn(() => Promise.resolve({ data: { telemetryTagId: 'tag-new' } })),
  disablePoint: vi.fn(() => Promise.resolve({ data: { telemetryTagId: 'tag-1' } })),
}))

vi.mock('@/composables/useBusinessTelemetryPoints', () => ({
  useBusinessTelemetryPoints: () => ({
    points: computed(() => state.points),
    pointsError: shallowRef(),
    pointsPending: shallowRef(false),
    savePoint: state.savePoint,
    savePointPending: shallowRef(false),
    disablePoint: state.disablePoint,
    disablePointPending: shallowRef(false),
  }),
}))

vi.mock('@/utils/notify', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/utils/notify')>()),
  notifySuccess: vi.fn(),
  notifyOperationFailure: vi.fn(),
}))

const stubs = {
  NvSheet: { template: '<div><slot /></div>' },
  NvSheetContent: { template: '<div><slot /></div>' },
  NvSheetHeader: { template: '<div><slot /></div>' },
  NvSheetTitle: { template: '<h2><slot /></h2>' },
  NvSheetDescription: { template: '<p><slot /></p>' },
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
  NvCheckbox: {
    props: ['modelValue', 'id'],
    emits: ['update:modelValue'],
    template:
      '<input type="checkbox" :id="id" :checked="modelValue" @change="$emit(\'update:modelValue\', $event.target.checked)" />',
  },
}

const COUNT_POINT = {
  telemetryTagId: 'tag-1',
  deviceAssetId: 'EQ00001',
  tagKey: 'parts_count',
  displayName: '成品计数',
  valueType: 'production-count-draft',
  unitCode: 'pcs',
  samplingPolicy: 'sample-60s',
  isWritable: false,
  controlAllowedValues: [],
  isEnabled: true,
}

const SPEED_POINT = {
  telemetryTagId: 'tag-2',
  deviceAssetId: 'EQ00001',
  tagKey: 'spindle.speed',
  displayName: null,
  valueType: 'decimal',
  unitCode: 'rpm',
  samplingPolicy: 'bucket=30s;raw=7d',
  isWritable: true,
  controlMinValue: 100,
  controlMaxValue: 3000,
  controlAllowedValues: ['1500', '2000'],
  isEnabled: true,
}

const RETIRED_POINT = {
  telemetryTagId: 'tag-3',
  deviceAssetId: 'EQ00001',
  tagKey: 'old_count',
  displayName: '旧计数',
  valueType: 'production-count-posted',
  unitCode: 'pcs',
  samplingPolicy: 'sample-60s',
  isWritable: false,
  controlAllowedValues: [],
  isEnabled: false,
}

function mountSheet(canManage = true) {
  return mount(DeviceTelemetryPointsSheet, {
    props: { deviceAssetId: 'EQ00001', deviceTitle: '数控车床（EQ00001）', canManage, open: true },
    global: { stubs },
  })
}

function buttonByText(wrapper: ReturnType<typeof mountSheet>, text: string) {
  const button = wrapper.findAll('button').find((b) => b.text().includes(text))
  if (!button) throw new Error(`button not found: ${text}`)
  return button
}

beforeEach(() => {
  state.points = [COUNT_POINT, SPEED_POINT, RETIRED_POINT]
  state.savePoint.mockClear()
  state.disablePoint.mockClear()
})

describe('DeviceTelemetryPointsSheet', () => {
  it('lists every point with its name, code and business wording, and keeps retired points read-only', () => {
    const wrapper = mountSheet()
    const rows = wrapper.findAll('li')

    expect(wrapper.text()).toContain('采集点位 · 数控车床（EQ00001）')
    expect(rows).toHaveLength(3)
    expect(rows[0]!.text()).toContain('成品计数')
    expect(rows[0]!.text()).toContain('编码 parts_count')
    expect(rows[0]!.text()).toContain('计数（待人工确认）')
    // 没填名称的点位用编码当标题；历史写法 decimal 归到「数值」，不回吐英文码。
    expect(rows[1]!.text()).toContain('spindle.speed')
    expect(rows[1]!.text()).toContain('数值')
    expect(rows[1]!.text()).toContain('可远程写入')
    expect(rows[1]!.text()).not.toContain('decimal')
    // 已停用的点位只能看，不能再编辑或停用。
    expect(rows[2]!.text()).toContain('旧计数')
    expect(rows[2]!.findAll('button')).toHaveLength(0)
    expect(rows[0]!.findAll('button').map((b) => b.text())).toEqual(['编辑', '停用'])
  })

  it('hides every write action from users without the point-management permission', () => {
    const wrapper = mountSheet(false)

    expect(wrapper.text()).not.toContain('新建点位')
    expect(wrapper.findAll('li button')).toHaveLength(0)
  })

  it('creates a counting point with the user-maintained code and no remote write', async () => {
    const wrapper = mountSheet()
    await buttonByText(wrapper, '新建点位').trigger('click')

    await wrapper.find('#point-name').setValue('二号机成品计数')
    await wrapper.find('#point-key').setValue('good_count')
    const selects = wrapper.findAll('select')
    await selects[0]!.setValue('production-count-draft')
    await selects[1]!.setValue('pcs')
    await selects[2]!.setValue('sample-60s')
    // 计数点位不开放远程写入。
    expect(wrapper.find('#point-writable').exists()).toBe(false)

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(state.savePoint).toHaveBeenCalledWith({
      tagKey: 'good_count',
      displayName: '二号机成品计数',
      valueType: 'production-count-draft',
      unitCode: 'pcs',
      samplingPolicy: 'sample-60s',
      isWritable: false,
      controlMinValue: null,
      controlMaxValue: null,
      controlAllowedValues: [],
    })
    expect(wrapper.find('form').exists()).toBe(false)
  })

  it('round-trips the control range and allowed values when editing, and locks the code', async () => {
    const wrapper = mountSheet()
    await wrapper.findAll('li')[1]!.findAll('button')[0]!.trigger('click')

    // 编码锁定：只读显示，没有输入框。
    expect(wrapper.find('input#point-key').exists()).toBe(false)
    expect(wrapper.find('#point-key').text()).toBe('spindle.speed')
    expect((wrapper.find('#point-min').element as HTMLInputElement).value).toBe('100')
    await wrapper.find('#point-name').setValue('主轴转速')
    await wrapper.find('#point-max').setValue('2800')

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(state.savePoint).toHaveBeenCalledWith({
      tagKey: 'spindle.speed',
      displayName: '主轴转速',
      valueType: 'number',
      unitCode: 'rpm',
      samplingPolicy: 'bucket=30s;raw=7d',
      isWritable: true,
      controlMinValue: 100,
      controlMaxValue: 2800,
      controlAllowedValues: ['1500', '2000'],
    })
  })

  it.each([
    ['parts_count', '这台设备已有同编码的点位'],
    ['OLD_COUNT', '这个编码的点位已停用，不能再用，请换一个编码'],
    ['计数', '点位编码只能用字母、数字、点、下划线和短横线'],
  ])('rejects the code %s before saving', async (tagKey, message) => {
    const wrapper = mountSheet()
    await buttonByText(wrapper, '新建点位').trigger('click')
    await wrapper.find('#point-key').setValue(tagKey)
    const selects = wrapper.findAll('select')
    await selects[0]!.setValue('number')
    await selects[1]!.setValue('rpm')

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain(message)
    expect(state.savePoint).not.toHaveBeenCalled()
  })

  it('rejects a write range whose minimum exceeds its maximum', async () => {
    const wrapper = mountSheet()
    await wrapper.findAll('li')[1]!.findAll('button')[0]!.trigger('click')
    await wrapper.find('#point-min').setValue('5000')

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('写入下限不能大于上限')
    expect(state.savePoint).not.toHaveBeenCalled()
  })

  it('disables a point only after the user confirms', async () => {
    const wrapper = mountSheet()
    await wrapper.findAll('li')[0]!.findAll('button')[1]!.trigger('click')

    expect(wrapper.text()).toContain('停用点位：成品计数')
    expect(state.disablePoint).not.toHaveBeenCalled()
    await buttonByText(wrapper, '确认停用').trigger('click')
    await flushPromises()

    expect(state.disablePoint).toHaveBeenCalledWith('parts_count')
    expect(wrapper.findAll('li')).toHaveLength(3)
  })
})
