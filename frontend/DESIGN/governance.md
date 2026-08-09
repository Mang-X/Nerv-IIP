# 治理规范——Nerv-IIP Console 设计系统

## 所有权

`@nerv-iip/ui` package（`frontend/packages/ui`）是所有 UI 基础原语（primitive）**以及**设计 token
（`packages/ui/src/styles/theme.css`）的单一事实来源。`frontend/apps/*` 中的应用代码
绝不拥有 primitive 组件逻辑，也绝不定义 token 值。

## 原版零改动（硬性规则）

基础 shadcn-vue primitive 从官方 `reka-nova` 注册表（registry）**逐字节**拉取，必须保持
字节零改动，以便随时重新拉取或覆盖。**不得通过编辑 primitive 实现定制**。任何定制
都必须是名称不同的*复制重建*组件（FE-2“区块组件库”），构建在未改动的 primitive 和
token 之上。

### 全新拉取基线（FE-1 #276）

设计系统 v2 基础集已从官方 registry 重新拉取，并按仓库约定规范化
（相对 `../../../lib/utils` / `../<comp>` 导入；
`@lucide/vue` → `@lucide/vue`）。

- **注册表：**`https://shadcn-vue.com/r/styles/reka-nova/<component>.json`
- **等效 CLI：**`pnpm dlx shadcn-vue@2.7.3 add <component>`（style `reka-nova`、
  `components.json`；这里使用直接 JSON 拉取，因为 CLI 会修改 `main.css`，并执行在
  CI / 离线环境下不可靠的依赖安装）。
- **固定版本：**`shadcn-vue@2.7.3`、`reka-ui@^2.9.7`、`tailwindcss@^4.3.0`、
  `@lucide/vue@1.0.0`。Table 的数据表 helper 另加 `@tanstack/vue-table@^8.21.3`。
- **重新拉取（纯原版）**：button、card、table、input、select、dropdown-menu、dialog、
  另包括 sheet、tabs、breadcrumb、sidebar、pagination、tooltip、popover、skeleton、empty。
- **有意不重新拉取：**`badge` 带有项目定制（`success` / `warning` variant，包括
  `BusinessStatusBadge` 在内的多处代码正在使用）。它**等待在 FE-2 中由复制重建的
  StatusBadge 取代**；success/warning 已作为 `--success` / `--warning` token 存在。
  其他扩展组件（avatar、chart、file-upload、date-picker、field 等）同样属于定制，
  不在原始重新拉取集合中。

## NvUI 命名与场景命名空间（ADR 0020）

组件库品牌名 **NvUI**，品牌前缀 `Nv`。完整规则、判定流程与全量旧名→新名映射表冻结在
[ADR 0020](../../docs/adr/0020-nvui-naming-token-namespaces-and-style-isolation.md)，
执行批次为 MAN-433（库侧）/ MAN-435（分 app codemod）/ MAN-436（守护收口）。要点：

- `Nv` 前缀 = 品牌定制层唯一标识；无前缀 = shadcn 原版底座（或待收口的 deprecated 旧名）。
- PC 层（pc/blocks/layout）取素名：`NvButton`、`NvDataTable`、`NvPageHeader`（素名
  优先权归 PC）。screen/mobile/touch 与 PC 潜在同名者保留场景词根（`NvScreenButton`、
  `NvMobileDialog`、`NvTouchButton`），天然独有名直接 Nv（`NvScanBar`、`NvOeeHero`）。
- 新组件命名必须走 ADR 0020 §1.2 的 R1–R5 判定流程；先定场景归属（表面/视距/输入方式
  决定目录与 token 命名空间），再定名。
- shadcn 原版（`components/ui/`）零改动零重命名——本文件既有红线不变，且由契约
  测试断言“原版目录不出现 `Nv`/`--nv-` 字样”进行机器守护。
- 迁移期（MAN-433 合入后）旧名是 `@deprecated` 别名：**新代码禁止使用旧名**
  （`ButtonPro`、`--sb-*`、`.ds-*`/`.sb-*` 类）。
- CSS 类名前缀与 token 命名空间对齐：PC `nv-*`、screen `nv-scr-*`、mobile `nv-m-*`、
  touch `nv-t-*`；Nv 件 `data-slot` 值以 `nv-` 开头。

## 样式层（CSS 层叠层，ADR 0020）

全部组件样式进入 CSS 层叠层，全局唯一层序（每个 app `main.css` 首条语句声明）：

```css
@layer theme, nv-tokens, base, components, nv-components, utilities, nv-overrides, app;
```

- 库内禁止未分层（unlayered）规则（白名单：`@font-face`/`@keyframes`/`@property`/`@import`/
  `@custom-variant`/`@theme`）；SFC `<style>` 统一包 `@layer nv-components`；历史
  "故意 unlayered"的玻璃拟态/sidebar 选中态收编进 `nv-overrides`（位于 utilities 之后）。
- app 自定义 CSS 一律进 `@layer app`（层序最后，app 主权最高）。
- VitePress 文档站是例外宿主：工具类与覆盖样式需以未分层方式导入，以赢过 VitePress
  自带裸重置；启用 `postcssIsolateStyles()` + demo 统一容器挂 `vp-raw`；`--vp-*` 桥接
  映射保留；**禁用 `revert-layer`**（历史坑）。细则见 ADR 0020 §4.2。

## 选件阶梯（写页面时按序判断）

1. **用现有 `Nv*` 件**（速查表 `DESIGN/index.md`；真实 props 以设计系统文档站
   - 源码为准）。
2. **组合现有件**（区块 / 交互模式级拼装，见 `patterns/`）。
3. **新建品牌组件** —— 满足任一触发条件就新建，不要削足适履：
   - 交互用现有件表达不出来（如需要变通方式（hack）、`:deep()` 或覆盖内部结构）；
   - 为凑合现有件加了 2 个以上"配置型" props/class 补丁；
   - 你在"和组件搏斗"而不是在做页面。

   新建门槛低、规矩高：按下面两个流程之一走完 DoD。app 内长出的业务组件成熟后
   **上提组件库**（反哺，见 `packages/ui/AGENTS.md`）。

## 新增 shadcn-vue 组件（装原版，再决定是否建品牌层）

1. 从 `frontend/` workspace 根目录执行：

   ```bash
   pnpm dlx shadcn-vue@2.7.3 add <component-name>
   ```

   这会把源码安装到 `packages/ui/src/components/ui/<component-name>/`
   （原版逐字节保持，永不改动）。

2. 从 `packages/ui/src/index.ts` 导出所有公开部分。

3. 需要品牌化/定制时**复制重建**到对应层（`pc/`/`blocks/`/…），走下面的新组件 DoD；
   无需定制的原版件（Alert/Empty 类）可直接在 app 使用。

## 新组件 DoD（六件套，缺一不算完成）

无论是复制重建原版、还是从业务场景全新创造，一个新组件 = 以下六件全齐：

1. **源码**落在正确的层（`pc/` `blocks/` `layout/` `touch/` `screen/` 或 ui-mobile），
   命名过 ADR 0020 §1.2 R1–R5；动手前先读所在层的 `product.md`
   （PC: `components/pc/product.md`；screen: `components/screen/product.md`）。
2. **Barrel 导出**（`packages/ui/src/index.ts` 或 ui-mobile `index.ts`）。
3. **契约测试通过**（`nvui-naming` / `ui-primitives` / 各 app `nvui-imports`）。
4. **`DESIGN/component-coverage.md` 矩阵行**（四场景覆盖态，缺口如实标 `—`）。
5. **决策段**：`DESIGN/components/<name>.md` 写使用时机 / 变体选择 / Do-Don't
   （体例向 `patterns/interaction-patterns.md` 的“规则/判定/正例/反例”四段式看齐）。
6. **文档站页**（`apps/design-system/docs/components/<surface>/<name>.md`，实时演示；
   决策性正文用 `@include` 嵌 DESIGN 源，不复制）。

`skills/new-component` 技能（仓库内维护）把这条流程做成了可执行清单。

## 新增设计 token

token 只能位于 `packages/ui/src/styles/theme.css`（PC/共享）或对应场景 token 表
（screen: `components/screen/tokens.css`；mobile/touch 表在首个场景 token 出现时建立）。
绝不得向 app `main.css` 添加 token 值。

1. 先定命名空间（ADR 0020 §3）：shadcn 契约名冻结不动；PC/共享自有语义用 `--nv-*`；
   场景专属用 `--nv-scr-*` / `--nv-m-*` / `--nv-t-*`。跨场景同值必须用 var 引用链
   （`--nv-scr-green: var(--nv-success)`），禁止复制字面量；场景组件只允许引用本场景
   前缀 + 契约层 token。
2. 把 CSS 自定义属性添加到所属 token 表的 `:root {}`。
3. 把暗色覆盖添加到 `.dark {}`（场景表如无亮色态则不适用）。
4. 在 `@theme inline {}` 中添加 Tailwind 映射（桥接名保持 utility 契约，右值指向
   `--nv-*` 新名）。
5. 更新 `DESIGN/tokens.md` 和 `DESIGN/foundation.md`。
6. 如果 token 属于品牌约束（`--primary`、`--nv-brand`、`--nv-success`/
   `--nv-warning`、高程），就在 `packages/ui/src/design-system.contract.test.ts`
   中添加断言。

## 字体

两套 UI 字体均为**自行托管**（由 Vite 打包；绝不使用 `fonts.googleapis.com` 或运行时
CDN），并在 `packages/ui/src/styles/theme.css` 顶部统一导入一次：

| 角色            | 字体族           | 包                           | 许可证                 |
| --------------- | ---------------- | ---------------------------- | ---------------------- |
| 拉丁字母 / 数字 | `Inter Variable` | `@fontsource-variable/inter` | OFL                    |
| 简体中文        | `MiSans`         | `misans`（Xiaomi）           | Apache-2.0，可免费商用 |

`--font-sans` 为 `'Inter Variable', 'MiSans', …`，因此拉丁字母由 Inter 渲染，中文回退到
MiSans。**不得**添加 `misans-webfont`（标记为仅限学习交流 / 非商用）。

### 重新生成 `styles/misans.css`

`misans` package 随附的 MiSans 使用非标准光学字重（Regular=330、Medium=380、
Semibold=520、Bold=630）。`styles/misans.css` 是**生成文件**，用于把这些值重新映射为
标准 CSS 字重，使 Tailwind `font-normal/medium/semibold/bold` 正确对应；按
`unicode-range` 切分的 woff2 分块保留在 `node_modules` 中（使用相对引用，不提交）。
升级 `misans` 后，从 `frontend/` 重新生成：

```bash
python - <<'PY'
import os
src="packages/ui/node_modules/misans/lib/Normal"
weights=[("MiSans-Regular",330,400),("MiSans-Medium",380,500),("MiSans-Semibold",520,600),("MiSans-Bold",630,700)]
prefix="../../node_modules/misans/lib/Normal/"
out=["/* generated from `misans` (Apache-2.0); weights remapped to standard CSS values */",""]
for name,old,new in weights:
    css=open(os.path.join(src,name+".min.css"),encoding="utf-8").read()
    css=css.replace(f"font-weight:{old}",f"font-weight:{new}").replace("url('","url('"+prefix)
    out += [f"/* {name} -> {new} */", css, ""]
open("packages/ui/src/styles/misans.css","w",encoding="utf-8",newline="\n").write("\n".join(out))
PY
```

## 组件契约测试

`packages/ui/src/design-system.contract.test.ts` 读取
`packages/ui/src/styles/theme.css`，并守护设计系统 v2 的关键 token：

- `--primary: oklch(0.205 0 0)`——近黑主色（不得出现已停用的蓝色）
- `--brand: oklch(0.55 0.18 255)` + `--color-brand` + `--chart-1: var(--brand)`——动态强调色
- `--success` / `--warning`（+ `--color-success` / `--color-warning`）——状态语义
- `--background: oklch(0.985 0 0)` ≠ `--card: oklch(1 0 0)` + `--shadow-{sm,md,lg}`——高程
- `.dark { … }` 覆盖，其中含 `--primary: oklch(0.922 0 0)` 和 `color-scheme: dark`

使用 `pnpm -C frontend --filter @nerv-iip/ui test` 运行。任何 token 变更合并前，
此测试必须通过。如果需要更新受守护的值，必须有意更新测试，并在此记录决策。

ADR 0020 落地批（MAN-433）将把守护面扩展到：八层层序声明、库内零未分层（unlayered）规则（白名单
外）、`--nv-scr-*` 全表与 `--sb-*` 别名期形态、关键 var 引用链、原版目录纯净
（无 `Nv`/`--nv-` 字样）、Nv 件 `data-slot` 命名空间、跨场景 token 引用污染、旧名零
新增。清单见 ADR 0020 §4.4。

## 迁移待办

两个旧组件仍使用 `--legacy-color-*` token 和 `<style scoped>`：

| 文件                                                          | 处理方式                                      |
| ------------------------------------------------------------- | --------------------------------------------- |
| `apps/console/src/components/console/InstanceTable.vue`       | 使用 `Table` + shadcn-vue primitive 重写      |
| `apps/console/src/components/console/InstanceDetailPanel.vue` | 使用 `Card` + shadcn-vue primitive 重写       |
| `apps/console/src/pages/index.vue`                            | 删除 `<style scoped>`，转换为 Tailwind 工具类 |

迁移完成后，从 `main.css` 删除所有 `--legacy-color-*` 定义。

## 版本管理

此系统仅供内部使用（不采用 semver）。对 `@nerv-iip/ui` 导出的破坏性变更，必须在同一
提交中更新 `apps/console` 内所有消费该导出的导入位点。

## 审核清单（适用于 UI PR）

- [ ] 可见页面文案面向业务用户，而不是开发者或审核者
- [ ] 不出现可见的 demo / 测试 / 脚手架用语（`样例`、`内置`、`用于验证`、`联动测试`、`demo`、`mock`、`seed`）
- [ ] 不出现可见的平台元数据或 gateway / API 用语（`组织`、`环境`、`上下文`、`业务网关契约`、`operationId`）
- [ ] 不使用原始调色板 CSS 类（`bg-blue-*`、`text-gray-*` 等）
- [ ] `.vue` 文件中不使用原始十六进制值
- [ ] 新组件中不使用 `--legacy-color-*`
- [ ] 状态指示器使用具名 `Badge` 变体
- [ ] 破坏性操作使用 `AlertDialog`
- [ ] 新 shadcn 组件从 `@nerv-iip/ui` 导出
- [ ] 新组件名过 ADR 0020 §1.2 判定流程（Nv 前缀 + 场景词根判定），无旧名
      （`*Pro`、裸场景名、`--sb-*`、`.ds-*`/`.sb-*`）新增
- [ ] 新增/改动的手写样式在正确的 CSS 层叠层内（SFC → `nv-components`；赢
      工具类的库级装饰 → `nv-overrides`；app 自定义 → `app`），无白名单外未分层规则
- [ ] 引入新交互模式或组件时已更新 DESIGN/ 文档
- [ ] `design-system.contract.test.ts` 通过
