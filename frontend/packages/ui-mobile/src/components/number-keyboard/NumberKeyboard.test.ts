import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import NumberKeyboard from './NumberKeyboard.vue'

function keyboardButtons(): HTMLButtonElement[] {
  const keyboard = document.querySelector('[data-slot="number-keyboard"]')
  return Array.from(keyboard?.querySelectorAll('button') ?? []) as HTMLButtonElement[]
}
const buttonTexts = () => keyboardButtons().map((b) => b.textContent?.trim())
const settle = () => new Promise((r) => setTimeout(r, 0))

describe('NumberKeyboard', () => {
  it('renders a single 完成 confirm (no duplicate sub-touch header button)', async () => {
    const wrapper = mount(NumberKeyboard, { props: { show: true }, attachTo: document.body })
    await settle()
    // 头部小「完成」已删（其高度 <44px 不达触点基线，且与底部完成重复）→ 只剩底部大键。
    expect(buttonTexts().filter((t) => t === '完成')).toHaveLength(1)
    wrapper.unmount()
  })

  it('does not render the ± sign key by default', async () => {
    const wrapper = mount(NumberKeyboard, { props: { show: true }, attachTo: document.body })
    await settle()
    expect(buttonTexts()).not.toContain('±')
    wrapper.unmount()
  })

  it('shows the ± sign key when signToggle is on (with a decimal extraKey)', async () => {
    const wrapper = mount(NumberKeyboard, {
      props: { show: true, signToggle: true, extraKey: '.' },
      attachTo: document.body,
    })
    await settle()
    const texts = buttonTexts()
    expect(texts).toContain('±')
    expect(texts).toContain('.')
    expect(texts).toContain('0')
    wrapper.unmount()
  })

  it('reserves the ± column even when the extraKey is hidden (extraKey="")', async () => {
    const wrapper = mount(NumberKeyboard, {
      props: { show: true, signToggle: true, extraKey: '' },
      attachTo: document.body,
    })
    await settle()
    // ± 不会因 0 占满而溢出到第 5 行：signToggle 恒为 ± 预留一列（0 收到 col-span-2）。
    expect(buttonTexts()).toContain('±')
    expect(buttonTexts()).not.toContain('.')
    const zero = keyboardButtons().find((b) => b.textContent?.trim() === '0')
    expect(zero?.className).toContain('col-span-2')
    wrapper.unmount()
  })

  it('toggles the leading sign of the value via ±', async () => {
    const wrapper = mount(NumberKeyboard, {
      props: { show: true, signToggle: true, modelValue: '5' },
      attachTo: document.body,
    })
    await settle()
    keyboardButtons()
      .find((b) => b.textContent?.trim() === '±')
      ?.click()
    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual(['-5'])
    wrapper.unmount()
  })

  it('removes the leading minus when toggled on an already-negative value', async () => {
    const wrapper = mount(NumberKeyboard, {
      props: { show: true, signToggle: true, modelValue: '-5' },
      attachTo: document.body,
    })
    await settle()
    keyboardButtons()
      .find((b) => b.textContent?.trim() === '±')
      ?.click()
    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual(['5'])
    wrapper.unmount()
  })
})
