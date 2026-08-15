import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import { isPriorityMobileOverlayTarget, MOBILE_OVERLAY_LAYER } from '../../lib/overlay-target'
import NumberKeyboard from './NumberKeyboard.vue'

function keyboardButtons(): HTMLButtonElement[] {
  const keyboard = document.querySelector('[data-slot="number-keyboard"]')
  return Array.from(keyboard?.querySelectorAll('button') ?? []) as HTMLButtonElement[]
}
const buttonTexts = () => keyboardButtons().map((b) => b.textContent?.trim())
const settle = () => new Promise((r) => setTimeout(r, 0))

describe('NumberKeyboard', () => {
  it('uses the priority input overlay layers above modal surfaces', async () => {
    const wrapper = mount(NumberKeyboard, { props: { show: true }, attachTo: document.body })
    await settle()

    const backdrop = document.querySelector<HTMLElement>(
      '[data-mobile-overlay-layer="input-backdrop"]',
    )
    const keyboard = document.querySelector<HTMLElement>(
      '[data-mobile-overlay-layer="input-surface"]',
    )

    expect(MOBILE_OVERLAY_LAYER.inputBackdrop).toBeGreaterThan(MOBILE_OVERLAY_LAYER.surface)
    expect(MOBILE_OVERLAY_LAYER.inputSurface).toBeGreaterThan(MOBILE_OVERLAY_LAYER.inputBackdrop)
    expect(backdrop?.style.zIndex).toBe(String(MOBILE_OVERLAY_LAYER.inputBackdrop))
    expect(keyboard?.style.zIndex).toBe(String(MOBILE_OVERLAY_LAYER.inputSurface))
    expect(keyboard?.className).toContain('pointer-events-auto')
    expect(isPriorityMobileOverlayTarget(keyboard)).toBe(true)
    expect(isPriorityMobileOverlayTarget(document.body)).toBe(false)
    wrapper.unmount()
  })

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
