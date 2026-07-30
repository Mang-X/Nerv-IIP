import { mount } from '@vue/test-utils'
import { CheckboxRoot } from 'reka-ui'
import { nextTick } from 'vue'
import { describe, expect, it } from 'vitest'
import NvCheckbox from './NvCheckbox.vue'

// 组件是多根（button + 隐藏的 v-if 表单域），attributes() 必须锚到方框本体。
const box = '[data-slot="nv-checkbox"]'

// MAN-689 / #1257 回归锁：NvCheckbox 通过 useForwardPropsEmits 转发 reka-ui 的
// CheckboxRoot，其受控入参是 `modelValue` / `update:modelValue`。历史上调用方
// 误用 `v-model:checked`，属性掉进 attrs 变成死属性、事件永不触发，视觉打勾但
// 外部 state 不变（排产池「生成首版」永久禁用）。
//
// 这组断言同时锁住「组件契约」与「实装 reka 版本的原语契约」：若未来升级
// reka 改名回 checked/其它，下面任一断言必须变红。

function propNames(component: unknown): string[] {
  const props = (component as { props?: unknown }).props
  if (Array.isArray(props)) return props as string[]
  if (props && typeof props === 'object') return Object.keys(props as object)
  return []
}

function emitNames(component: unknown): string[] {
  const emits = (component as { emits?: unknown }).emits
  if (Array.isArray(emits)) return emits as string[]
  if (emits && typeof emits === 'object') return Object.keys(emits as object)
  return []
}

describe('nvCheckbox 契约', () => {
  it('实装 reka 的 CheckboxRoot 原语只认 modelValue，不存在 checked', () => {
    const props = propNames(CheckboxRoot)
    expect(props).toContain('modelValue')
    expect(props).not.toContain('checked')
  })

  it('NvCheckbox 声明的 props 与 reka 原语一致：有 modelValue、无 checked', () => {
    const props = propNames(NvCheckbox)
    expect(props).toContain('modelValue')
    expect(props).not.toContain('checked')
  })

  it('NvCheckbox 声明的 emits 是 update:modelValue，而非 update:checked', () => {
    const emits = emitNames(NvCheckbox)
    expect(emits).toContain('update:modelValue')
    expect(emits).not.toContain('update:checked')
  })

  it('受控用法：点击触发 update:modelValue', async () => {
    const wrapper = mount(NvCheckbox, { props: { modelValue: false } })

    await wrapper.get(box).trigger('click')

    expect(wrapper.emitted('update:modelValue')).toEqual([[true]])
    expect(wrapper.emitted('update:checked')).toBeUndefined()
  })

  it('受控用法：外部值驱动渲染，父级不改值则视觉不变', async () => {
    const wrapper = mount(NvCheckbox, { props: { modelValue: false } })

    expect(wrapper.get(box).attributes('data-state')).toBe('unchecked')

    // 受控且父级不回写 —— 点击后 UI 必须保持 unchecked（不得走 reka 内部非受控态）
    await wrapper.get(box).trigger('click')
    await nextTick()
    expect(wrapper.get(box).attributes('data-state')).toBe('unchecked')

    // 父级回写后才勾上
    await wrapper.setProps({ modelValue: true })
    expect(wrapper.get(box).attributes('data-state')).toBe('checked')
  })

  it('非受控（未传 modelValue）时才使用内部状态', async () => {
    const wrapper = mount(NvCheckbox)

    expect(wrapper.get(box).attributes('data-state')).toBe('unchecked')
    await wrapper.get(box).trigger('click')
    await nextTick()
    expect(wrapper.get(box).attributes('data-state')).toBe('checked')
  })

  it('disabled 时不触发 update:modelValue', async () => {
    const wrapper = mount(NvCheckbox, { props: { modelValue: false, disabled: true } })

    await wrapper.get(box).trigger('click')

    expect(wrapper.emitted('update:modelValue')).toBeUndefined()
  })
})
