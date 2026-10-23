import { mount } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, nextTick, ref } from 'vue'
import { NvDialog, NvDialogContent, NvDialogDescription, NvDialogFooter, NvDialogTitle } from '.'

// 真实 Portal / Content / Footer 交互；几何与 Tab 由文档站浏览器回归证明。
describe('NvDialog footer composition', () => {
  let wrapper: ReturnType<typeof mount> | undefined
  afterEach(() => {
    wrapper?.unmount()
    wrapper = undefined
    document.body.innerHTML = ''
  })

  async function open(nested: boolean) {
    const submit = vi.fn()
    wrapper = mount(
      defineComponent({
        setup() {
          const isOpen = ref(true)
          return () =>
            h(
              NvDialog,
              { open: isOpen.value, 'onUpdate:open': (v: boolean) => (isOpen.value = v) },
              {
                default: () =>
                  h(NvDialogContent, null, {
                    default: () => {
                      const footer = h(
                        NvDialogFooter,
                        { showCloseButton: true },
                        {
                          default: () => h('button', { type: 'submit' }, '确认'),
                        },
                      )
                      return [
                        h(NvDialogTitle, () => '组合测试'),
                        h(NvDialogDescription, () => '验证真实页脚交互'),
                        nested
                          ? h(
                              'form',
                              {
                                onSubmit: (event: Event) => {
                                  event.preventDefault()
                                  submit()
                                },
                              },
                              [footer],
                            )
                          : footer,
                      ]
                    },
                  }),
              },
            )
        },
      }),
      { attachTo: document.body },
    )
    await nextTick()
    return submit
  }

  it.each([false, true])('Footer_CloseButton_ClosesRealDialog_nested_%s', async (nested) => {
    await open(nested)
    expect(document.querySelector('[role="dialog"]')?.textContent).toContain('组合测试')
    const close = [
      ...document.querySelectorAll<HTMLButtonElement>('[data-slot="nv-dialog-footer"] button'),
    ].find((button) => button.textContent === '关闭')
    expect(close).toBeDefined()
    close!.click()
    await nextTick()
    expect(wrapper!.findComponent(NvDialog).props('open')).toBe(false)
  })

  it('Footer_NestedForm_ConfirmationSubmitsWithoutClosing', async () => {
    const submit = await open(true)
    document
      .querySelector<HTMLButtonElement>('[data-slot="nv-dialog-footer"] button[type="submit"]')!
      .click()
    await nextTick()
    expect(submit).toHaveBeenCalledOnce()
    expect(document.querySelector('[role="dialog"]')?.textContent).toContain('组合测试')
  })
})
