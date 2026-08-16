import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'

import MasterDataRowActions from './MasterDataRowActions.vue'

const stub = vi.hoisted(() => ({
  disable: vi.fn().mockResolvedValue({}),
  enable: vi.fn().mockResolvedValue({}),
  toastSuccess: vi.fn(),
  toastError: vi.fn(),
}))

vi.mock('@nerv-iip/ui', async (orig) => ({
  ...(await orig<typeof import('@nerv-iip/ui')>()),
  toast: { success: stub.toastSuccess, error: stub.toastError },
}))

// 下拉与确认弹层都含 reka portal/Teleport，jsdom 卸载会崩——就地渲染，便于填原因、点确认。
const stubs = {
  NvRowActions: { template: '<div><slot /></div>' },
  NvDropdownMenuContent: { template: '<div><slot /></div>' },
  NvDropdownMenuItem: {
    emits: ['click'],
    template: '<button type="button" @click="$emit(\'click\', $event)"><slot /></button>',
  },
  NvDialog: { template: '<div><slot /></div>' },
  NvDialogContent: { template: '<div><slot /></div>' },
  NvDialogHeader: { template: '<div><slot /></div>' },
  NvDialogFooter: { template: '<div><slot /></div>' },
  NvDialogTitle: { template: '<h2><slot /></h2>' },
  NvDialogDescription: { template: '<p><slot /></p>' },
  NvAlertDialog: { template: '<div><slot /></div>' },
  NvAlertDialogContent: { template: '<div><slot /></div>' },
  NvAlertDialogHeader: { template: '<div><slot /></div>' },
  NvAlertDialogFooter: { template: '<div><slot /></div>' },
  NvAlertDialogTitle: { template: '<h2><slot /></h2>' },
  NvAlertDialogDescription: { template: '<p><slot /></p>' },
  NvAlertDialogCancel: {
    emits: ['click'],
    template: '<button type="button" @click="$emit(\'click\', $event)"><slot /></button>',
  },
  NvAlertDialogAction: {
    props: ['disabled'],
    emits: ['click'],
    template:
      '<button type="button" :disabled="disabled" @click="$emit(\'click\', $event)"><slot /></button>',
  },
}

const actions = {
  disable: stub.disable,
  enable: stub.enable,
  disablePending: shallowRef(false),
  enablePending: shallowRef(false),
}

function mountRowActions(active: boolean) {
  return mount(MasterDataRowActions, {
    props: {
      row: {
        resourceType: 'unit-of-measure',
        code: 'EA',
        displayName: '个',
        active,
      },
      entityLabel: '计量单位',
      detailFields: [{ label: '名称', value: '个' }],
      actions,
    },
    global: { stubs },
  })
}

function findButton(wrapper: ReturnType<typeof mountRowActions>, text: string) {
  return wrapper.findAll('button').find((b) => b.text().trim() === text)
}

beforeEach(() => {
  stub.disable.mockClear()
  stub.enable.mockClear()
  stub.toastSuccess.mockClear()
  actions.disablePending.value = false
  actions.enablePending.value = false
})

describe('MasterDataRowActions 生命周期原因', () => {
  it('停用确认框提供原因输入，空原因时确认按钮禁用且不发请求', async () => {
    const wrapper = mountRowActions(true)
    await findButton(wrapper, '停用')!.trigger('click')
    await flushPromises()

    const reasonInput = wrapper.find('input[data-testid="lifecycle-reason"]')
    expect(reasonInput.exists()).toBe(true)

    const confirm = findButton(wrapper, '确认停用')!
    expect(confirm.attributes('disabled')).toBeDefined()

    await confirm.trigger('click')
    await flushPromises()
    expect(stub.disable).not.toHaveBeenCalled()

    // 纯空白同样不算原因。
    await reasonInput.setValue('   ')
    await flushPromises()
    expect(findButton(wrapper, '确认停用')!.attributes('disabled')).toBeDefined()
  })

  it('停用把用户填写的原因原样传给请求（去首尾空白）', async () => {
    const wrapper = mountRowActions(true)
    await findButton(wrapper, '停用')!.trigger('click')
    await flushPromises()
    await wrapper
      .find('input[data-testid="lifecycle-reason"]')
      .setValue('  产线拆除，改用公制单位  ')
    await flushPromises()

    const confirm = findButton(wrapper, '确认停用')!
    expect(confirm.attributes('disabled')).toBeUndefined()
    await confirm.trigger('click')
    await flushPromises()

    expect(stub.disable).toHaveBeenCalledWith('EA', { reason: '产线拆除，改用公制单位' })
    expect(stub.toastSuccess).toHaveBeenCalled()
  })

  it('重新启用同样必填原因并随请求提交', async () => {
    const wrapper = mountRowActions(false)
    await findButton(wrapper, '启用')!.trigger('click')
    await flushPromises()

    expect(findButton(wrapper, '确认启用')!.attributes('disabled')).toBeDefined()
    await wrapper.find('input[data-testid="lifecycle-reason"]').setValue('整改完成，恢复使用')
    await flushPromises()
    await findButton(wrapper, '确认启用')!.trigger('click')
    await flushPromises()

    expect(stub.enable).toHaveBeenCalledWith('EA', { reason: '整改完成，恢复使用' })
  })

  it('再次打开确认框时原因已清空，不残留上一条', async () => {
    const wrapper = mountRowActions(true)
    await findButton(wrapper, '停用')!.trigger('click')
    await flushPromises()
    await wrapper.find('input[data-testid="lifecycle-reason"]').setValue('供应商终止合作')
    await flushPromises()
    await findButton(wrapper, '确认停用')!.trigger('click')
    await flushPromises()

    await findButton(wrapper, '停用')!.trigger('click')
    await flushPromises()

    expect(
      (wrapper.find('input[data-testid="lifecycle-reason"]').element as HTMLInputElement).value,
    ).toBe('')
    expect(findButton(wrapper, '确认停用')!.attributes('disabled')).toBeDefined()
  })

  it('提交失败时保留已填原因，便于重试', async () => {
    stub.disable.mockRejectedValueOnce(new Error('停用失败'))
    const wrapper = mountRowActions(true)
    await findButton(wrapper, '停用')!.trigger('click')
    await flushPromises()
    await wrapper.find('input[data-testid="lifecycle-reason"]').setValue('设备报废')
    await flushPromises()
    await findButton(wrapper, '确认停用')!.trigger('click')
    await flushPromises()

    expect(stub.toastError).toHaveBeenCalled()
    expect(
      (wrapper.find('input[data-testid="lifecycle-reason"]').element as HTMLInputElement).value,
    ).toBe('设备报废')
  })
})
