import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import NvEntityPicker from './NvEntityPicker.vue'

const options = [
  { value: 'SKU-FG-100', label: '前减振器总成', hint: '成品' },
  { value: 'RM-200', label: '活塞杆毛坯', hint: '原材料' },
]

function panelOpen(): boolean {
  return document.body.querySelector('[role="listbox"]') !== null
}

describe('NvEntityPicker', () => {
  it('shows the placeholder when nothing is selected, and name + code when selected', () => {
    const empty = mount(NvEntityPicker, {
      props: { options, title: '选择物料', placeholder: '请选择物料' },
    })
    expect(empty.text()).toContain('请选择物料')

    const selected = mount(NvEntityPicker, {
      props: { options, title: '选择物料', modelValue: 'RM-200' },
    })
    expect(selected.text()).toContain('活塞杆毛坯')
    expect(selected.text()).toContain('RM-200')
  })

  it('is selection-only — the trigger never accepts free text', () => {
    const wrapper = mount(NvEntityPicker, { props: { options, title: '选择物料' } })
    expect(wrapper.find('input').exists()).toBe(false)
  })

  // owner 走查：「不支持直接下拉，我记得有直接下拉的内部支持搜索」。
  // 默认形态必须是「点一下直接展开下拉、下拉内自带搜索框」，不是先弹一个对话框。
  it('defaults to the dropdown form: one click opens an in-place listbox with a search box', async () => {
    const wrapper = mount(NvEntityPicker, {
      props: { options, title: '选择物料', sourceText: '数据来自物料主数据' },
      attachTo: document.body,
    })

    expect(wrapper.get('button[aria-haspopup]').attributes('aria-haspopup')).toBe('listbox')

    await wrapper.get('button[aria-haspopup]').trigger('click')
    await flushPromises()

    expect(panelOpen()).toBe(true)
    expect(document.body.querySelector('input[role="combobox"]')).not.toBeNull()
    expect(document.body.querySelector('[role="listbox"]')?.textContent).toContain('前减振器总成')
    // 编码与来源注脚是 NvEntityPicker 区别于 NvSearchSelect 的地方，两种形态都要有。
    expect(document.body.textContent).toContain('SKU-FG-100')
    expect(document.body.textContent).toContain('数据来自物料主数据')

    wrapper.unmount()
  })

  // owner 走查原话：「总是会先下拉出现然后马上消失」。
  // 同一次交互产生的第二次开关必须被闸门吞掉，浮层不能自己关。
  it('stays open after the click that opened it (no open-then-vanish flash)', async () => {
    const wrapper = mount(NvEntityPicker, {
      props: { options, title: '选择物料' },
      attachTo: document.body,
    })

    // 同一轮事件循环里连发两次点击 —— 祖先 label 转发点击 / as-child 触发器重复触发 /
    // 浮层挂载瞬间把这次点击当成层外点击，三者最终都表现成这样。
    const btn = wrapper.get('button[aria-haspopup]').element
    btn.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    btn.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    await flushPromises()

    expect(panelOpen()).toBe(true)

    wrapper.unmount()
  })

  // 闸门只吞「同一轮」的关闭，不能把正常的收起也焊死。
  it('still closes on a genuine second click', async () => {
    const wrapper = mount(NvEntityPicker, {
      props: { options, title: '选择物料' },
      attachTo: document.body,
    })

    await wrapper.get('button[aria-haspopup]').trigger('click')
    await flushPromises()
    expect(panelOpen()).toBe(true)

    await wrapper.get('button[aria-haspopup]').trigger('click')
    await flushPromises()
    expect(panelOpen()).toBe(false)

    wrapper.unmount()
  })

  it('an ancestor <label> re-dispatching the click does not close the dropdown', async () => {
    const wrapper = mount(
      {
        components: { NvEntityPicker },
        props: ['options', 'title'],
        template: `<label><span>物料</span><NvEntityPicker :options="options" :title="title" /></label>`,
      },
      { props: { options, title: '选择物料' }, attachTo: document.body },
    )

    await wrapper.get('button[aria-haspopup]').trigger('click')
    await flushPromises()
    expect(panelOpen()).toBe(true)

    wrapper.unmount()
  })

  it('picks by click and closes', async () => {
    const wrapper = mount(NvEntityPicker, {
      props: { options, title: '选择物料' },
      attachTo: document.body,
    })
    await wrapper.get('button[aria-haspopup]').trigger('click')
    await flushPromises()

    const optionButtons = [...document.body.querySelectorAll<HTMLButtonElement>('[role="option"]')]
    optionButtons.find((b) => b.textContent?.includes('RM-200'))?.click()
    await flushPromises()

    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual(['RM-200'])
    wrapper.unmount()
  })

  it('opts into the dialog form for heavy catalogues', async () => {
    const wrapper = mount(NvEntityPicker, {
      props: { options, title: '选择物料', variant: 'dialog' as const },
      attachTo: document.body,
    })

    expect(wrapper.get('button[aria-haspopup]').attributes('aria-haspopup')).toBe('dialog')

    await wrapper.get('button[aria-haspopup]').trigger('click')
    await flushPromises()

    expect(document.body.querySelector('[role="dialog"]')).not.toBeNull()
    expect(document.body.querySelector('input[role="combobox"]')).not.toBeNull()
    expect(document.body.querySelector('[role="listbox"]')?.textContent).toContain('前减振器总成')

    wrapper.unmount()
  })

  // value 是内部 id（GUID）的调用点：print-batches / eco / bom-analysis。
  // 组件此前无条件把 value 当编码渲染，GUID 会直接露到界面上。
  describe('value 是内部标识时不能把它当编码印出来', () => {
    const GUID = '8f14e45f-ceea-467a-9f1e-4b2c3d5a6e7b'
    const idOptions = [{ value: GUID, label: '前减振器总成', code: 'SKU-FG-100' }]

    it('给了 code 就显示 code，不显示 value', async () => {
      const wrapper = mount(NvEntityPicker, {
        props: { options: idOptions, title: '选择物料', modelValue: GUID },
        attachTo: document.body,
      })

      // 触发器上
      expect(wrapper.text()).toContain('SKU-FG-100')
      expect(wrapper.text()).not.toContain(GUID)

      // 下拉里
      await wrapper.get('button[aria-haspopup]').trigger('click')
      await flushPromises()
      const list = document.body.querySelector('[role="listbox"]')
      expect(list?.textContent).toContain('SKU-FG-100')
      expect(list?.textContent).not.toContain(GUID)

      wrapper.unmount()
    })

    it('show-code=false 时编码行整条不渲染', async () => {
      const wrapper = mount(NvEntityPicker, {
        props: {
          options: [{ value: GUID, label: '前减振器总成' }],
          title: '选择物料',
          modelValue: GUID,
          showCode: false,
        },
        attachTo: document.body,
      })

      expect(wrapper.text()).toContain('前减振器总成')
      expect(wrapper.text()).not.toContain(GUID)
      // 没有编码就别留一对空括号。
      expect(wrapper.text()).not.toContain('（）')

      await wrapper.get('button[aria-haspopup]').trigger('click')
      await flushPromises()
      expect(document.body.querySelector('[role="listbox"]')?.textContent).not.toContain(GUID)

      wrapper.unmount()
    })

    it('按人读编码搜得到，GUID 不参与匹配', async () => {
      const wrapper = mount(NvEntityPicker, {
        props: { options: idOptions, title: '选择物料' },
        attachTo: document.body,
      })
      await wrapper.get('button[aria-haspopup]').trigger('click')
      await flushPromises()

      const search = document.body.querySelector<HTMLInputElement>('input[role="combobox"]')!
      search.value = 'SKU-FG'
      search.dispatchEvent(new Event('input', { bubbles: true }))
      await flushPromises()
      expect(document.body.querySelectorAll('[role="option"]').length).toBe(1)

      search.value = '8f14e45f'
      search.dispatchEvent(new Event('input', { bubbles: true }))
      await flushPromises()
      expect(document.body.querySelectorAll('[role="option"]').length).toBe(0)

      wrapper.unmount()
    })

    it('value 本身就是人读编码时行为不变（默认仍显示编码）', () => {
      const wrapper = mount(NvEntityPicker, {
        props: { options, title: '选择物料', modelValue: 'RM-200' },
      })
      expect(wrapper.text()).toContain('RM-200')
    })
  })

  it('clears the selection without opening the picker', async () => {
    const wrapper = mount(NvEntityPicker, {
      props: {
        options,
        title: '选择物料',
        modelValue: 'RM-200',
        clearable: true,
        ariaLabel: '物料',
      },
      attachTo: document.body,
    })

    const clearBtn = wrapper.get('button[aria-label="清除物料"]')
    await clearBtn.trigger('pointerdown')
    await clearBtn.trigger('mousedown')
    await clearBtn.trigger('click')
    await flushPromises()

    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual([''])
    // 点清除叉不能顺带把浮层打开 —— 叉是压在触发器上方的。
    expect(panelOpen()).toBe(false)

    wrapper.unmount()
  })
})
