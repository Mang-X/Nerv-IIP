# ADR 0020: NvUI 组件命名、token 场景命名空间与样式隔离

- Status: Accepted
- Date: 2026-07-07
- 关联: Linear MAN-432 / GitHub #786（前端第二波 W0）
- 上游已定决策（本 ADR 只细化落地，不重议）：品牌前缀 `Nv`、组件库品牌名 **NvUI**、
  组件迁移在业务批之前完成、token 允许跨场景取值相同但名称必须按场景命名空间隔离、
  组件样式全部进入 CSS cascade layer。
- 执行批次：本 ADR **不改任何代码**。库侧改名与 layer 落地 = MAN-433；分 app codemod =
  MAN-435；守护与收口 = MAN-436。附录 A/B/C 是这些批次的执行输入。

## Context

以下现状为 2026-07-07 对仓库的实地清点结果（非估算）。

**命名三风格并存。** `@nerv-iip/ui` 内定制组件同时存在三种命名法：

1. Pro 后缀：`pro/` 35 个目录、116 个组件导出（`ButtonPro`、`DataTablePro`、
   `AlertDialogProAction`…），其中还混有无后缀件（`Loader`、`StatusDot`、`NotifierHost`）；
2. 场景前缀/后缀：`screen/` 34 件中 16 件带 `Screen` 前缀、`touch/` 5 件中 2 件带
   `Touch` 前缀、`@nerv-iip/ui-mobile` 47 个导出中 17 件带 `Mobile` 词根（含
   `AppShellMobile` 后缀式）；
3. 裸名：`blocks/`（`PageHeader`、`Toolbar`、`DataTable`…）、screen 18 件
   （`OeeHero`、`StatusCard`…）、mobile 30 件（`Badge`、`Cell`、`Picker`…）。

**真实撞名已经发生。** `@nerv-iip/ui`（shadcn 原版）与 `@nerv-iip/ui-mobile` 同时导出
`Badge`、`Empty`、`DropdownMenu`/`DropdownMenuItem`；`blocks/DataTable`（旧代）与
`pro/DataTablePro`（新代）两代并存；`blocks/StatusBadge` 与 `pro/StatusBadgePro` 并存。
codex/协作代理反复把 shadcn 原版误当作品牌组件库直接使用——原版与定制层在名字上无法
区分是直接诱因。

**token 场景间靠自觉隔离。** 大屏 `--sb-*` 表（30 个，`screen/tokens.css`）独立于共享
`theme.css`，但 PC/mobile/touch 共用一套裸名 token；动效值跨场景复制字面量而非引用
（`--sb-ease` 与 `--ease-out-quart` 同值 `cubic-bezier(0.25,1,0.5,1)`，两处硬编码）；
`.sb-scroll` 在 `screen/tokens.css` 与 `apps/screen/src/assets/main.css` 有两份视觉参数
不一致的定义（hover 显隐 vs 常显）。类名前缀同样混乱：PC/touch 用 `.ds-*`
（`.ds-overlay-content`、`.ds-tbtn`），screen 用 `.sb-*`（`.sb-scroll`、`.sb-tbl`）。

**样式层现状是"两轨制"。** 四个产品 app 的 `main.css` 用标准 `@import 'tailwindcss'`
（theme/base/components/utilities 四层）；而库内存在**故意 unlayered** 的规则以赢过
utilities（`theme.css` 中玻璃拟态 `[data-slot=…]` 覆盖、`.ds-overlay-content` 动效、
sidebar premium 选中态，注释明写 "intentionally UNLAYERED"）。app 自定义规则
（business-console 的 sidebar active、apps/screen 的 body/滚动条）也全部 unlayered。
优先级依赖"谁不分层谁赢"，不可审计。

**VitePress 文档站的宿主坑已有一套手工解法。** `apps/design-system` 站的
`.vitepress/theme/style.css`：因 VitePress 自带 unlayered 重置（`h1..h6`、`button`、
`.vp-doc table`…）会赢过 Tailwind 的 layered utilities，站内把 utilities **unlayered 导入**
（首行 `@layer theme, base, components;` + `@import 'tailwindcss/utilities.css'` 不套层）；
`--vp-*` 全表映射到我们的 token；`.vp-doc` 对 `.sb-tbl`/`.sb-at-tbl` 的表格样式污染靠逐条
手写反制。历史上的 `revert-layer` hack 已被移除（曾致嵌入组件样式回退异常）。VitePress
1.6.3 官方提供 `postcssIsolateStyles()`（`.vp-raw` 隔离），本站**尚未启用**（已核实
1.6.3 dist 导出该函数、默认主题样式未带 `.vp-raw` 保护）。

**规模数据（迁移面）。** `from '@nerv-iip/ui'` 167 个文件、`from '@nerv-iip/ui-mobile'`
17 个文件，除 `@nerv-iip/ui/file-preview` 子入口外零深 import。页面数：business-console
93、business-pda 15、console 10、screen 10。全部前端包 `private: true`，无 npm 发布。
`--sb-*` 引用约 434 处（ui 包 screen 层）+ 759 处（apps/screen）+ design-system 站若干。
`packages/ui` 为单 barrel 入口，`screen/index.ts` 顶部副作用 `import './tokens.css'`——
即 PC app 只要 import `@nerv-iip/ui` 也会载入大屏 token 表。

## Decision 1: 命名规则

### 1.1 总则

1. 品牌前缀 `Nv`，库品牌名 **NvUI**。`Nv` 前缀是"Nerv-IIP 品牌定制层"的唯一标识：
   看到 `Nv*` = 可直接使用的品牌件；无 `Nv` 前缀的组件名 = shadcn 原版底座或已废弃旧名。
2. **PC 层取素名**：`pro/`（去 `Pro` 后缀）、`blocks/` 活跃件、`layout/` 全部升级为
   `Nv` + 素名：`NvButton`、`NvDataTable`、`NvPageHeader`、`NvPage`。PC 是素名的
   默认拥有者（素名优先权归 PC）。
3. **场景层与 PC 潜在同名者保留场景词根**：`NvScreenButton`、`NvMobileDialog`、
   `NvTouchButton`；**天然独有名直接 Nv**：`NvScanBar`、`NvOeeHero`、`NvTaktGantt`。
   判定按 1.2 流程，逐件结果冻结在附录 A。
4. **shadcn 原版（`packages/ui/src/components/ui/`，34 个目录）零改动零重命名**——
   governance 既有红线不变。原版导出名（`Button`、`Badge`、`Table`…）继续以现名从
   `@nerv-iip/ui` 导出，不加 `Nv`，不做别名。
5. **非组件导出不改名**：composable（`useTheme`、`useScreenData`、`useSidebar`…）、
   函数（`messagePro`、`notificationPro`、`dismissNotify`、`resolveStatus`、`cn`…）、
   常量（`nervMotion`、`ACCENT_PRESETS`…）、独立类型（`LineSeries`、`TabItem`…）
   全部保持现名。唯一例外：**名字含 `Pro` 的组件派生类型与变体常量随组件改名**
   （`DataTableProColumn → NvDataTableColumn`、`fieldProVariants → nvFieldVariants`，
   全表见附录 A）。`messagePro`/`notificationPro` 两个函数名的 `Pro` 尾巴保留到收口批
   （MAN-436）再评估是否提供 `nvMessage`/`nvNotification` 别名，不阻塞组件迁移。
6. **类名与 data-slot 跟随命名空间**：库内 CSS 类前缀 PC `nv-`、screen `nv-scr-`、
   mobile `nv-m-`、touch `nv-t-`（现 `.ds-*`→`.nv-*`、`.sb-*`→`.nv-scr-*`、
   `.ds-tbtn`→`.nv-t-btn`）。Nv 件的 `data-slot` 值改为 `nv-` 前缀
   （`dialog-pro-content → nv-dialog-content`），原版件的 `data-slot` 零改动。

### 1.2 场景件命名判定流程（新组件与存量判定共用）

对 screen/touch/mobile 的候选名依次执行，先命中先停：

- **R1** 现名已含场景词根（`Screen`/`Mobile`/`Touch`，任意位置）→ `Nv` + 原名，
  词根与词序保持（`ScreenButton → NvScreenButton`、`AppShellMobile → NvAppShellMobile`）。
- **R2** 素名与 shadcn 原版导出名相同，或与 PC 层（pro/blocks/layout 改名后）素名相同
  → 必须加本场景词根（mobile `Badge` 撞原版 `Badge` → `NvMobileBadge`）。
- **R3** 素名属于通用交互原语词——判据：出现在 Arco Design PC 组件名单
  （`frontend/DESIGN/component-coverage.md` 既定的 PC 参照系）→ 加场景词根
  （mobile `Steps`/`Collapse`/`Rate`/`Tag`/`Result` → `NvMobileSteps` 等）。
- **R3b** 复合名以通用原语词（Chart/Table/Card/Tag/Bar…）结尾：首词为工业/业务专名
  （Oee、Takt、Alarm、Kpi、Scan、Station、Qty、Andon…开放词表，扩词须 review）→
  专名主导，直接 `Nv`（`AlarmTable → NvAlarmTable`、`KpiBar → NvKpiBar`）；首词为
  通用修饰词（Trend、Status、List…）→ 加场景词根（`TrendChart → NvScreenTrendChart`）。
- **R4** 其余（工业专名、本项目自造名、纯场景语汇）→ 直接 `Nv`
  （`OeeHero → NvOeeHero`、`Picker → NvPicker`、`Cell → NvCell`、`BottomSheet → NvBottomSheet`）。
- **R5** 专项与兜底：`Status*` 家族在非 PC 场景一律带词根（PC 已占用 `NvStatusDot`/
  `NvStatusBadge`，防状态语义件跨场景混淆：`StatusCard → NvScreenStatusCard`）；
  任何仍拿不准的 → 带词根（素名是稀缺资源，默认留给 PC）。

**新组件归属判定**：先问"这件的交互密度/视距/输入方式属于哪个表面"（PC 指针紧凑 36–40px /
mobile 原生触控 40–48px / touch 大触控 56–72px / screen 挂墙远读）——归属定目录与 token
命名空间；再走 R1–R5 定名。一件组件跨两个表面时必须拆两件分别实现（既有分层铁律），
不允许"一件双态"。

### 1.3 两代并存件的处置

被新代取代的旧件**不授予 Nv 名**，保旧名标 `@deprecated`，在收口批删除：
`blocks/DataTable`、`blocks/DataTablePagination`（由 `NvDataTable`/`NvDataTablePagination`
= 原 `DataTablePro`/`DataTablePaginationPro` 取代）、`blocks/StatusBadge`（由
`NvStatusBadge` = 原 `StatusBadgePro` 取代）。`screen/WaterLevel.vue` 未从 barrel 导出且
全库零引用，由 MAN-435 决定补导出（届时名 `NvWaterLevel`）或删除。

## Decision 2: 包名——保持 `@nerv-iip/ui` 与 `@nerv-iip/ui-mobile`

**结论：不改包名。** NvUI 是组件层品牌，由组件前缀、文档站与 DESIGN 文档承载；包名
维持 `@nerv-iip/ui`（+ `@nerv-iip/ui-mobile`）。

两方案评估：

| 维度 | A. 改 `@nerv-iip/nvui`(+nvui-mobile) | B. 保持包名，只改组件名（**采纳**） |
|---|---|---|
| import 替换面 | 167 + 17 = 184 个文件的 import 行 + ui-mobile 对 ui 的 workspace 依赖声明 | 0（组件名替换已由 MAN-435 codemod 承担，不叠加） |
| workspace/工具链 | 6 个 app 的 package.json、若目录同步改名则 5 处 `@source` 相对路径、vite workspace:build 配置、design-system `@source`、tsconfig 引用 | 0 |
| 发布影响 | 全部包 `private: true`，无 npm 发布，改名无对外收益 | 同左，无损失 |
| 并行冲突 | 与业务批（MAN-430/431/434）及全部在途 PR 产生全局 rebase 冲突 | 组件名走别名过渡，可渐进合入 |
| 品牌一致性 | 包名即品牌；但 scope `@nerv-iip` 已含品牌，`@nerv-iip/nvui` 双品牌冗余 | 包名语义"nerv-iip 的 UI 包"本就成立 |

**重新评估触发条件**：未来组件库对外发布/开源、或平台 SDK 需要独立分发 UI 包时，再以
`@nerv-iip/nvui`（或独立 scope）立项改名——届时有 semver 与 registry 语境，成本收益
才成立。在那之前，任何 PR 不得顺手改包名。

## Decision 3: token 场景命名空间

### 3.1 分层与前缀

```text
┌ 契约层（不改名）      shadcn 官方主题变量：--background --foreground --card --popover
│                       --primary --secondary --muted --accent --destructive --border
│                       --input --ring --chart-1..5 --sidebar-* --radius，及 Tailwind 主题名
│                       （--font-sans 等）。原版组件经 @theme inline 桥接依赖这些名字，
│                       改名即等于改原版 —— 红线。
├ Nv 语义层（PC 共享）  --nv-*：项目自有语义扩展。现有 --brand(+fg,+strong)、
│                       --success/--warning(+fg,+strong)、--destructive-strong、
│                       --ease-*、--duration-*、--shadow-xs/sm/md/lg、--shadow-glow-brand
│                       迁移为 --nv-brand、--nv-success、--nv-ease-out-quart…（附录 C 全表）
├ 场景命名空间          screen --nv-scr-*（现 --sb-* 全表迁移，附录 B）
│                       mobile --nv-m-*（当前空集，规范先行；mobile.css 的安全区
│                                        @utility 保持，token 仍来自共享层）
│                       touch  --nv-t-*（当前空集，规范先行）
└ Tailwind 桥           @theme inline 的 --color-*/--shadow-* 桥接名不变（utility 类名
                        text-success、bg-brand 等业务写法零影响），桥的右值指向新名：
                        --color-success: var(--nv-success)
```

原则：

1. **primitive 值（OKLCH/曲线/时长）全库共享**；场景层不得复制字面量。
2. **跨场景同值必须用 var 引用链表达**：`--nv-scr-ease: var(--nv-ease-out-quart)`（替代
   现状 `--sb-ease` 硬编码同值）。引用链方向固定为"场景名 → 共享名"，禁止反向。
3. **允许跨场景取值相同，但名称必须隔离**：screen 想用与 PC 相同的绿色，写
   `--nv-scr-green: var(--nv-success)`，不得在 screen 组件里直接引 `--nv-success`。
   场景组件只允许引用本场景前缀 + 契约层 token；跨场景直引由 contract test 拦截（4.4）。
4. **场景 token 表文件与场景层同目录**（现状 `screen/tokens.css` 模式不变，改名为
   `--nv-scr-*`；mobile/touch 表在首个场景 token 出现时建立）。

### 3.2 动效：值共享、引用名分场景、motion-v 统一封装

- CSS 侧：曲线/时长的唯一定义在 Nv 语义层（`--nv-ease-out-quart`、`--nv-duration-fast`…），
  场景引用名（`--nv-scr-ease`、`--nv-scr-ease-emphasized`）一律 var 链指向共享名。
- JS 侧：motion-v 预设唯一来源 `packages/ui/src/lib/motion.ts`（`nervMotion`，名字不动），
  数值与 CSS token 同表对齐（现已对齐：0.187s ↔ `--duration-fast-invoke: 187ms`）。
  场景专属预设（如大屏入场）以 `nervMotion` 的值组合导出，不另造数值。
- 组件位移/透明度类动效优先走 motion-v 封装（whilePress、useReducedMotion 全局降级），
  纯 CSS 过渡引用 token 曲线/时长，两轨都禁止内联 cubic-bezier 字面量。

## Decision 4: 样式隔离——CSS cascade layer

### 4.1 层序（全局唯一）

```css
@layer theme, nv-tokens, base, components, nv-components, utilities, nv-overrides, app;
```

| 层 | 内容 | 归属 |
|---|---|---|
| `theme` | Tailwind `@theme` 产物（--color-* 桥等） | Tailwind |
| `nv-tokens` | 库 token 表：theme.css 的 :root/.dark 声明、场景 tokens.css | NvUI |
| `base` | Tailwind preflight + 库 base 段（box-sizing/body 基线，现 theme.css `@layer base` 并入） | Tailwind + NvUI |
| `components` | Tailwind/shadcn 组件层产物（tw-animate-css、shadcn-vue/tailwind.css） | Tailwind |
| `nv-components` | 库手写组件样式：全部 SFC `<style>` 规则、overlay 动效（`.nv-overlay-content`）、`.nv-scr-scroll` 等 | NvUI |
| `utilities` | Tailwind utilities（app 模板里的 class 能覆盖组件默认样式） | Tailwind |
| `nv-overrides` | 库级必须赢过 utilities 的装饰：玻璃拟态 `[data-slot=…]` 覆盖、sidebar premium 选中态、mobile overlay glass（即现状全部"故意 unlayered"规则的收编地） | NvUI |
| `app` | app 自定义 CSS（business-console sidebar active、apps/screen body 等），app 主权最高 | 各 app |

规则：

1. **库内禁止 unlayered 规则**。白名单仅 `@font-face`、`@keyframes`、`@property`、
   `@import`、`@custom-variant`、`@theme`（本身不参与层叠或有专属语义）。现有三处
   "故意 unlayered"全部收编进 `nv-overrides`。
2. **SFC `<style>` 统一包 `@layer nv-components { … }`**（scoped 照常可用；@layer 是纯
   CSS 特性，Vite/Vue 编译透传）。
3. **层序声明必须是每个 app `main.css` 的第一条语句**（`@layer` statement 允许出现在
   `@import` 之前），保证构建产物首个 layer 语句即全序。层归属通过
   `@import … layer(<name>)` 在导入点指定；库的 overrides 段拆为独立文件
   （`styles/overrides.css`，文件内不包层），产品 app 以 `layer(nv-overrides)` 导入。
4. **app 自定义样式一律进 `@layer app`**；紧急 hotfix 允许临时 unlayered，但 review
   checklist 项要求限期归层。
5. 已知风险与验证条款：`@layer` 基线为 2022 全绿浏览器，大屏一体机/车间 WebView 的内核
   版本在 MAN-433 落地前实机核查；vitest 不编译 CSS，层序正确性除 contract test 文本
   断言外，**必须真机走查亮/暗/大屏三态**（门禁绿 ≠ 真机无 bug，历史教训）。

### 4.2 VitePress 文档站嵌入规范（制度化现行解法 + 官方隔离）

design-system 站是唯一"宿主自带 unlayered 敌意样式"的环境，其规范与产品 app 不同：

1. **utilities 与 nv-overrides 在站内 unlayered 导入**（例外条款）：VitePress 自带
   unlayered 重置（`h1..h6`、`button`、`.vp-doc table`）赢过一切 layer 内样式，utilities
   必须裸导入才能以 specificity 取胜（现行做法，保持）；`styles/overrides.css` 因文件内
   不包层，站内直接 `@import` 即裸导入，玻璃拟态等在站内依旧生效。
2. **启用 `postcssIsolateStyles()`**（VitePress 1.6.3 官方导出，已核实存在）+ 组件 demo
   统一容器（`Demo.vue`/`ScreenDemo.vue`/`MobileDoc.vue`）根节点挂 `vp-raw`：让
   `.vp-doc` 排版样式不再进入 demo 子树，替代"每出一个污染写一条 `.vp-doc` 反制"的
   增量模式。存量反制条款（`.sb-tbl` 等）在其生效验证后删除。
3. **`--vp-*` 桥接映射保留**：站点 chrome（导航/侧栏/代码块）继续经 `--vp-* → 我们的
   token` 换肤，token 改名时同步右值（`--vp-c-brand-1: var(--nv-brand-strong)`）。
4. **禁用 `revert-layer`**（历史坑：嵌入组件样式回退异常，已从站内移除，不得复用）。
5. 新增组件文档页的验收：demo 必须包在统一容器内，不允许裸放组件到 markdown。

### 4.3 交付形态

`@nerv-iip/ui` 的样式交付物按层拆分（MAN-433 落地）：

- `styles/theme.css`：`nv-tokens` + `base` 段（文件内 `@layer` 包裹）+ `@theme inline` 桥；
- `components/screen/tokens.css` 等场景表：文件内 `@layer nv-tokens` 包裹；
- `styles/overrides.css`：**不包层**，由宿主导入点定层（产品 app `layer(nv-overrides)`，
  文档站裸导入）；
- SFC 样式：内嵌 `@layer nv-components`。

### 4.4 design contract test 扩展守护项（MAN-433 一并落地）

`packages/ui/src/design-system.contract.test.ts` 在既有 7 组 token 断言（断言文本随
附录 C 改名同步）之外，新增：

1. **层序**：四个产品 app 的 `main.css` 首条语句 = 4.1 全序，逐字一致；
2. **库内零白名单外 unlayered**：扫描 `theme.css`/各场景 `tokens.css`/`overrides.css`
   之外库内 CSS 顶层规则必须位于 `@layer` 内；
3. **screen 命名空间**：`--nv-scr-*` 全表（附录 B 30 项）存在；别名期断言
   `--sb-<x>: var(--nv-scr-<x>)` 形式；收口后断言全库 `--sb-` 零匹配；
4. **引用链**：`--nv-scr-ease: var(--nv-ease-out-quart)`、
   `--chart-1: var(--nv-brand)`、`--color-success: var(--nv-success)` 等关键链条；
5. **原版纯净**：`components/ui/**` 内不出现 `Nv`、`@layer nv-`、`--nv-` 字样
   （"原版零改动"从约定升级为机器断言）;
6. **data-slot 命名空间**：Nv 件 `data-slot` 以 `nv-` 开头（抽查 dialog/sheet/dropdown
   等关键件）；
7. **跨场景污染**：screen 组件源码不引用 `--nv-m-`/`--nv-t-`，mobile/touch 组件不引用
   `--nv-scr-`；
8. **旧名封锁（守护期起）**：库导出与 app import 扫描中旧组件名/旧 token 名零新增
   （实现走 contract test 文本扫描 + CI grep gate，**不引入 ESLint**——遵守 ADR 0006
   的 Vite+ 工具链决策）。

## Decision 5: 迁移路线与验收口径

顺序（总约束：**全部组件迁移批在业务批开始之前完成**）：

| 步 | 批次 | 内容 | 验收口径 |
|---|---|---|---|
| S1 别名过渡 | MAN-433 | 库侧：按附录 A 新增 `Nv*` 为 canonical 实现名；旧名以 `export { NvButton as ButtonPro }` 形式保留并标 JSDoc `@deprecated`（IDE 划线）；token 新名落地 + 旧名 var 别名（`--sb-bg: var(--nv-scr-bg)`）；4.1/4.3 layer 结构落地；4.4 契约守护（别名期形态）上线；design-system 站组件页标题/示例切新名（文档站是唯一教学面，先切断旧名传播） | `pnpm -C frontend typecheck && test && build` 全绿；contract test 别名期断言绿；文档站构建绿 + 实机抽查（亮/暗/大屏）；既有业务代码**零改动**仍编译通过（别名兜底的直接证据） |
| S2 codemod 分 app | MAN-435 | 按 app 逐个 PR 替换 import 与模板引用：business-console（93 页）→ business-pda（15）→ screen（10）→ console（10）。codemod 以附录 A 为唯一映射输入（脚本 + 人工复核），同 PR 内完成该 app 的 `--sb-*`/类名引用替换 | 每 app PR：typecheck/test/build 三绿 + 真机逐模块抽查；该 app 源码旧名/旧 token **零匹配**（CI grep）；不允许半迁移合入 |
| S3 守护 | MAN-436 前置 | 4.4 第 8 项切到"旧名零新增"强断言；PR review checklist 加命名/层归属项（governance.md 已同步） | 守护断言在 CI 稳定绿一周（覆盖并行 PR 汇入） |
| S4 收口 | MAN-436 | 删除全部 deprecated 别名导出、旧 token 别名、旧类名；1.3 的旧代组件（blocks/DataTable 等）删除；contract test 切收口断言（`--sb-` 全库零匹配、旧名导出不存在）；DESIGN 文档（component-coverage、motion-interaction、screen/product.md、pro/MIGRATION.md）术语归档更新 | 全库 `ButtonPro\|--sb-\|\.sb-\|\.ds-` 等旧标识零匹配（白名单：本 ADR 与历史 roadmap 文档）；frontend 全量 gate 绿；文档站构建绿 |

回滚策略：S1 合入后任何一步发现阻断性问题，业务代码可继续用旧名（别名兜底），
回滚粒度为"撤销单个 app 的 codemod PR"，不存在全局不可逆点；S4 之后才移除退路。

## Alternatives Considered

1. **包名改 `@nerv-iip/nvui`**：见 Decision 2 对比表。被否：private 包无发布收益，
   184 个文件 import 面 + 工具链配置面纯属机械成本，且与并行业务批的冲突面最大化。
2. **全场景强制词根（screen 件一律 NvScreenXxx）**：判定简单，但 `NvScreenOeeHero`、
   `NvScreenTaktGantt` 冗长且专名本身已自明场景；与上游已定的"天然独有名直接 Nv"
   （NvScanBar/NvOeeHero）矛盾。被否，改用 R1–R5 判定流程 + 附录 A 冻结逐件结果。
3. **素名完全自由（先到先得）**：会把 `Result`、`Steps`、`Tag` 等 PC 参照系词汇让给
   移动端，PC 后续立项被迫用别扭名。被否：素名优先权归 PC（R2/R3）。
4. **样式隔离用 Shadow DOM / CSS Modules / BEM 全前缀**：Shadow DOM 与 reka-ui portal
   / Tailwind utility 流不兼容；CSS Modules 覆盖不了 utility-first 组件（样式主体是
   模板 class）；BEM 全前缀等于放弃 Tailwind 生态。cascade layer 是唯一与
   Tailwind v4 原生层模型（本就是 layer）同构的方案。
5. **文档站继续手写 `.vp-doc` 反制、不引入 postcssIsolateStyles**：每新增一类组件文档
   就多一坨站点特判 CSS，已经发生三轮（表格/标题/按钮）。官方隔离机制存在且已核实
   可用，制度化成本更低。保留反制条款仅作为隔离机制验证失败时的回退。
6. **token 直接全库改名（契约层也加 nv 前缀）**：`--background → --nv-background` 会
   切断原版组件经 Tailwind 桥的依赖链，等价于改原版——违反红线。被否，契约层名单
   永久冻结（3.1）。

## Consequences

**变易。** 原版与品牌件一眼可分（`Nv` 前缀即边界），协作代理误用原版的诱因消除；
`Badge`/`Empty`/`DropdownMenu` 双包撞名消解（mobile 侧改 `NvMobile*`）；token 场景
归属由名字自证，跨场景引用链可被机器断言；样式优先级从"谁 unlayered 谁赢"变为
八层全序，app 主权（`app` 层）与库级装饰（`nv-overrides`）各得其所；文档站嵌入从
逐坑手补变为容器隔离制度。

**变难。** 一次性认知成本：迁移期内旧名（deprecated）与新名并存，IDE 补全会同时出现
两套（靠 @deprecated 划线缓解）；S2 是 118 页 × import/模板/token 的大面积机械替换，
必须依赖附录 A 而非人脑记忆；类型名连带改名（`DataTableProColumn` 等）会出现在
业务代码签名里，codemod 须覆盖 type import。

**后续维护者须知。** 附录 A/B/C 是 MAN-433/435 的执行合同，执行中发现映射遗漏先修订
本 ADR 再动代码；新组件命名一律走 1.2 流程，R3b 专名词表扩词须在 PR 中注明；
`screen/index.ts` 的 token 副作用 import（PC app 会捎带大屏 token 表）在 layer 化后
无害但仍是体积冗余，若拆子入口另立 ADR（涉及导出边界，不在本 ADR 范围）。

---

## 附录 A: 组件旧名 → 新名完整映射表（MAN-433/435 执行输入）

判定依据列：`PC` = PC 素名规则（去 Pro/裸名升级）；`R1`–`R5` 见 1.2；`废` = 1.3 处置。

> **冻结后新增（无旧名，完全 Pro-free）。** MAN-439（#793）在 `pro/combobox/` 新增两个
> 全新组件，直接以 `Nv*` canonical 名落地（R1），不涉及旧名映射：
> - `NvCombobox`（输入联想框：文本输入即过滤建议，允许自由录入）
> - `NvSearchSelect`（弹出选择框：可搜索的弹出单选，仅选不填）
>
> 二者的**文件名即 `NvCombobox.vue`/`NvSearchSelect.vue`**（不带 `Pro` 后缀），且内部**不含**
> `data-slot="*-pro"` / `.ds-*` / `.sb-*`（纯 Tailwind utility 类 + ARIA `role` 语义）——即
> 直接落在 S4 收口后的目标形态，不给 `pro→nv` 收口（#896）新增任何债。已并入
> `nvui-naming.contract.test.ts` 的冻结 canonical 集合。（`pro/` 目录名是 PC 素名层标识，保留。）

### A1. PC 素名层 — `pro/`（35 目录，116 个组件导出）

| 目录 | 旧名 | 新名 |
|---|---|---|
| alert-dialog | AlertDialogPro | NvAlertDialog |
| | AlertDialogProAction | NvAlertDialogAction |
| | AlertDialogProCancel | NvAlertDialogCancel |
| | AlertDialogProContent | NvAlertDialogContent |
| | AlertDialogProDescription | NvAlertDialogDescription |
| | AlertDialogProFooter | NvAlertDialogFooter |
| | AlertDialogProHeader | NvAlertDialogHeader |
| | AlertDialogProMedia | NvAlertDialogMedia |
| | AlertDialogProTitle | NvAlertDialogTitle |
| | AlertDialogProTrigger | NvAlertDialogTrigger |
| badge | BadgePro | NvBadge |
| button | ButtonPro | NvButton |
| card | CardPro | NvCard |
| | CardProAction | NvCardAction |
| | CardProContent | NvCardContent |
| | CardProDescription | NvCardDescription |
| | CardProFooter | NvCardFooter |
| | CardProHeader | NvCardHeader |
| | CardProTitle | NvCardTitle |
| | MetricCardPro | NvMetricCard |
| carousel | CarouselPro | NvCarousel |
| chart | AreaChartPro | NvAreaChart |
| | LineChartPro | NvLineChart |
| | BarChartPro | NvBarChart |
| | DonutChartPro | NvDonutChart |
| checkbox | CheckboxPro | NvCheckbox |
| command | CommandPro | NvCommand |
| data-table | DataTablePro | NvDataTable |
| | DataTablePaginationPro | NvDataTablePagination |
| | DataTableToolbarPro | NvDataTableToolbar |
| date-picker | DatePickerPro | NvDatePicker |
| | DateRangePickerPro | NvDateRangePicker |
| descriptions | DescriptionsPro | NvDescriptions |
| dialog | DialogPro（reka 再导出） | NvDialog |
| | DialogProTrigger（reka 再导出） | NvDialogTrigger |
| | DialogProClose（reka 再导出） | NvDialogClose |
| | DialogProContent | NvDialogContent |
| | DialogProTitle | NvDialogTitle |
| | DialogProDescription | NvDialogDescription |
| | DialogProHeader | NvDialogHeader |
| | DialogProFooter | NvDialogFooter |
| dropdown-menu | DropdownMenuPro | NvDropdownMenu |
| | DropdownMenuProCheckboxItem | NvDropdownMenuCheckboxItem |
| | DropdownMenuProContent | NvDropdownMenuContent |
| | DropdownMenuProGroup | NvDropdownMenuGroup |
| | DropdownMenuProItem | NvDropdownMenuItem |
| | DropdownMenuProLabel | NvDropdownMenuLabel |
| | DropdownMenuProRadioGroup | NvDropdownMenuRadioGroup |
| | DropdownMenuProRadioItem | NvDropdownMenuRadioItem |
| | DropdownMenuProSeparator | NvDropdownMenuSeparator |
| | DropdownMenuProShortcut | NvDropdownMenuShortcut |
| | DropdownMenuProSub | NvDropdownMenuSub |
| | DropdownMenuProSubContent | NvDropdownMenuSubContent |
| | DropdownMenuProSubTrigger | NvDropdownMenuSubTrigger |
| | DropdownMenuProTrigger | NvDropdownMenuTrigger |
| | DropdownMenuProPortal（reka 再导出） | NvDropdownMenuPortal |
| field | FieldPro | NvField |
| | FieldProContent | NvFieldContent |
| | FieldProDescription | NvFieldDescription |
| | FieldProError | NvFieldError |
| | FieldProGroup | NvFieldGroup |
| | FieldProLabel | NvFieldLabel |
| | FieldProLegend | NvFieldLegend |
| | FieldProSeparator | NvFieldSeparator |
| | FieldProSet | NvFieldSet |
| | FieldProTitle | NvFieldTitle |
| filter-bar | FilterBarPro | NvFilterBar |
| form-section | FormSectionPro | NvFormSection |
| input | InputPro | NvInput |
| kanban | KanbanPro | NvKanban |
| loader | Loader | NvLoader |
| metric-comparison | MetricComparisonPro | NvMetricComparison |
| navigation-menu | NavigationMenuPro | NvNavigationMenu |
| | NavigationMenuProContent | NvNavigationMenuContent |
| | NavigationMenuProIndicator | NvNavigationMenuIndicator |
| | NavigationMenuProItem | NvNavigationMenuItem |
| | NavigationMenuProLink | NvNavigationMenuLink |
| | NavigationMenuProList | NvNavigationMenuList |
| | NavigationMenuProTrigger | NvNavigationMenuTrigger |
| | NavigationMenuProViewport | NvNavigationMenuViewport |
| notify | NotifierHost | NvNotifierHost |
| popconfirm | PopconfirmPro | NvPopconfirm |
| radio | RadioGroupPro | NvRadioGroup |
| | RadioGroupProItem | NvRadioGroupItem |
| record-card | RecordCardPro | NvRecordCard |
| select | SelectPro | NvSelect |
| | SelectProTrigger | NvSelectTrigger |
| | SelectProContent | NvSelectContent |
| | SelectProItem | NvSelectItem |
| | SelectProGroup（reka 再导出） | NvSelectGroup |
| | SelectProValue（reka 再导出） | NvSelectValue |
| sheet | SheetPro（reka 再导出） | NvSheet |
| | SheetProTrigger（reka 再导出） | NvSheetTrigger |
| | SheetProClose（reka 再导出） | NvSheetClose |
| | SheetProContent | NvSheetContent |
| | SheetProTitle | NvSheetTitle |
| | SheetProDescription | NvSheetDescription |
| | SheetProHeader | NvSheetHeader |
| | SheetProFooter | NvSheetFooter |
| sidebar | SidebarProBrand | NvSidebarBrand |
| | SidebarProDot | NvSidebarDot |
| | SidebarProSub | NvSidebarSub |
| | SidebarProUser | NvSidebarUser |
| slider | SliderPro | NvSlider |
| status | StatusDot | NvStatusDot |
| | StatusBadgePro | NvStatusBadge |
| switch | SwitchPro | NvSwitch |
| tabs | TabsPro | NvTabs |
| | TabsProContent | NvTabsContent |
| | TabsProList | NvTabsList |
| | TabsProTrigger | NvTabsTrigger |
| time-picker | TimePickerPro | NvTimePicker |
| timeline | TimelinePro | NvTimeline |
| tooltip | TooltipPro（reka 再导出） | NvTooltip |
| | TooltipProProvider（reka 再导出） | NvTooltipProvider |
| | TooltipProTrigger（reka 再导出） | NvTooltipTrigger |
| | TooltipProContent | NvTooltipContent |

**pro 层派生类型/常量随改**：`DataTableProAlign → NvDataTableAlign`、
`DataTableProColumn → NvDataTableColumn`、`DataTableProDensity → NvDataTableDensity`、
`DataTableProFilterOption → NvDataTableFilterOption`、`DataTableProFilters →
NvDataTableFilters`、`DataTableProSort → NvDataTableSort`、`FieldProVariants →
NvFieldVariants`、`fieldProVariants → nvFieldVariants`。
不带 Pro 的独立类型（`LineSeries`、`BarSeries`、`DonutSlice`、`CommandGroup`、
`CommandItem`、`DateRange`、`DescriptionItem`、`FilterField`、`FilterFieldOption`、
`KanbanColumn`、`KanbanTone`、`MetricComparisonSide`、`RecordCardMeta`、
`RecordCardStatus`、`TimelineItem`、`TimelineTone`）与函数
（`messagePro`、`notificationPro`、`dismissNotify`、`useNotifyStore` 及其类型）不改名。

### A2. PC 素名层 — `blocks/`（9 目录，11 个组件导出）

| 旧名 | 新名 | 依据 |
|---|---|---|
| AppShellInset | NvAppShellInset | PC |
| DataTable | **不授予 Nv 名，@deprecated** → 迁移到 NvDataTable（原 DataTablePro） | 废 |
| DataTablePagination | **@deprecated** → NvDataTablePagination（原 DataTablePaginationPro） | 废 |
| PageHeader | NvPageHeader | PC |
| RowActions | NvRowActions | PC |
| SectionCard | NvSectionCard | PC |
| SectionCards | NvSectionCards | PC |
| StatusBadge | **@deprecated** → NvStatusBadge（原 StatusBadgePro） | 废 |
| ThemePicker | NvThemePicker | PC |
| ThemeToggle | NvThemeToggle | PC |
| Toolbar | NvToolbar | PC |

（`resolveStatus` 函数与 `ResolvedStatus`/`StatusTone`/`PageHeaderCrumb`/
`TrendDirection`/`DataTableAlign`/`DataTableColumn`/`DataTableSort` 类型不改名；
deprecated 组件的旧类型随组件在 S4 一并删除。）

### A3. PC 素名层 — `layout/`（8 件）

| 旧名 | 新名 | | 旧名 | 新名 |
|---|---|---|---|---|
| App | NvApp | | PageAside | NvPageAside |
| AppHeader | NvAppHeader | | PageGrid | NvPageGrid |
| Container | NvContainer | | PageColumns | NvPageColumns |
| Page | NvPage | | PageSection | NvPageSection |

### A4. screen 层（34 件：33 导出 + 1 未导出）

| 旧名 | 新名 | 依据 |
|---|---|---|
| ScreenPanel | NvScreenPanel | R1 |
| ScreenScrollArea | NvScreenScrollArea | R1 |
| ScreenScaler | NvScreenScaler | R1 |
| ScreenHeader | NvScreenHeader | R1 |
| ScreenButton | NvScreenButton | R1 |
| ScreenTable | NvScreenTable | R1 |
| ScreenSelect | NvScreenSelect | R1 |
| ScreenSearch | NvScreenSearch | R1 |
| ScreenInput | NvScreenInput | R1 |
| ScreenTabs | NvScreenTabs | R1 |
| ScreenSegmented | NvScreenSegmented | R1 |
| ScreenSwitch | NvScreenSwitch | R1 |
| ScreenPagination | NvScreenPagination | R1 |
| ScreenBarChart | NvScreenBarChart | R1 |
| ScreenDonut | NvScreenDonut | R1 |
| ScreenPareto | NvScreenPareto | R1 |
| OeeHero | NvOeeHero | R4 工业专名 |
| TaktGantt | NvTaktGantt | R4 工业专名 |
| DigitalFlop | NvDigitalFlop | R4 大屏专名 |
| RingGauge | NvRingGauge | R3b 专名图形 |
| CapsuleBar | NvCapsuleBar | R3b 专名主导 |
| Sparkline | NvSparkline | R3b 专名图形 |
| ScrollBoard | NvScrollBoard | R4 大屏专名 |
| KpiBar | NvKpiBar | R3b 专名(Kpi)主导 |
| AlarmTable | NvAlarmTable | R3b 专名(Alarm)主导 |
| TrendChart | NvScreenTrendChart | R3b 通用修饰(Trend)+原语(Chart) |
| StatusCard | NvScreenStatusCard | R5 Status 家族 |
| StatusTag | NvScreenStatusTag | R5 Status 家族 |
| StatusLight | NvScreenStatusLight | R5 Status 家族 |
| TitleBar | NvTitleBar | R4 大屏装饰语汇（PC 对应位为 NvPageHeader，无真实撞名） |
| TechFrame | NvTechFrame | R4 大屏装饰专名 |
| BorderPanel | NvBorderPanel | R4 大屏装饰专名 |
| GlowDivider | NvGlowDivider | R4 大屏装饰专名 |
| WaterLevel（未导出、零引用） | 待 MAN-435 定：导出为 NvWaterLevel 或删除 | 1.3 |

（`scale.ts`/`useScreenData.ts` 的全部导出不改名。）

### A5. touch 层（5 件）

| 旧名 | 新名 | 依据 |
|---|---|---|
| TouchButton | NvTouchButton | R1 |
| TouchSegmented | NvTouchSegmented | R1 |
| QtyStepper | NvQtyStepper | R3b 专名(Qty)主导 |
| StatTile | NvStatTile | R4 自造名 |
| StationBar | NvStationBar | R3b 专名(Station)主导 |

（`SegmentOption` 类型不改名。）

### A6. `@nerv-iip/ui-mobile`（47 个组件导出，43 件）

| 旧名 | 新名 | 依据 |
|---|---|---|
| AppShellMobile | NvAppShellMobile | R1（词序保持） |
| MobileButton | NvMobileButton | R1 |
| MobileSwitch | NvMobileSwitch | R1 |
| MobileInput | NvMobileInput | R1 |
| MobileRadioGroup | NvMobileRadioGroup | R1 |
| MobileRadioItem | NvMobileRadioItem | R1 |
| MobileTabs | NvMobileTabs | R1 |
| MobileCheckbox | NvMobileCheckbox | R1 |
| MobileDatePicker | NvMobileDatePicker | R1 |
| MobileDialog | NvMobileDialog | R1 |
| MobileGrid | NvMobileGrid | R1 |
| MobileToast | NvMobileToast | R1 |
| MobileAvatar | NvMobileAvatar | R1 |
| MobileSkeleton | NvMobileSkeleton | R1 |
| MobileProgress | NvMobileProgress | R1 |
| MobileSlider | NvMobileSlider | R1 |
| MobileImage | NvMobileImage | R1 |
| Badge | NvMobileBadge | R2 撞原版 Badge |
| Empty | NvMobileEmpty | R2 撞原版 Empty |
| DropdownMenu | NvMobileDropdownMenu | R2 撞原版 DropdownMenu |
| DropdownMenuItem | NvMobileDropdownMenuItem | R2 撞原版 DropdownMenuItem |
| Tag | NvMobileTag | R3（Arco: Tag） |
| Divider | NvMobileDivider | R3（Arco: Divider） |
| Rate | NvMobileRate | R3（Arco: Rate；PC coverage P2 待建） |
| Steps | NvMobileSteps | R3（Arco: Steps） |
| Collapse | NvMobileCollapse | R3（Arco: Collapse） |
| Result | NvMobileResult | R3（Arco: Result；PC coverage P2 待建） |
| ScanBar | NvScanBar | R3b 专名(Scan)主导（上游钦定例） |
| ListRow | NvListRow | R4 自造名 |
| BottomSheet | NvBottomSheet | R4 移动专名 |
| NavBar | NvNavBar | R4 移动专名（PC 语汇为 Header/Breadcrumb） |
| Cell | NvCell | R4 移动语汇（Vant/TDesign 共识；PC 单元格语汇为 TableCell，不同名） |
| CellGroup | NvCellGroup | R4 同上 |
| TabBar | NvTabBar | R4 移动专名 |
| NoticeBar | NvNoticeBar | R4 移动专名 |
| SearchBar | NvSearchBar | R4 移动专名（PC 搜索语汇为 Command/Input） |
| Stepper | NvStepper | R4（Arco 无 Stepper，PC 对应件规划名为 InputNumber） |
| Picker | NvPicker | R4 移动专名（滚轮选择器） |
| ActionSheet | NvActionSheet | R4 移动专名 |
| SwipeCell | NvSwipeCell | R4 移动专名 |
| PullRefresh | NvPullRefresh | R4 移动专名 |
| InfiniteList | NvInfiniteList | R4 自造名 |
| VirtualList | NvVirtualList | R4 自造名 |
| Fab | NvFab | R4 移动专名 |
| NumberKeyboard | NvNumberKeyboard | R4 移动专名 |
| Swiper | NvSwiper | R4 移动语汇（PC 轮播已定名 NvCarousel） |
| SwiperItem | NvSwiperItem | R4 同上 |

（`cn`、`MOBILE_OVERLAY_TARGET` 及全部独立类型（`TabItem`、`MobileTabItem`、
`StepItem`、`ActionItem`、`SwipeAction`、`PickerOption`、`GridItem`、`FabAction`、
`DropdownOption`）不改名。）

### A7. 不参与改名的导出（明确列出，防 codemod 误伤）

- shadcn 原版全部导出（`components/ui/` 34 目录）：`Button`、`Badge`、`Table`、
  `Dialog`、`Sidebar` 家族、`FileUpload`/`FilePreview` 家族等——零改动零别名；
- `cn`、`useTheme` 家族（`ACCENT_PRESETS`、`initTheme`、`useColorMode`…）、
  `nervMotion`、`toast`（vue-sonner 透传）、`Toaster`；
- `@nerv-iip/app-shell`、`@nerv-iip/business-core` 等非 UI 包的组件不在本 ADR 范围。

## 附录 B: `--sb-*` → `--nv-scr-*` token 全表映射（30 项）

规则：机械替换前缀 `--sb-` → `--nv-scr-`；右值同步做引用链收敛的两项已标注。

| 旧名 | 新名 | 备注 |
|---|---|---|
| --sb-bg | --nv-scr-bg | |
| --sb-bg-accent | --nv-scr-bg-accent | |
| --sb-panel-a | --nv-scr-panel-a | |
| --sb-panel-b | --nv-scr-panel-b | |
| --sb-line | --nv-scr-line | |
| --sb-line-2 | --nv-scr-line-2 | |
| --sb-divider | --nv-scr-divider | |
| --sb-cyan | --nv-scr-cyan | |
| --sb-cyan-dim | --nv-scr-cyan-dim | |
| --sb-accent-from | --nv-scr-accent-from | |
| --sb-accent-to | --nv-scr-accent-to | |
| --sb-accent-fill | --nv-scr-accent-fill | |
| --sb-accent-edge | --nv-scr-accent-edge | |
| --sb-indigo | --nv-scr-indigo | |
| --sb-green | --nv-scr-green | |
| --sb-amber | --nv-scr-amber | |
| --sb-red | --nv-scr-red | |
| --sb-text | --nv-scr-text | |
| --sb-text-2 | --nv-scr-text-2 | |
| --sb-muted | --nv-scr-muted | |
| --sb-faint | --nv-scr-faint | |
| --sb-highlight | --nv-scr-highlight | |
| --sb-edge-gradient | --nv-scr-edge-gradient | |
| --sb-value-glow | --nv-scr-value-glow | |
| --sb-edge-glow | --nv-scr-edge-glow | |
| --sb-glow | --nv-scr-glow | |
| --sb-sheen | --nv-scr-sheen | |
| --sb-radius | --nv-scr-radius | |
| --sb-ease | --nv-scr-ease | 右值改 `var(--nv-ease-out-quart)`（现为复制的同值字面量） |
| --sb-ease-emphasized | --nv-scr-ease-emphasized | 右值改 `var(--nv-ease-out-expo)`（同上） |

类名同步：`.sb-scroll → .nv-scr-scroll`、`.sb-tbl → .nv-scr-tbl`、
`.sb-at-tbl → .nv-scr-at-tbl`，及 screen 组件内部全部 `sb-` 前缀类。
`apps/screen/src/assets/main.css` 中与 `tokens.css` 不一致的第二份 `.sb-scroll` 定义在
S2（screen app codemod）时消除——保留 tokens.css 单一定义，app 差异若确需保留则以
`@layer app` 内 `.nv-scr-scroll` 覆盖表达。

## 附录 C: PC 自有语义 token nv 化清单（契约层之外的全部 `:root`/`.dark` 自有名）

| 旧名 | 新名 |
|---|---|
| --brand / --brand-foreground / --brand-strong | --nv-brand / --nv-brand-foreground / --nv-brand-strong |
| --success / --success-foreground / --success-strong | --nv-success / --nv-success-foreground / --nv-success-strong |
| --warning / --warning-foreground / --warning-strong | --nv-warning / --nv-warning-foreground / --nv-warning-strong |
| --destructive-strong（--destructive 本体是契约名，不动） | --nv-destructive-strong |
| --ease-out-quart / --ease-out-expo / --ease-in-out-quart / --ease-fast-invoke / --ease-point-to-point | --nv-ease-out-quart / --nv-ease-out-expo / --nv-ease-in-out-quart / --nv-ease-fast-invoke / --nv-ease-point-to-point |
| --duration-fast / --duration-base / --duration-slow / --duration-fast-invoke / --duration-fade | --nv-duration-fast / --nv-duration-base / --nv-duration-slow / --nv-duration-fast-invoke / --nv-duration-fade |
| --shadow-xs / --shadow-sm / --shadow-md / --shadow-lg | --nv-shadow-xs / --nv-shadow-sm / --nv-shadow-md / --nv-shadow-lg |
| --shadow-glow-brand | --nv-shadow-glow-brand |

Tailwind 桥（`@theme inline`）左侧名保持（`--color-success`、`--color-brand-strong`、
`--shadow-sm`、`--ease-out-quart` 等 utility 契约不变，业务模板 `text-success`/
`bg-brand`/`shadow-sm`/`ease-out-quart` 零影响），右值切到新名。`.ds-overlay-content`
的局部变量（`--ds-overlay-*`）随类名改为 `.nv-overlay-content` / `--nv-overlay-*`。
契约层冻结名单（永不加前缀）：`--background`、`--foreground`、`--card(-foreground)`、
`--popover(-foreground)`、`--primary(-foreground)`、`--secondary(-foreground)`、
`--muted(-foreground)`、`--accent(-foreground)`、`--destructive(-foreground)`、
`--border`、`--input`、`--ring`、`--chart-1..5`、`--sidebar-*`、`--radius`、
`--font-sans`、`--font-heading`。
