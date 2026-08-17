import { existsSync, readdirSync, readFileSync } from 'node:fs'
import { dirname, join, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'
import { parse } from 'vue/compiler-sfc'

/**
 * `confirm-destroy.md` 规则 3 与规则 5 的**写法门禁**（#1613 子项 a）。
 *
 * - **规则 3**：破坏性确认按钮不得用 `NvAlertDialogAction`。它包的是 reka `AlertDialogAction`
 *   → 渲染成 `DialogClose`，`@click` 里 `onOpenChange(false)` **无条件执行、不看
 *   `defaultPrevented`**。于是「失败保留原因原地重试」与「pending 期间禁点」只在控制器层成立，
 *   真 UI 走不到（#1607）。
 * - **规则 5**：`NvAlertDialog` 不得出现在带 `v-for` 的元素子树内——N 行就是 N 个弹层实例（#1608）。
 *
 * ## 这道门禁保证什么、不保证什么
 *
 * 它挡的是**写法**：「异步确认有没有用 `NvAlertDialogAction`」「弹层有没有写在 `v-for` 里」，
 * 扫源即可判定。它**不保证关框时机**——「点确认后框该不该关」是**行为**，只有**挂真弹层**
 * （不 stub `NvAlertDialog*`）的用例能钉住。PR #1615 实测过这条边界：把一处确认按钮改回
 * `NvAlertDialogAction`、并补回对应 stub 之后，整套页面测试**仍然全绿**。
 * 所以每个清扫落点都另有一条 `*.realDialog.test.ts`（见文件末尾的落点表），
 * 白名单清空只证明「没人再写这个组件名」，不证明「失败后框还在」。
 *
 * ## 判定用 AST，不用正则
 *
 * 用 `vue/compiler-sfc` 解析模板 AST 后遍历，原因有两条实测教训：
 * 1. **只匹配标识符会把 `import` 行和注释算进去**（#1594 的契约测试栽过一次）。AST 只看模板里的
 *    元素标签，脚本块的 `import { NvAlertDialogAction }` 与 `<!-- 不用 NvAlertDialogAction -->`
 *    这类注释天然不命中——本文件正文与被扫页面里就有大量这样的散文提及。
 * 2. 规则 5 的 `v-for` 与 `<NvAlertDialog` 常隔十几行、缩进也不可靠，文本判定既漏又误伤。
 *
 * 鉴别力不靠断言"我觉得它能拦"——文件末尾的**变异对照**用一组正/负样本把判定谓词本身钉住。
 *
 * ## 已知不覆盖
 *
 * - `<component :is="...">` 动态渲染：只认字面标签。
 * - 经业务组件间接承载的弹层（如 `<MasterDataLifecycleDialog>` 塞进 `v-for`）：那一层由
 *   `pages/master-data/lifecycleDialogSingleInstance.contract.test.ts` 与其 `runtime` 版本管。
 */

/** 本文件在 business-console 里，但扫描面是**整个前端工作区**——新用法在任何 app / package 里都进不来。 */
const frontendRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../../..')

/**
 * 规则 3 的白名单：**当前为空**。
 *
 * 存量 11 处（#1613 票面清单）已全部落地：其中 `master-data/scheduling.vue` 的 3 处由 #1608 /
 * PR #1615 销掉，其余 10 处由本 PR 改成普通 `NvButton`。因此不存在「先写进来再逐条删」的账。
 *
 * 什么情况下可以往这里加：确认动作是**纯同步本地状态**（不等任何接口结果），点击即关框没有
 * 可失败的写回。加的时候必须逐条写明**为何同步安全**，不许空挂。异步确认一律不许进。
 */
const SYNC_SAFE_ACTIONS: { file: string; why: string }[] = []

/**
 * 规则 5 的白名单：**当前为空**。
 *
 * 存量由 #1608 跑一次全仓 AST 扫描回填（见 issue #1613 评论）：`main` 上只有
 * `master-data/scheduling.vue` 两处，且已由 PR #1615 收敛。零存量落地，新增用法一律拦。
 */
const LOOPED_DIALOGS: { file: string; why: string }[] = []

const NODE_TYPE_ELEMENT = 1

function walkVueFiles(dir: string, out: string[] = []): string[] {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (entry.name === 'node_modules' || entry.name === 'dist') continue
    const full = join(dir, entry.name)
    if (entry.isDirectory()) walkVueFiles(full, out)
    else if (entry.name.endsWith('.vue')) out.push(full)
  }
  return out
}

/** `frontend/apps/<包名>/src` 与 `frontend/packages/<包名>/src` 下的全部 `.vue`。 */
function workspaceVueFiles() {
  const files: string[] = []
  for (const group of ['apps', 'packages']) {
    const groupDir = join(frontendRoot, group)
    if (!existsSync(groupDir)) continue
    for (const entry of readdirSync(groupDir, { withFileTypes: true })) {
      if (!entry.isDirectory()) continue
      const srcDir = join(groupDir, entry.name, 'src')
      if (existsSync(srcDir)) walkVueFiles(srcDir, files)
    }
  }
  return files.sort()
}

/**
 * 模板 AST 里本门禁真正用到的那几个字段。
 *
 * 不从 `vue/compiler-sfc` 取 `ElementNode` / `RootNode` 等类型——那个入口只 re-export
 * 了值，没有 re-export 这些类型（写成 `import type` 会 TS2305）。这里按需声明结构类型，
 * 只落到实际读的字段上，免得为了拿类型去依赖 `@vue/compiler-core` 的内部路径。
 */
interface TemplateNode {
  type: number
  tag?: string
  props?: { type: number; name?: string }[]
  children?: TemplateNode[]
  loc: { start: { line: number } }
}

function isElement(node: TemplateNode) {
  return node.type === NODE_TYPE_ELEMENT
}

/** `type === 7` 是 `DirectiveNode`；`name === 'for'` 即 `v-for`。 */
function hasVFor(node: TemplateNode) {
  return (node.props ?? []).some((prop) => prop.type === 7 && prop.name === 'for')
}

export interface ConfirmDestroyFindings {
  /** 模板里出现 `<NvAlertDialogAction>` 的行号（规则 3）。 */
  actionTags: number[]
  /** 出现在某个带 `v-for` 元素子树内的 `<NvAlertDialog>` 行号（规则 5）。 */
  loopedDialogs: number[]
}

/**
 * 解析一份 SFC 源码，返回两条规则的命中行号。
 *
 * 导出是为了让文件末尾的变异对照能直接喂样本——被验证的必须是**门禁真正用的那个谓词**，
 * 而不是另写一份"等价"实现（否则变异对照证明的是它自己）。
 */
export function scanConfirmDestroy(source: string, filename: string): ConfirmDestroyFindings {
  const findings: ConfirmDestroyFindings = { actionTags: [], loopedDialogs: [] }
  const { descriptor, errors } = parse(source, { filename })
  if (errors.length) {
    throw new Error(`${filename} 模板解析失败，门禁无法判定：${errors[0]!.message}`)
  }
  const ast = descriptor.template?.ast as TemplateNode | undefined
  if (!ast) return findings

  const visit = (node: TemplateNode, insideLoop: boolean) => {
    for (const child of node.children ?? []) {
      if (!isElement(child)) continue
      if (child.tag === 'NvAlertDialogAction') findings.actionTags.push(child.loc.start.line)
      if (insideLoop && child.tag === 'NvAlertDialog')
        findings.loopedDialogs.push(child.loc.start.line)
      visit(child, insideLoop || hasVFor(child))
    }
  }
  visit(ast, false)
  return findings
}

interface ScannedFile {
  rel: string
  findings: ConfirmDestroyFindings
}

const scanned: ScannedFile[] = workspaceVueFiles().map((file) => ({
  rel: relative(frontendRoot, file),
  findings: scanConfirmDestroy(readFileSync(file, 'utf8'), file),
}))

function hits(pick: (f: ConfirmDestroyFindings) => number[], allowed: { file: string }[]) {
  const allowedFiles = new Set(allowed.map((entry) => entry.file))
  return scanned
    .filter(({ rel }) => !allowedFiles.has(rel))
    .flatMap(({ rel, findings }) => pick(findings).map((line) => `${rel}:${line}`))
}

describe('confirm-destroy 写法门禁（规则 3 / 规则 5）', () => {
  it('扫描面没有静默塌缩——路径改了要红，而不是悄悄扫了 0 个文件', () => {
    // 2026-08-17 实测 718 个 .vue（与 #1608 那次全仓扫描同一口径）。留出下限而非等值，
    // 免得新增页面都要来改这个数字；但塌到几十个说明 glob 或目录约定变了，必须有人看一眼。
    expect(scanned.length).toBeGreaterThan(600)
    // 两个具名锚点：一个在本 app、一个在组件库，任一侧扫不到都说明扫描面缺了一半。
    const files = scanned.map(({ rel }) => rel)
    expect(files).toContain('apps/business-console/src/pages/quality/ncrs.vue')
    expect(files).toContain('packages/ui/src/components/pc/alert-dialog/NvAlertDialogAction.vue')
  })

  it('规则 3：破坏性确认按钮不用 NvAlertDialogAction（白名单外零命中）', () => {
    const offenders = hits((f) => f.actionTags, SYNC_SAFE_ACTIONS)
    expect(
      offenders,
      offenders.length
        ? `以下确认按钮用了点击即无条件关框的 NvAlertDialogAction（confirm-destroy 规则 3）：\n` +
            `${offenders.join('\n')}\n` +
            `改成普通 NvButton，由 handler 成功才置 open = false；` +
            `确认动作若确属纯同步本地状态，登记进 SYNC_SAFE_ACTIONS 并写明为何同步安全。`
        : '',
    ).toEqual([])
  })

  it('规则 5：NvAlertDialog 不写在 v-for 子树里（白名单外零命中）', () => {
    const offenders = hits((f) => f.loopedDialogs, LOOPED_DIALOGS)
    expect(
      offenders,
      offenders.length
        ? `以下 NvAlertDialog 落在 v-for 子树内，N 行就是 N 个实例（confirm-destroy 规则 5）：\n` +
            `${offenders.join('\n')}\n` +
            `把弹层提到循环外声明为页面层单实例，用 target ref 指向当前行。`
        : '',
    ).toEqual([])
  })

  it('白名单里的每一条都写了理由，不许空挂', () => {
    const empty = [...SYNC_SAFE_ACTIONS, ...LOOPED_DIALOGS].filter(
      (entry) => !entry.why.trim() || entry.why.trim().length < 10,
    )
    expect(empty).toEqual([])
  })

  it('白名单不许留已经清干净的条目——存量销完就必须删掉，否则门禁会替它兜住回归', () => {
    const stale = [
      ...SYNC_SAFE_ACTIONS.filter(
        (entry) => !scanned.find(({ rel }) => rel === entry.file)?.findings.actionTags.length,
      ),
      ...LOOPED_DIALOGS.filter(
        (entry) => !scanned.find(({ rel }) => rel === entry.file)?.findings.loopedDialogs.length,
      ),
    ].map((entry) => entry.file)
    expect(stale).toEqual([])
  })
})

/**
 * 变异对照：证明上面那个谓词**有鉴别力**——能拦住新增用法，且不误伤合法写法。
 *
 * 没有这一组，「白名单清空 + 全绿」只说明当前树里没有命中，说不清是判对了还是判空了
 * （#1510 连栽四轮、#1508 同一红门重生三次，都是这个缺口）。每条样本都标了它防的是哪种误判。
 */
describe('门禁判定的变异对照', () => {
  const fixture = (template: string, script = '') =>
    scanConfirmDestroy(`${script}<template>\n${template}\n</template>\n`, 'fixture.vue')

  describe('规则 3 应当命中', () => {
    it('模板里直接用 NvAlertDialogAction', () => {
      expect(
        fixture('<NvAlertDialogAction @click="go">确认</NvAlertDialogAction>').actionTags,
      ).toHaveLength(1)
    })

    it('嵌在 Footer / Content 深处也算', () => {
      const template = [
        '<NvAlertDialog v-model:open="open">',
        '  <NvAlertDialogContent>',
        '    <NvAlertDialogFooter>',
        '      <NvAlertDialogAction :disabled="pending" @click="go">确认</NvAlertDialogAction>',
        '    </NvAlertDialogFooter>',
        '  </NvAlertDialogContent>',
        '</NvAlertDialog>',
      ].join('\n')
      expect(fixture(template).actionTags).toEqual([5])
    })

    it('自闭合写法也算', () => {
      expect(fixture('<NvAlertDialogAction @click="go" />').actionTags).toHaveLength(1)
    })
  })

  describe('规则 3 不应误伤', () => {
    it('脚本块里的 import 不算——只匹配标识符的正则会在这里假红（#1594）', () => {
      const script = `<script setup lang="ts">\nimport { NvAlertDialogAction } from '@nerv-iip/ui'\n</script>\n`
      expect(fixture('<NvButton @click="go">确认</NvButton>', script).actionTags).toEqual([])
    })

    it('模板注释里提到组件名不算——本仓库每个清扫落点都写了这样一条注释', () => {
      const template = [
        '<!-- 普通 NvButton，不用 NvAlertDialogAction：后者点击即无条件关框。 -->',
        '<NvButton type="button" @click="go">确认</NvButton>',
      ].join('\n')
      expect(fixture(template).actionTags).toEqual([])
    })

    it('文本内容里出现组件名不算', () => {
      expect(fixture('<p>不要用 NvAlertDialogAction</p>').actionTags).toEqual([])
    })

    it('NvAlertDialogCancel 不算——取消本来就该无条件关框（规则 3 末句）', () => {
      expect(fixture('<NvAlertDialogCancel>取消</NvAlertDialogCancel>').actionTags).toEqual([])
    })

    it('名字更长的近邻组件不算——判定是标签全等，不是前缀匹配', () => {
      expect(fixture('<NvAlertDialogActionGroup />').actionTags).toEqual([])
    })
  })

  describe('规则 5 应当命中', () => {
    it('弹层直接写在 v-for 元素里', () => {
      const template = [
        '<li v-for="row in rows" :key="row.id">',
        '  <NvAlertDialog v-model:open="row.open" />',
        '</li>',
      ].join('\n')
      expect(fixture(template).loopedDialogs).toEqual([3])
    })

    it('隔了好几层、缩进也不规律仍然命中——文本判定在这里会漏', () => {
      const template = [
        '<div v-for="row in rows" :key="row.id">',
        '<section>',
        '        <div><span>',
        '<NvAlertDialog v-model:open="row.open" />',
        '</span></div>',
        '</section>',
        '</div>',
      ].join('\n')
      expect(fixture(template).loopedDialogs).toEqual([5])
    })

    it('`<template v-for>` 也算', () => {
      const template = [
        '<template v-for="row in rows" :key="row.id">',
        '  <NvAlertDialog v-model:open="row.open" />',
        '</template>',
      ].join('\n')
      expect(fixture(template).loopedDialogs).toEqual([3])
    })
  })

  describe('规则 5 不应误伤', () => {
    it('循环已经闭合、弹层是它的兄弟节点——这正是页面层单实例的目标形态', () => {
      const template = [
        '<ul>',
        '  <li v-for="row in rows" :key="row.id">{{ row.name }}</li>',
        '</ul>',
        '<NvAlertDialog v-model:open="confirmOpen" />',
      ].join('\n')
      expect(fixture(template).loopedDialogs).toEqual([])
    })

    it('v-for 里的触发按钮不算——只开框、不承载弹层，规则 2 的目标写法', () => {
      const template = [
        '<li v-for="row in rows" :key="row.id">',
        '  <NvButton @click="openConfirm(row)">停用</NvButton>',
        '</li>',
        '<NvAlertDialog v-model:open="confirmOpen" />',
      ].join('\n')
      expect(fixture(template).loopedDialogs).toEqual([])
    })

    it('v-for 里的其它弹层类型不算——本条只管 AlertDialog', () => {
      const template = [
        '<li v-for="row in rows" :key="row.id">',
        '  <NvDialog v-model:open="row.open" />',
        '</li>',
      ].join('\n')
      expect(fixture(template).loopedDialogs).toEqual([])
    })
  })
})

/**
 * #1613 的落点表：白名单已清空，每处清扫都另有一条**挂真弹层**的行为断言。
 * 这一组把「有没有那条真弹层用例」也变成门禁——删掉用例会红，而不是静默退回假绿。
 */
describe('每个清扫落点都有挂真弹层的行为断言', () => {
  const pagesRoot = join(frontendRoot, 'apps/business-console/src')
  const REAL_DIALOG_TESTS = [
    // #1607 / PR #1609 的原始两处
    'components/masterData/MasterDataLifecycleDialog.realDialog.test.ts',
    // #1608 / PR #1615（清单第 4 条）
    'pages/master-data/scheduling.deleteConfirm.realDialog.test.ts',
    // #1613 子项 b：master-data 域
    'pages/master-data/productCategoryArchive.realDialog.test.ts',
    // #1613 子项 c：engineering 域
    'pages/engineering/productionVersionArchive.realDialog.test.ts',
    // #1613 子项 d：quality 域
    'pages/quality/ncrClose.realDialog.test.ts',
    // #1613 子项 e：equipment 域
    'pages/equipment/batchAckConfirm.realDialog.test.ts',
    // #1613 子项 f：排产页
    'pages/schedulingRevoke.realDialog.test.ts',
  ]

  it.each(REAL_DIALOG_TESTS)('%s 存在', (rel) => {
    expect(existsSync(join(pagesRoot, rel))).toBe(true)
  })

  it('这些用例都没有 stub 掉 NvAlertDialog——stub 了就测不到关框时机', () => {
    const offenders = REAL_DIALOG_TESTS.filter((rel) => {
      const source = readFileSync(join(pagesRoot, rel), 'utf8')
      // 桩的形式是 `NvAlertDialog: {` / `NvAlertDialogContent: {` 这类对象键。
      return /\bNvAlertDialog\w*\s*:\s*\{/.test(source)
    })
    expect(offenders).toEqual([])
  })
})
