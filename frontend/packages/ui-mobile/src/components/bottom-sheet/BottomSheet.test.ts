import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import BottomSheet from './BottomSheet.vue'

describe('BottomSheet', () => {
  it('renders content into the document when open', async () => {
    mount(BottomSheet, {
      props: { open: true, title: '选择库位' },
      slots: { default: '<div>抽屉内容</div>' },
      attachTo: document.body,
    })
    await new Promise((r) => setTimeout(r, 0))
    expect(document.body.textContent).toContain('选择库位')
    expect(document.body.textContent).toContain('抽屉内容')
  })

  it('does not render content when closed', () => {
    mount(BottomSheet, {
      props: { open: false, title: '隐藏标题' },
      slots: { default: '<div>不可见</div>' },
      attachTo: document.body,
    })
    expect(document.body.textContent).not.toContain('不可见')
  })
})
