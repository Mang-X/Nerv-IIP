import { DialogRoot } from 'reka-ui'
import * as reka from 'reka-ui'
import { describe, expect, it } from 'vitest'

import * as barrel from './index'

/**
 * barrel 把无样式的 reka 根/触发器/关闭件以自有名字再导出。别名不是纯改名：
 * Vue 按 `name || __name` 解析组件身份（`@vue/runtime-core` 的 `getComponentName`），
 * 而 reka 产物只有 `__name`（即 reka 真名）。改了名的别名必须浅拷贝一份并补上
 * 等于导出名的 `name`，否则
 *
 * - `NvDialog` 与 `NvSheet` 同为 reka `DialogRoot`，运行时身份相同、无法分辨；
 * - 消费方按 `Nv` 名做的组件解析（含测试打桩）落到 reka 真名上，静默失配。
 *
 * 失配方向是假绿（找不到就退回真实 reka 组件继续渲染），没有别的门禁能发现，故在此钉死。
 *
 * 这不是命名规范守卫：`name` 决定的是运行时能否被解析到，而不是叫什么好看。
 *
 * ## 检查面
 *
 * 扫的是**整个包公开导出面** `./index`，不限于 `Nv*` 品牌层——凡是从包边界出去、
 * 会被消费方按导出名解析的 reka 再导出，都受这条约束，这是有意为之。
 *
 * 由此带来两处需要知道的边界：
 *
 * - `components/pc`（以及 touch / screen / layout / blocks）在 `./index` 里是 `export *`，
 *   故这些层新增别名**自动进入检查面**；`components/ui`（原版层）走具名清单，
 *   该层新增的别名要进具名清单才会被检查到——那是另一票的事（本文件不为它兜底）。
 * - 原版层现有的 reka 直通再导出（`components/ui/dropdown-menu` 的 `DropdownMenuPortal`）
 *   **不在根具名清单里，当前根本不在检查面上**；即便将来进了清单，它的导出名恰等于
 *   reka 真名，按 `name || __name` 也天然合规，不会被这条守卫逼着动原版件。
 *   若将来出现**改了名**的原版直通再导出并进了具名清单，本测试会红：那是一个真实的
 *   解析缺陷（按新名打桩不命中），而原版层不许 `Object.assign` 补 `name`，
 *   届时该走裁决/开票，不是在这里放宽判据。
 *
 * ## 为什么不持有清单
 *
 * 别名由「与某个 reka 导出共享同一个 `setup` 函数引用」这一结构事实从实际导出集合里
 * 现算出来（浅拷贝必然原样带走 `setup`），不需要有人来同步名单。
 */

type ComponentLike = Record<string, unknown>

/** 对象式组件（`Object.assign({}, X)` 只搬得动这一种；reka 若改成函数式组件，拷贝会是空壳）。 */
function isObjectComponent(value: unknown): value is ComponentLike {
  return (
    typeof value === 'object' &&
    value !== null &&
    typeof (value as ComponentLike).setup === 'function'
  )
}

/** reka 侧：按 `setup` 引用索引。reka 内部把同一个组件挂在多个导出名下（如 Combobox*／Autocomplete*），故值是名字数组。 */
const rekaBySetup = new Map<unknown, { readonly names: string[]; readonly source: ComponentLike }>()
for (const [rekaName, value] of Object.entries(reka as Record<string, unknown>)) {
  if (!isObjectComponent(value)) continue
  const known = rekaBySetup.get(value.setup)
  if (known) known.names.push(rekaName)
  else rekaBySetup.set(value.setup, { names: [rekaName], source: value })
}

interface RekaAlias {
  readonly exportName: string
  readonly component: ComponentLike
  /** 该 reka 组件在 reka 自己的导出面上叫什么。 */
  readonly rekaNames: readonly string[]
  readonly source: ComponentLike
}

/** 从一个导出命名空间里现算出所有 reka 派生的再导出。 */
function collectRekaAliases(namespace: Record<string, unknown>): RekaAlias[] {
  return Object.entries(namespace).flatMap(([exportName, value]) => {
    if (!isObjectComponent(value)) return []
    const known = rekaBySetup.get(value.setup)
    return known
      ? [{ exportName, component: value, rekaNames: known.names, source: known.source }]
      : []
  })
}

/** 违反项的中文说明；空数组表示这个再导出合规。 */
function violationsOf(alias: RekaAlias): string[] {
  const reasons: string[] = []
  // 与 `@vue/runtime-core` 的 `getComponentName` 同口径：`name || __name`（不是 `??`）。
  const resolved = alias.component.name || alias.component.__name
  if (resolved !== alias.exportName) {
    reasons.push(
      `Vue 会把它解析成 ${String(resolved)}，而不是导出名 ${alias.exportName}：打桩/组件解析会静默落到 reka 真名上`,
    )
  }
  if (alias.component === alias.source && !alias.rekaNames.includes(alias.exportName)) {
    reasons.push(
      `它与 reka ${alias.rekaNames.join('／')} 是同一个对象：原地改名会波及共用该组件的其它别名`,
    )
  }
  return reasons
}

// --- 探测器自检：不依赖 barrel 内容，防止 barrel 与守卫同时被改坏后守卫空转变绿。
describe('reka 别名探测器自身的鉴别力', () => {
  const probes: ReadonlyArray<readonly [string, Record<string, unknown>, boolean]> = [
    [
      '浅拷贝且 name 等于导出名',
      { NvProbeSound: Object.assign({}, DialogRoot, { name: 'NvProbeSound' }) },
      true,
    ],
    ['浅拷贝但漏写 name', { NvProbeNoName: Object.assign({}, DialogRoot) }, false],
    ['直接再导出 reka 对象却换了名字', { NvProbeInPlace: DialogRoot }, false],
  ]

  it.each(probes)('%s：能被认出是 reka 再导出', (_case, namespace) => {
    expect(collectRekaAliases(namespace)).toHaveLength(1)
  })

  it.each(probes)('%s：合规判定符合预期', (_case, namespace, compliant) => {
    const [alias] = collectRekaAliases(namespace)
    expect(violationsOf(alias).length === 0).toBe(compliant)
  })

  // 上面的 `NvProbeInPlace` 同时触犯两条 reason，「原地改名」那条被「解析名不符」掩盖，
  // 单独删掉它整组仍绿（复审实测）。而真实导出面上造不出只触犯第二条的输入：那要求
  // 导出名既等于组件解析名、又不在 reka 自己的导出名里，只有原地改过 reka 共享对象的
  // `name` 才成立，本文件不去改 reka 模块。故用合成 source 直接喂 `violationsOf`。
  it('只触犯「与 reka 源同一个对象」时也会被判违反', () => {
    const renamedInPlace: ComponentLike = Object.assign({ setup() {} }, { name: 'NvProbeRenamed' })
    const violations = violationsOf({
      exportName: 'NvProbeRenamed',
      component: renamedInPlace,
      rekaNames: ['ProbeSource'],
      source: renamedInPlace,
    })
    expect(violations).toHaveLength(1)
    expect(violations[0]).toContain('是同一个对象')
  })
})

// --- 真正的检查面：包公开导出集合。
const aliases = collectRekaAliases(barrel as Record<string, unknown>)

describe('reka 别名再导出的组件身份契约', () => {
  it('包公开面上确实存在 reka 再导出', () => {
    // 断言非空，避免探测口径失效后整组用例变成 0 条而“全绿”。
    expect(aliases.length).toBeGreaterThan(0)
  })

  it.each(aliases.map((alias) => [alias.exportName, alias] as const))(
    '%s 以自身导出名解析',
    (_exportName, alias) => {
      expect(violationsOf(alias)).toEqual([])
    },
  )
})
