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
  // 走查发现:卡片曾写死 border-none shadow-none,在同色列上"裸奔"(亮色白底白卡/暗色黑底黑卡)。
  // 现用 NvCard 默认 .ds-card ring+阴影,亮/暗都有清晰边界。此测试锁定不再回退。
  it('renders a defined card (no invisible border-none/shadow-none override)', () => {
    const card = mountForm().find('[data-slot="card-pro"]')
    expect(card.exists()).toBe(true)
    expect(card.classes()).toContain('ds-card')
    expect(card.classes()).not.toContain('border-none')
    expect(card.classes()).not.toContain('shadow-none')
  })

  it('emits submit with a trimmed login name and the raw password', async () => {
    const wrapper = mountForm()
    await wrapper.find('#login-name').setValue('  admin  ')
    await wrapper.find('#password').setValue('secret ')
    await wrapper.find('form').trigger('submit')
    expect(wrapper.emitted('submit')?.[0]).toEqual([{ loginName: 'admin', password: 'secret ' }])
  })
})
