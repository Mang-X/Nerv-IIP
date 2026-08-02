import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import { h, nextTick } from 'vue'

// 根/触发器无样式，barrel 里是 reka DialogRoot 的重导出。
import { DialogRoot as NvSheet } from 'reka-ui'
import NvSheetContent from './NvSheetContent.vue'
import NvSheetHeader from './NvSheetHeader.vue'
import NvSheetTitle from './NvSheetTitle.vue'

/**
 * 抽屉版式的两条硬约束。
 *
 * jsdom 不跑 Tailwind，算不出实际像素，所以这里断言的是**类名契约**——
 * 这些类是 `cn()` 里的字面量，去掉就一定断。比起「测不了就不测」，
 * 钉住字面量至少能拦住「有人顺手删掉」。真实像素由走查截图把关。
 */
describe('NvSheetContent 版式契约', () => {
  async function mountSheet() {
    return mount(NvSheet, {
      props: { open: true },
      slots: {
        default: () =>
          h(NvSheetContent, null, {
            default: () => [
              h(NvSheetHeader, null, { default: () => h(NvSheetTitle, () => '标题') }),
              h('div', { 'data-testid': 'body' }, '正文'),
            ],
          }),
      },
      attachTo: document.body,
    })
    // portal 是异步挂载的，不等一拍第一条用例会查不到（后面的用例反而借到上一条的残留 DOM，
    // 于是「第一条红、第二条绿」——这种顺序依赖比直接全红更难查）。
    await nextTick()
    return wrapper
  }

  /**
   * 内容走 DialogPortal 传送到 body，`wrapper.find` 找不到——必须查 document。
   * 这一步本身也算个小护栏：查不到就说明 portal 结构变了。
   */
  function contentClasses() {
    const node = document.querySelector('[data-slot="nv-sheet-content"]')
    expect(node, '找不到 nv-sheet-content：portal 结构可能变了').not.toBeNull()
    return node?.getAttribute('class') ?? ''
  }

  /**
   * owner 第五轮亲验点名：工单紧急度解释抽屉里「计算时间」「保存优先级」与 CR/Slack
   * 判定表全压在面板右缘上。根因是基类只有 `gap-4`，**一点水平 padding 都没有**，
   * 而 21 个调用点没有一个自己写 `px-*`——也就是说所有抽屉都两侧贴死。
   */
  it('正文子元素带水平内边距，头尾排除在外（否则双重内边距且背景缩进）', async () => {
    await mountSheet()
    const classes = contentClasses()
    expect(classes).toContain(
      '[&>*:not([data-slot=nv-sheet-header]):not([data-slot=nv-sheet-footer])]:px-4',
    )
  })

  /**
   * #1421：flex 子项的 `min-width:auto` 会让宽表格按内容最小宽把自己撑过抽屉边界。
   * 与上面那条是**两件事**——一个管「超宽内容顶破面板」，一个管「正常内容贴死边缘」，
   * 当时只解决了前者，所以这里一并钉住，避免下次又只修一半。
   */
  it('直接子元素钉住 min-w-0，超宽内容各自横向滚动而不顶破面板', async () => {
    await mountSheet()
    const classes = contentClasses()
    expect(classes).toContain('[&>*]:min-w-0')
    expect(classes).toContain('overflow-y-auto')
  })
})
