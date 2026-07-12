import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import { createBusinessConsoleI18n } from '@/i18n'
import LoginForm from './LoginForm.vue'

function mountForm() {
  return mount(LoginForm, {
    global: { plugins: [createBusinessConsoleI18n({ locale: 'zh-CN' })] },
  })
}

describe('LoginForm', () => {
  // 走查回归:卡片曾写死 `border-none shadow-none`,把 NvCard 默认边框/阴影盖掉,在同色列上"裸奔"。
  // 只断言 LoginForm 自身契约——不把这两个 Tailwind utility 传给卡片;不耦合 NvCard 的内部实现名
  // (`data-slot=card-pro` / `.ds-card` 等待 #896 收口迁 nv-*,断言它们会无谓脆裂)。卡片"有可见表面"
  // 的样式契约属 NvCard,归 UI 包测试。
  it('does not strip the card surface (no border-none/shadow-none override)', () => {
    const html = mountForm().html()
    expect(html).not.toContain('border-none')
    expect(html).not.toContain('shadow-none')
  })

  it('renders the login fields and submits with a trimmed login name + raw password', async () => {
    const wrapper = mountForm()
    expect(wrapper.find('#login-name').exists()).toBe(true)
    expect(wrapper.find('#password').exists()).toBe(true)

    await wrapper.find('#login-name').setValue('  admin  ')
    await wrapper.find('#password').setValue('secret ')
    await wrapper.find('form').trigger('submit')
    expect(wrapper.emitted('submit')?.[0]).toEqual([{ loginName: 'admin', password: 'secret ' }])
  })
})
