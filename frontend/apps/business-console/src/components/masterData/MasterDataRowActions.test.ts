import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import MasterDataRowActions from './MasterDataRowActions.vue'

// 下拉与详情弹层含 reka portal/Teleport，jsdom 卸载会崩——就地渲染。
const stubs = {
  NvRowActions: { template: '<div><slot /></div>' },
  NvDropdownMenuContent: { template: '<div><slot /></div>' },
  NvDropdownMenuItem: {
    props: ['disabled'],
    emits: ['click'],
    template:
      '<button type="button" :disabled="disabled" @click="$emit(\'click\', $event)"><slot /></button>',
  },
  NvDialog: { template: '<div><slot /></div>' },
  NvDialogContent: { template: '<div><slot /></div>' },
  NvDialogHeader: { template: '<div><slot /></div>' },
  NvDialogFooter: { template: '<div><slot /></div>' },
  NvDialogTitle: { template: '<h2><slot /></h2>' },
  NvDialogDescription: { template: '<p><slot /></p>' },
}

function mountRowActions(active: boolean, rowOverride: Record<string, unknown> = {}) {
  return mount(MasterDataRowActions, {
    props: {
      row: {
        resourceType: 'unit-of-measure',
        code: 'EA',
        displayName: '个',
        active,
        ...rowOverride,
      },
      entityLabel: '计量单位',
      detailFields: [{ label: '名称', value: '个' }],
    },
    global: { stubs },
  })
}

function findButton(wrapper: ReturnType<typeof mountRowActions>, text: string) {
  return wrapper.findAll('button').find((b) => b.text().trim() === text)
}

describe('MasterDataRowActions（只负责触发，不承载确认框）', () => {
  it('不再自带停用/启用确认框——确认框收在页面层单实例（#1591）', () => {
    const wrapper = mountRowActions(true)
    // 组件里若又出现 AlertDialog，就说明确认框被搬回了行内。
    expect(wrapper.html()).not.toContain('alertdialog')
    expect(findButton(wrapper, '确认停用')).toBeUndefined()
  })

  it('启用中的行给「停用」，点它只发 toggle 事件', async () => {
    const wrapper = mountRowActions(true)
    await findButton(wrapper, '停用')!.trigger('click')
    await flushPromises()

    expect(wrapper.emitted('toggle')).toHaveLength(1)
    expect((wrapper.emitted('toggle')![0] as unknown[])[0]).toMatchObject({ code: 'EA' })
  })

  it('已停用的行给「启用」', async () => {
    const wrapper = mountRowActions(false)
    expect(findButton(wrapper, '停用')).toBeUndefined()
    await findButton(wrapper, '启用')!.trigger('click')
    expect(wrapper.emitted('toggle')).toHaveLength(1)
  })

  it('编辑只发事件给页面（页面打开全字段表单带回填）', async () => {
    const wrapper = mountRowActions(true)
    await findButton(wrapper, '编辑')!.trigger('click')
    expect(wrapper.emitted('edit')).toHaveLength(1)
  })

  it('没有编码的行禁用编辑与停用/启用', () => {
    // 显式覆盖成「无编码」——默认参数会把 undefined 吃掉，那样测的就还是有编码的行。
    const wrapper = mountRowActions(true, { code: undefined })
    expect(findButton(wrapper, '编辑')!.attributes('disabled')).toBeDefined()
    expect(findButton(wrapper, '停用')!.attributes('disabled')).toBeDefined()
    // 查看详情始终可用（只读、渲染行内已有字段）。
    expect(findButton(wrapper, '查看详情')!.attributes('disabled')).toBeUndefined()
  })

  it('查看详情展示传入字段与状态', async () => {
    const wrapper = mountRowActions(true)
    await findButton(wrapper, '查看详情')!.trigger('click')
    await flushPromises()
    expect(wrapper.text()).toContain('计量单位详情')
    expect(wrapper.text()).toContain('名称')
  })
})
