import {
  DialogClose,
  DialogRoot,
  DialogTrigger,
  DropdownMenuPortal,
  SelectGroup,
  SelectValue,
  TooltipProvider,
  TooltipRoot,
  TooltipTrigger,
} from 'reka-ui'
import { describe, expect, it } from 'vitest'

import * as dialog from './components/pc/dialog'
import * as dropdownMenu from './components/pc/dropdown-menu'
import * as select from './components/pc/select'
import * as sheet from './components/pc/sheet'
import * as tooltip from './components/pc/tooltip'

/**
 * 五族 barrel 把无样式的 reka 根/触发器/关闭件以 `Nv*` 别名再导出。别名不是纯改名：
 * Vue 按 `name || __name` 解析组件身份，而 reka 产物只有 `__name`（即 reka 真名）。
 * 别名必须浅拷贝一份并补上等于导出名的 `name`，否则
 *
 * - `NvDialog` 与 `NvSheet` 同为 reka `DialogRoot`，运行时身份相同、无法分辨；
 * - 消费方按 `Nv` 名做的组件解析（含测试打桩）落到 reka 真名上，静默失配。
 *
 * 失配方向是假绿（找不到就退回真实 reka 组件继续渲染），没有别的门禁能发现，故在此钉死。
 */
const aliases: ReadonlyArray<readonly [string, unknown, unknown]> = [
  ['NvDialog', dialog.NvDialog, DialogRoot],
  ['NvDialogTrigger', dialog.NvDialogTrigger, DialogTrigger],
  ['NvDialogClose', dialog.NvDialogClose, DialogClose],
  ['NvSheet', sheet.NvSheet, DialogRoot],
  ['NvSheetTrigger', sheet.NvSheetTrigger, DialogTrigger],
  ['NvSheetClose', sheet.NvSheetClose, DialogClose],
  ['NvSelectGroup', select.NvSelectGroup, SelectGroup],
  ['NvSelectValue', select.NvSelectValue, SelectValue],
  ['NvTooltip', tooltip.NvTooltip, TooltipRoot],
  ['NvTooltipProvider', tooltip.NvTooltipProvider, TooltipProvider],
  ['NvTooltipTrigger', tooltip.NvTooltipTrigger, TooltipTrigger],
  ['NvDropdownMenuPortal', dropdownMenu.NvDropdownMenuPortal, DropdownMenuPortal],
]

describe('reka 别名再导出的组件身份契约', () => {
  it.each(aliases)('%s 以自身导出名解析', (exportName, alias) => {
    expect((alias as { name?: string }).name).toBe(exportName)
  })

  it.each(aliases)('%s 是 reka 组件的浅拷贝，不是原地改名', (_exportName, alias, source) => {
    // 原地改 `DialogRoot.name` 会让共用同一个 reka 组件的 NvDialog / NvSheet 互相覆盖。
    // 这条不被上一条蕴含：被原地改名的那个别名自己的 `name` 是对的，只有这条能发现它
    // 与 reka 源是同一个对象（实测见 PR 的 M2 变异）。
    expect(alias).not.toBe(source)
  })

  // 这条按 source 生效而不是按别名生效（9 个 source 被 12 个别名共用），故不展开成 12 条。
  it('全部 reka 来源仍是对象式组件', () => {
    // `Object.assign({}, x)` 只搬得动对象式组件的选项。reka 若改成函数式组件，
    // 拷贝结果会是个没有渲染逻辑的空壳，而 `name` 照样设得上——这条断言是那次静默失效的唯一防线。
    for (const [, , source] of aliases) expect(typeof source).toBe('object')
  })
})
