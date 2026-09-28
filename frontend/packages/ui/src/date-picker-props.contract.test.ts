import { readdirSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

/**
 * 「宽度归调用方」这条约定的契约（#3735）。
 *
 * Vue 里 `defineProps` 声明了的名字**不进** `$attrs`——它不会像非声明属性那样
 * 自动落到根元素上。所以组件一旦声明了 `class?:`，就必须在模板里显式把它绑到
 * 某个节点上；否则调用方传的宽度**确定性不生效且不报错**。
 *
 * 踩过的坑：`NvDateRangePicker` 声明了 `class?:` 却在模板里绑了 0 次，四个调用点
 * 新传的 `class="w-full sm:w-64"` 全是空操作——`oee` 实测 256→440px、
 * `reliability` →254.2px、`downtime` 227.7→806.2px，而 CI 与全部单测全绿。
 * 同一轮里同一个约定在孪生组件 `NvDatePicker` 上是对的、在它身上是错的：
 * **改了兄弟忘了另一个**。所以规则钉在这个族的层级上，而不是只修那一行绑定。
 *
 * 为什么不是全仓每个 .vue 都跑一遍：挂载需要 provider / 注入的组件（reka 的
 * Root、依赖注入的复合组件）挂不起来，那样的失败与本约定无关，只会逼出一张
 * 越来越长的豁免名单。约定定义在这一族，就守在这一族。
 *
 * 只覆盖会**透传到 DOM** 的 prop。事件型 prop（`v-model`、`@x`）是行为不是
 * DOM 属性，不在此列。名单是**封闭的**：白名单外的名字一个都不查。
 */

const srcDir = dirname(fileURLToPath(import.meta.url))
const pickerDir = resolve(srcDir, 'components/pc/date-picker')

/**
 * 会透传到 DOM 的 prop——**封闭名单**。`aria-*` / `data-*` 由前缀覆盖，所以那两类
 * 无需枚举；除它们之外只认这六项。
 *
 * 白名单是封闭的，代价要说清楚：日后哪个组件若声明了 `name` / `href` / `width` 这类
 * 同样会落 DOM 的 prop，**本测试不会变红，是根本不检查**。所以新加透传 prop 时必须
 * 手动往这里补一项，注释不替这件事兜底。
 */
const DOM_PROPS = new Set(['class', 'style', 'id', 'title', 'role', 'tabindex'])
const isDomProp = (name: string) => {
  const kebab = name.replace(/[A-Z]/g, (c) => `-${c.toLowerCase()}`)
  return DOM_PROPS.has(kebab) || kebab.startsWith('aria-') || kebab.startsWith('data-')
}

const pickers = readdirSync(pickerDir).filter((name) => name.endsWith('.vue'))
expect(pickers.length).toBeGreaterThan(0)

describe.each(pickers)('%s 声明的 DOM prop 必须被消费', (file) => {
  it('声明为 prop 的 class/id 真的落进 DOM', async () => {
    const module = await import(/* @vite-ignore */ resolve(pickerDir, file))
    const component = module.default
    const declared = Object.keys(component?.props ?? {}).filter(isDomProp)
    // 兜底：白名单与组件脱节（有人从 `DOM_PROPS` 删了名字、或组件改了声明）时这里红。
    // 失败方向不对称——它只在「该组件仅剩的 DOM prop 被移出白名单」时才兜得住；一旦
    // 组件还有第二个 DOM prop，把 `class` 挪出白名单会静默通过。
    expect(declared.length).toBeGreaterThan(0)

    const undelivered: string[] = []
    for (const name of declared) {
      const probe = 'nv-prop-probe'
      const wrapper = mount(component, { props: { [name]: probe } })
      // 真挂载、真读渲染结果：不猜它落在哪个节点上——包一层、portal 出去、
      // 由 reka 的 Root 承接，落在哪一层都行，落在**哪里都不行**才是 bug。
      // 读源码正则做不到「声明了 prop 但模板里没绑」。
      const rendered = wrapper.html()
      if (!rendered.includes(probe)) undelivered.push(name)
      wrapper.unmount()
    }

    expect(
      undelivered,
      `声明为 prop 却没有落进 DOM：${undelivered.join('、')}。声明 prop 不进 $attrs，` +
        `调用方传的值会被静默丢弃且不报错。把它显式绑到某个节点上，或改用非声明的 attr。`,
    ).toEqual([])
  })
})
