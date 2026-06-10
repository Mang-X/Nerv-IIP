# 移动端地基与 PDA 应用壳（M0+M1）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 搭出可运行的 PDA 应用壳 `business-pda`，建立移动端组件包 `@nerv-iip/ui-mobile`（5 个地基组件）与同源内核包 `@nerv-iip/business-core` 骨架，并接好 Capacitor APK 打包基线。

**Architecture:** 复用现有前端单仓约定——Tailwind v4（CSS 驱动 token）、文件路由、`@pinia/colada` + generated `@nerv-iip/api-client`、`@nerv-iip/ui` 的 `theme.css`/`useTheme` 主题机制。新增两包一应用：`ui-mobile`（Reka UI + Tailwind 触摸组件，复用 `@nerv-iip/ui` 的 token 与 `cn`，原版零改）、`business-core`（SOP 状态机 + PDA 任务字典，PC/移动端同源）、`business-pda`（Capacitor 套壳的 Vue Web app，键盘楔入扫码）。

**Tech Stack:** Vue 3.5 / TypeScript / Tailwind v4 / reka-ui 2.9 / @vueuse/core 14 / @pinia/colada / vite-plus(`vp`) / vitest + @vue/test-utils / Capacitor（Android）。

---

## 本计划的范围

**交付（可独立运行、可测试）：** 一个能登录、展示首页（常驻扫码条 + 我的任务占位 + 快捷应用墙）的 PDA 应用，跑在浏览器与 Android WebView（Capacitor APK）；外加 5 个移动端地基组件与 SOP/字典内核骨架，全部带测试与三项门禁（typecheck/test/build）。

**不在本计划：** WMS/MES/设备业务作业页（Plan 2-4）、扫码解析增强（Plan 5）、离线写队列（phase 2）、后端缺口端点（后端 consolidated issue）。这些业务页落地前在应用墙上以 disabled/`route-ready` 占位，不做空跳转。

## 约定速查（零上下文工程师先读这一段）

- **包脚手架**：每个新 package/app 的 `package.json` 含 `"type":"module"`、`"private":true`、`"exports":{".":"./src/index.ts"}`、`"scripts":{"typecheck":"vue-tsc --noEmit -p tsconfig.json","test":"vp test run src"}`；workspace 依赖用 `"workspace:*"`。
- **tsconfig**：每个 package/app 的 `tsconfig.json` 用 `{"extends":"../../tsconfig.base.json"}`（app 在 `apps/*`、包在 `packages/*`，相对根都是 `../../`）。
- **别名登记**：新 `@nerv-iip/*` 必须同时加入 `frontend/tsconfig.base.json` 的 `paths` 与消费方 `vite.config.ts` 的 `resolve.alias`。
- **样式**：token 不重复，只 `@import` `packages/ui/src/styles/theme.css`；Tailwind v4 用 `@tailwindcss/vite`，无 config 文件。
- **主题复用**：`import { initTheme } from '@nerv-iip/ui'` 在 `main.ts` mount 前调用；`useColorMode`/`useThemeAccent` 复用同机制。
- **测试**：`vp test run src`（vitest globals + jsdom）；组件测试用 `@vue/test-utils` 的 `mount`；composable/数据测试 mock `@nerv-iip/api-client` 和 `@pinia/colada`（样式见 `frontend/apps/business-console/src/composables/useBusinessEquipment.test.ts`）。
- **单测单文件命令**：`pnpm -C frontend --filter <pkgName> exec vp test run <相对 src 的路径>`。
- **门禁三连**：`pnpm -C frontend --filter <pkgName> typecheck`、`... test`、（app 才有）`... build`。
- **UI 禁忌**：界面不得出现 operationId/sourceSystem/code/policy/demo/seed/mock/issue 号；不做假数据/假分页（同 PC 金标准）。

## 文件结构地图

```
docs/architecture/mobile-pda-module-product-design.md      # 新建：PDA 模块产品业务文档
docs/architecture/frontend-navigation-map.md               # 更新：PDA v1 任务地图
docs/architecture/frontend-structure.md                    # 更新：新增 apps/packages 边界
docs/architecture/implementation-readiness.md              # 更新：移动端/PDA 实施轨与端口
frontend/tsconfig.base.json                                # 更新：新增 3 条 paths

frontend/packages/business-core/
  package.json  tsconfig.json
  src/index.ts                                             # barrel
  src/sop/defineStepFlow.ts        + defineStepFlow.test.ts
  src/tasks/pdaTaskKinds.ts        + pdaTaskKinds.test.ts

frontend/packages/ui-mobile/
  package.json  tsconfig.json
  src/index.ts                                             # barrel
  src/lib/utils.ts                                         # 复用 @nerv-iip/ui 的 cn
  src/styles/mobile.css                                    # 安全区 @utility + 触控基线
  src/components/app-shell-mobile/AppShellMobile.vue  + .test.ts
  src/components/scan-bar/ScanBar.vue                 + .test.ts
  src/components/list-row/ListRow.vue                 + .test.ts
  src/components/bottom-sheet/BottomSheet.vue         + .test.ts
  src/components/result/Result.vue                    + .test.ts

frontend/apps/business-pda/
  package.json  tsconfig.json  index.html  vite.config.ts  capacitor.config.ts
  src/main.ts  src/App.vue
  src/assets/main.css
  src/router/index.ts  src/router/guards/auth.ts
  src/api/auth.ts  src/api/unauthorized.ts
  src/stores/auth.ts
  src/test/setup.ts
  src/pages/login.vue
  src/pages/index.vue                                      # 首页：扫码条 + 我的任务占位 + 应用墙
```

---

## Phase 0 — 文档先行（先文档后代码铁律）

### Task 1: 新建 PDA 模块产品业务文档 + 更新架构文档

**Files:**
- Create: `docs/architecture/mobile-pda-module-product-design.md`
- Modify: `docs/architecture/frontend-navigation-map.md`（"用户导航形态" 表 PDA 行附近）
- Modify: `docs/architecture/frontend-structure.md`（apps/packages 清单）
- Modify: `docs/architecture/implementation-readiness.md`（"当前初步使用方式" 端口段）

- [ ] **Step 1: 写模块产品业务文档**

新建 `docs/architecture/mobile-pda-module-product-design.md`，内容至少含以下小节（正文用中文业务语言，事实引用 spec `docs/superpowers/specs/2026-06-09-mobile-pda-design.md`）：

```markdown
# 移动端（PDA）模块产品业务文档

> 事实源：docs/superpowers/specs/2026-06-09-mobile-pda-design.md。
> 后端能力以 BusinessGateway facade 代码事实为准。

## 1. 用户与场景
一线操作员手持工业 PDA（键盘楔入扫码），完成 WMS（收货/上架/拣货/复核/盘点）与
MES（报工/领料/完工入库/工序执行）作业，外加轻量设备报修/点检。

## 2. 信息架构（任务范式，非菜单树）
首页 = 常驻扫码条 + 我的任务 + 快捷应用墙；扫码结果分流到对应作业页。
对象详情/动作表单/页内 Tabs 不作为常驻菜单项。

## 3. 角色与权限
默认可见：我的任务、扫码直达、应用墙。以 Gateway per-request enforcement 为权威，
前端裁剪只作 UX。

## 4. 分期
M0 ui-mobile 地基 → M1 PDA 壳 → M2 WMS → M3 MES → M4 设备 → M5 扫码解析。

## 5. 后端缺口
见 spec §9（WMS 拣货/上架/盘点缺 list、"我的任务"个人过滤、扫码 resolve）。
整批 consolidated issue 落地后在此回填 issue 号。

## 6. 验收
每页过"产品·业务·UX"三关；UI 无工程语言、无假数据/假分页；touched 范围门禁三连。
```

- [ ] **Step 2: 更新导航地图 PDA 段**

在 `docs/architecture/frontend-navigation-map.md` 的 "用户导航形态" 表 `PDA/mobile` 行下，补一段引用：

```markdown
> PDA v1 任务地图与组件/UX 标准见 `docs/architecture/mobile-pda-module-product-design.md`
> 与 `docs/superpowers/specs/2026-06-09-mobile-pda-design.md`。实现轨为独立 app
> `frontend/apps/business-pda`，不复用 PC 菜单树。
```

- [ ] **Step 3: 更新 frontend-structure 与 readiness**

在 `docs/architecture/frontend-structure.md` 的 apps/packages 清单中新增条目：`apps/business-pda`（PDA 一线作业，Capacitor APK）、`packages/ui-mobile`（触摸组件层）、`packages/business-core`（同源 SOP/字典/类型）；并标注 `business-workstation`/`business-board` 为 roadmap 预留。
在 `docs/architecture/implementation-readiness.md` "当前初步使用方式" 端口段补一句：`business-pda` 本地 dev 端口建议 `5126`（待 `nerv.ps1 ports` 矩阵确认），移动端为独立实施轨，详见 PDA 模块产品文档与 spec。

- [ ] **Step 4: Commit**

```bash
git add docs/architecture/mobile-pda-module-product-design.md docs/architecture/frontend-navigation-map.md docs/architecture/frontend-structure.md docs/architecture/implementation-readiness.md docs/superpowers/specs/2026-06-09-mobile-pda-design.md docs/superpowers/plans/2026-06-09-mobile-pda-foundation.md
git commit -m "docs(pda): add mobile PDA module product design + spec/plan, register apps/packages"
```

---

## Phase 1 — `business-core` 同源内核骨架

### Task 2: 创建 `@nerv-iip/business-core` 包骨架

**Files:**
- Create: `frontend/packages/business-core/package.json`
- Create: `frontend/packages/business-core/tsconfig.json`
- Create: `frontend/packages/business-core/src/index.ts`
- Modify: `frontend/tsconfig.base.json`

- [ ] **Step 1: 写 package.json**

`frontend/packages/business-core/package.json`：

```json
{
  "name": "@nerv-iip/business-core",
  "version": "0.1.0",
  "private": true,
  "type": "module",
  "exports": {
    ".": "./src/index.ts"
  },
  "scripts": {
    "typecheck": "vue-tsc --noEmit -p tsconfig.json",
    "test": "vp test run src"
  },
  "dependencies": {
    "vue": "3.5.34"
  },
  "devDependencies": {
    "@vue/test-utils": "2.4.10",
    "jsdom": "29.1.1",
    "vitest": "4.1.6"
  }
}
```

- [ ] **Step 2: 写 tsconfig.json**

`frontend/packages/business-core/tsconfig.json`：

```json
{
  "extends": "../../tsconfig.base.json",
  "include": ["src"]
}
```

- [ ] **Step 3: 写占位 barrel**

`frontend/packages/business-core/src/index.ts`：

```typescript
export { defineStepFlow } from './sop/defineStepFlow'
export type { StepFlow, StepFlowStep, StepFlowContext } from './sop/defineStepFlow'
export { PDA_TASK_KINDS, getPdaTaskKind } from './tasks/pdaTaskKinds'
export type { PdaTaskKind } from './tasks/pdaTaskKinds'
```

- [ ] **Step 4: 登记 tsconfig.base.json paths**

在 `frontend/tsconfig.base.json` 的 `compilerOptions.paths` 中新增三条（与现有 `@nerv-iip/ui` 等并列）：

```json
"@nerv-iip/business-core": ["packages/business-core/src/index.ts"],
"@nerv-iip/ui-mobile": ["packages/ui-mobile/src/index.ts"],
"@nerv-iip/business-pda": ["apps/business-pda/src/index.ts"]
```

- [ ] **Step 5: 安装依赖**

Run: `pnpm -C frontend install`
Expected: 新增 workspace 包被识别，无 lockfile 报错。

### Task 3: `defineStepFlow` SOP 状态机原语

**Files:**
- Create: `frontend/packages/business-core/src/sop/defineStepFlow.ts`
- Test: `frontend/packages/business-core/src/sop/defineStepFlow.test.ts`

- [ ] **Step 1: 写失败测试**

`frontend/packages/business-core/src/sop/defineStepFlow.test.ts`：

```typescript
import { describe, expect, it } from 'vitest'
import { defineStepFlow } from './defineStepFlow'

interface ReceiveCtx {
  order?: string
  sku?: string
  qty?: number
}

describe('defineStepFlow', () => {
  const flow = defineStepFlow<ReceiveCtx>({
    id: 'receive',
    steps: [
      { id: 'scanOrder', done: (c) => Boolean(c.order) },
      { id: 'scanSku', done: (c) => Boolean(c.sku) },
      { id: 'confirmQty', done: (c) => typeof c.qty === 'number' && c.qty > 0 },
    ],
  })

  it('starts at the first incomplete step', () => {
    expect(flow.currentStep({}).id).toBe('scanOrder')
    expect(flow.currentStep({ order: 'RO-1' }).id).toBe('scanSku')
  })

  it('reports completion only when every step is done', () => {
    expect(flow.isComplete({ order: 'RO-1', sku: 'S1' })).toBe(false)
    expect(flow.isComplete({ order: 'RO-1', sku: 'S1', qty: 5 })).toBe(true)
  })

  it('exposes ordered progress for the UI step indicator', () => {
    expect(flow.progress({ order: 'RO-1' })).toEqual({ completed: 1, total: 3 })
  })
})
```

- [ ] **Step 2: 跑测试确认失败**

Run: `pnpm -C frontend --filter @nerv-iip/business-core exec vp test run src/sop/defineStepFlow.test.ts`
Expected: FAIL（`defineStepFlow` 未定义）。

- [ ] **Step 3: 写实现**

`frontend/packages/business-core/src/sop/defineStepFlow.ts`：

```typescript
export interface StepFlowContext {
  [key: string]: unknown
}

export interface StepFlowStep<TCtx> {
  id: string
  /** 该步是否已完成（数据驱动，不在多处散落流程逻辑）。 */
  done: (ctx: TCtx) => boolean
}

export interface StepFlow<TCtx> {
  id: string
  steps: StepFlowStep<TCtx>[]
  currentStep: (ctx: TCtx) => StepFlowStep<TCtx>
  isComplete: (ctx: TCtx) => boolean
  progress: (ctx: TCtx) => { completed: number; total: number }
}

export function defineStepFlow<TCtx extends StepFlowContext>(config: {
  id: string
  steps: StepFlowStep<TCtx>[]
}): StepFlow<TCtx> {
  const { id, steps } = config
  return {
    id,
    steps,
    currentStep: (ctx) => steps.find((s) => !s.done(ctx)) ?? steps[steps.length - 1],
    isComplete: (ctx) => steps.every((s) => s.done(ctx)),
    progress: (ctx) => ({
      completed: steps.filter((s) => s.done(ctx)).length,
      total: steps.length,
    }),
  }
}
```

- [ ] **Step 4: 跑测试确认通过**

Run: `pnpm -C frontend --filter @nerv-iip/business-core exec vp test run src/sop/defineStepFlow.test.ts`
Expected: PASS（3 个用例）。

- [ ] **Step 5: Commit**

```bash
git add frontend/packages/business-core frontend/tsconfig.base.json frontend/pnpm-lock.yaml
git commit -m "feat(business-core): scaffold package + defineStepFlow SOP primitive"
```

### Task 4: `PDA_TASK_KINDS` 任务字典

**Files:**
- Create: `frontend/packages/business-core/src/tasks/pdaTaskKinds.ts`
- Test: `frontend/packages/business-core/src/tasks/pdaTaskKinds.test.ts`

- [ ] **Step 1: 写失败测试**

`frontend/packages/business-core/src/tasks/pdaTaskKinds.test.ts`：

```typescript
import { describe, expect, it } from 'vitest'
import { PDA_TASK_KINDS, getPdaTaskKind } from './pdaTaskKinds'

describe('PDA task kinds dictionary', () => {
  it('covers the v1 frontline tasks with Chinese labels and route targets', () => {
    const ids = PDA_TASK_KINDS.map((k) => k.id)
    expect(ids).toEqual(
      expect.arrayContaining(['wms.inbound', 'wms.putaway', 'wms.pick', 'wms.count', 'mes.report']),
    )
    expect(getPdaTaskKind('wms.inbound')).toMatchObject({ label: '收货入库', route: '/wms/inbound' })
  })

  it('marks not-yet-implemented tasks so the app wall can disable them (no fake links)', () => {
    expect(getPdaTaskKind('wms.pick')?.routeReady).toBe(false)
    expect(getPdaTaskKind('mes.report')?.routeReady).toBe(false)
  })

  it('returns undefined for unknown ids', () => {
    expect(getPdaTaskKind('nope')).toBeUndefined()
  })
})
```

- [ ] **Step 2: 跑测试确认失败**

Run: `pnpm -C frontend --filter @nerv-iip/business-core exec vp test run src/tasks/pdaTaskKinds.test.ts`
Expected: FAIL。

- [ ] **Step 3: 写实现**

`frontend/packages/business-core/src/tasks/pdaTaskKinds.ts`（`routeReady:false` 表示页面在后续 Plan 才落地，应用墙据此 disable，不做空跳转）：

```typescript
export interface PdaTaskKind {
  id: string
  label: string
  group: 'wms' | 'mes' | 'equipment'
  route: string
  /** 对应作业页是否已落地；false 时应用墙入口 disabled。 */
  routeReady: boolean
}

export const PDA_TASK_KINDS: PdaTaskKind[] = [
  { id: 'wms.inbound', label: '收货入库', group: 'wms', route: '/wms/inbound', routeReady: false },
  { id: 'wms.putaway', label: '上架', group: 'wms', route: '/wms/putaway', routeReady: false },
  { id: 'wms.pick', label: '拣货', group: 'wms', route: '/wms/pick', routeReady: false },
  { id: 'wms.review', label: '复核发货', group: 'wms', route: '/wms/review', routeReady: false },
  { id: 'wms.count', label: '盘点', group: 'wms', route: '/wms/count', routeReady: false },
  { id: 'mes.report', label: '报工', group: 'mes', route: '/mes/report', routeReady: false },
  { id: 'mes.issue', label: '领料', group: 'mes', route: '/mes/issue', routeReady: false },
  { id: 'mes.receipt', label: '完工入库', group: 'mes', route: '/mes/receipt', routeReady: false },
  { id: 'mes.operation', label: '工序执行', group: 'mes', route: '/mes/operation', routeReady: false },
  { id: 'equipment.repair', label: '报修', group: 'equipment', route: '/equipment/repair', routeReady: false },
  { id: 'equipment.inspect', label: '点检', group: 'equipment', route: '/equipment/inspect', routeReady: false },
]

const byId = new Map(PDA_TASK_KINDS.map((k) => [k.id, k]))

export function getPdaTaskKind(id: string): PdaTaskKind | undefined {
  return byId.get(id)
}
```

- [ ] **Step 4: 跑测试确认通过**

Run: `pnpm -C frontend --filter @nerv-iip/business-core exec vp test run src/tasks/pdaTaskKinds.test.ts`
Expected: PASS。

- [ ] **Step 5: 包级门禁 + Commit**

Run: `pnpm -C frontend --filter @nerv-iip/business-core typecheck` → Expected: PASS
Run: `pnpm -C frontend --filter @nerv-iip/business-core test` → Expected: PASS（全部用例）

```bash
git add frontend/packages/business-core/src/tasks
git commit -m "feat(business-core): add PDA task-kind dictionary with route-ready flags"
```

---

## Phase 2 — `@nerv-iip/ui-mobile` 包骨架与安全区基线

### Task 5: 创建 `@nerv-iip/ui-mobile` 包 + 安全区工具类

**Files:**
- Create: `frontend/packages/ui-mobile/package.json`
- Create: `frontend/packages/ui-mobile/tsconfig.json`
- Create: `frontend/packages/ui-mobile/src/index.ts`
- Create: `frontend/packages/ui-mobile/src/lib/utils.ts`
- Create: `frontend/packages/ui-mobile/src/styles/mobile.css`

- [ ] **Step 1: 写 package.json**

`frontend/packages/ui-mobile/package.json`：

```json
{
  "name": "@nerv-iip/ui-mobile",
  "version": "0.1.0",
  "private": true,
  "type": "module",
  "exports": {
    ".": "./src/index.ts",
    "./styles/mobile.css": "./src/styles/mobile.css"
  },
  "scripts": {
    "typecheck": "vue-tsc --noEmit -p tsconfig.json",
    "test": "vp test run src"
  },
  "dependencies": {
    "@nerv-iip/ui": "workspace:*",
    "@vueuse/core": "^14.3.0",
    "lucide-vue-next": "1.0.0",
    "reka-ui": "^2.9.7",
    "vue": "3.5.34"
  },
  "devDependencies": {
    "@vitejs/plugin-vue": "6.0.7",
    "@vue/test-utils": "2.4.10",
    "jsdom": "29.1.1",
    "vitest": "4.1.6"
  }
}
```

- [ ] **Step 2: 写 tsconfig.json**

`frontend/packages/ui-mobile/tsconfig.json`：

```json
{
  "extends": "../../tsconfig.base.json",
  "include": ["src"]
}
```

- [ ] **Step 3: 写 cn 复用**

`frontend/packages/ui-mobile/src/lib/utils.ts`（复用 `@nerv-iip/ui` 的 `cn`，不另造一套）：

```typescript
export { cn } from '@nerv-iip/ui'
```

- [ ] **Step 4: 写安全区/触控基线 CSS**

`frontend/packages/ui-mobile/src/styles/mobile.css`（Tailwind v4 `@utility`；app 的 main.css 会 `@import` 它）：

```css
/*
 * Nerv-IIP 移动端基线（@nerv-iip/ui-mobile）。
 * 安全区工具类统一注入顶/底/左右；触控密度变量。token 仍来自 @nerv-iip/ui 的 theme.css。
 */

@utility pt-safe {
  padding-top: max(0.75rem, env(safe-area-inset-top));
}
@utility pb-safe {
  padding-bottom: max(0.5rem, env(safe-area-inset-bottom));
}
@utility px-safe {
  padding-left: env(safe-area-inset-left);
  padding-right: env(safe-area-inset-right);
}
@utility min-h-touch {
  min-height: 48px;
}
@utility min-h-row {
  min-height: 56px;
}
```

- [ ] **Step 5: 写占位 barrel**

`frontend/packages/ui-mobile/src/index.ts`：

```typescript
export { cn } from './lib/utils'
export { default as AppShellMobile } from './components/app-shell-mobile/AppShellMobile.vue'
export { default as ScanBar } from './components/scan-bar/ScanBar.vue'
export { default as ListRow } from './components/list-row/ListRow.vue'
export { default as BottomSheet } from './components/bottom-sheet/BottomSheet.vue'
export { default as Result } from './components/result/Result.vue'
```

> 注：此时 barrel 引用的 .vue 还未建，typecheck 会失败——下一 Task 起逐个补齐后再跑包级门禁。

- [ ] **Step 6: 安装依赖**

Run: `pnpm -C frontend install`
Expected: `@nerv-iip/ui-mobile` 被识别为 workspace 包。

### Task 6: `AppShellMobile` 壳组件（三段安全区）

**Files:**
- Create: `frontend/packages/ui-mobile/src/components/app-shell-mobile/AppShellMobile.vue`
- Test: `frontend/packages/ui-mobile/src/components/app-shell-mobile/AppShellMobile.test.ts`

- [ ] **Step 1: 写失败测试**

`AppShellMobile.test.ts`：

```typescript
import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import AppShellMobile from './AppShellMobile.vue'

describe('AppShellMobile', () => {
  it('renders header / content / footer slots', () => {
    const wrapper = mount(AppShellMobile, {
      slots: { header: '<div>标题栏</div>', default: '<div>内容</div>', footer: '<nav>底部导航</nav>' },
    })
    expect(wrapper.text()).toContain('标题栏')
    expect(wrapper.text()).toContain('内容')
    expect(wrapper.text()).toContain('底部导航')
  })

  it('applies top safe-area on header and bottom safe-area on footer', () => {
    const wrapper = mount(AppShellMobile, {
      slots: { header: '<div>H</div>', footer: '<div>F</div>' },
    })
    expect(wrapper.get('[data-shell="header"]').classes()).toContain('pt-safe')
    expect(wrapper.get('[data-shell="footer"]').classes()).toContain('pb-safe')
  })

  it('omits footer region when no footer slot is provided', () => {
    const wrapper = mount(AppShellMobile, { slots: { default: '<div>X</div>' } })
    expect(wrapper.find('[data-shell="footer"]').exists()).toBe(false)
  })
})
```

- [ ] **Step 2: 跑测试确认失败**

Run: `pnpm -C frontend --filter @nerv-iip/ui-mobile exec vp test run src/components/app-shell-mobile/AppShellMobile.test.ts`
Expected: FAIL。

- [ ] **Step 3: 写实现**

`AppShellMobile.vue`：

```vue
<script setup lang="ts">
import { useSlots } from 'vue'
import { cn } from '../../lib/utils'

defineProps<{ class?: string }>()
const slots = useSlots()
</script>

<template>
  <div :class="cn('flex h-dvh flex-col bg-background text-foreground', $props.class)">
    <header
      v-if="slots.header"
      data-shell="header"
      class="pt-safe px-safe sticky top-0 z-20 shrink-0 border-b border-border bg-background"
    >
      <slot name="header" />
    </header>

    <main data-shell="content" class="px-safe min-h-0 flex-1 overflow-y-auto">
      <slot />
    </main>

    <footer
      v-if="slots.footer"
      data-shell="footer"
      class="pb-safe px-safe sticky bottom-0 z-20 shrink-0 border-t border-border bg-background"
    >
      <slot name="footer" />
    </footer>
  </div>
</template>
```

- [ ] **Step 4: 跑测试确认通过**

Run: `pnpm -C frontend --filter @nerv-iip/ui-mobile exec vp test run src/components/app-shell-mobile/AppShellMobile.test.ts`
Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add frontend/packages/ui-mobile
git commit -m "feat(ui-mobile): scaffold package + AppShellMobile with safe-area regions"
```

### Task 7: `ScanBar` 扫码焦点条（键盘楔入）

**Files:**
- Create: `frontend/packages/ui-mobile/src/components/scan-bar/ScanBar.vue`
- Test: `frontend/packages/ui-mobile/src/components/scan-bar/ScanBar.test.ts`

- [ ] **Step 1: 写失败测试**

`ScanBar.test.ts`（扫码枪以快速键入 + 回车结束；组件捕获并 `emit('scan', value)`，随后清空）：

```typescript
import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import ScanBar from './ScanBar.vue'

describe('ScanBar', () => {
  it('emits scan with the buffered value on Enter and clears the input', async () => {
    const wrapper = mount(ScanBar)
    const input = wrapper.get('input')
    await input.setValue('SKU-12345')
    await input.trigger('keydown', { key: 'Enter' })

    expect(wrapper.emitted('scan')).toBeTruthy()
    expect(wrapper.emitted('scan')![0]).toEqual(['SKU-12345'])
    expect((input.element as HTMLInputElement).value).toBe('')
  })

  it('ignores Enter on an empty buffer', async () => {
    const wrapper = mount(ScanBar)
    await wrapper.get('input').trigger('keydown', { key: 'Enter' })
    expect(wrapper.emitted('scan')).toBeFalsy()
  })

  it('renders the provided placeholder', () => {
    const wrapper = mount(ScanBar, { props: { placeholder: '扫描库位或物料' } })
    expect(wrapper.get('input').attributes('placeholder')).toBe('扫描库位或物料')
  })
})
```

- [ ] **Step 2: 跑测试确认失败**

Run: `pnpm -C frontend --filter @nerv-iip/ui-mobile exec vp test run src/components/scan-bar/ScanBar.test.ts`
Expected: FAIL。

- [ ] **Step 3: 写实现**

`ScanBar.vue`（自动聚焦 + 失焦自动重聚焦；回车提交去空白；trim 去重交给消费方）：

```vue
<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { ScanLine } from 'lucide-vue-next'
import { cn } from '../../lib/utils'

withDefaults(defineProps<{ placeholder?: string; class?: string; autofocus?: boolean }>(), {
  placeholder: '扫描条码 / 二维码',
  autofocus: true,
})
const emit = defineEmits<{ scan: [value: string] }>()

const inputEl = ref<HTMLInputElement>()
const buffer = ref('')

function submit() {
  const value = buffer.value.trim()
  if (!value) return
  emit('scan', value)
  buffer.value = ''
}

function refocus() {
  // 键盘楔入设备需要输入框始终持有焦点
  requestAnimationFrame(() => inputEl.value?.focus())
}

onMounted(() => {
  if (inputEl.value && (inputEl.value as HTMLInputElement).autofocus !== false) refocus()
})
</script>

<template>
  <div :class="cn('flex items-center gap-2 rounded-lg border border-border bg-card px-3 min-h-touch', $props.class)">
    <ScanLine class="size-5 shrink-0 text-brand" aria-hidden="true" />
    <input
      ref="inputEl"
      v-model="buffer"
      type="text"
      inputmode="none"
      autocomplete="off"
      autocapitalize="off"
      spellcheck="false"
      :placeholder="placeholder"
      class="w-full bg-transparent py-2 text-base outline-none placeholder:text-muted-foreground"
      @keydown.enter.prevent="submit"
      @blur="refocus"
    />
  </div>
</template>
```

- [ ] **Step 4: 跑测试确认通过**

Run: `pnpm -C frontend --filter @nerv-iip/ui-mobile exec vp test run src/components/scan-bar/ScanBar.test.ts`
Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add frontend/packages/ui-mobile/src/components/scan-bar
git commit -m "feat(ui-mobile): add ScanBar keyboard-wedge scan input"
```

### Task 8: `ListRow` 大行列表项

**Files:**
- Create: `frontend/packages/ui-mobile/src/components/list-row/ListRow.vue`
- Test: `frontend/packages/ui-mobile/src/components/list-row/ListRow.test.ts`

- [ ] **Step 1: 写失败测试**

`ListRow.test.ts`：

```typescript
import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import ListRow from './ListRow.vue'

describe('ListRow', () => {
  it('renders title and subtitle', () => {
    const wrapper = mount(ListRow, { props: { title: 'RO-2026-001', subtitle: '待收货 · 3 行' } })
    expect(wrapper.text()).toContain('RO-2026-001')
    expect(wrapper.text()).toContain('待收货 · 3 行')
  })

  it('emits select when activated and meets the touch height baseline', async () => {
    const wrapper = mount(ListRow, { props: { title: 'X' } })
    expect(wrapper.get('[data-row]').classes()).toContain('min-h-row')
    await wrapper.get('[data-row]').trigger('click')
    expect(wrapper.emitted('select')).toBeTruthy()
  })

  it('shows the chevron only when interactive', () => {
    const plain = mount(ListRow, { props: { title: 'X', interactive: false } })
    expect(plain.find('[data-chevron]').exists()).toBe(false)
  })
})
```

- [ ] **Step 2: 跑测试确认失败**

Run: `pnpm -C frontend --filter @nerv-iip/ui-mobile exec vp test run src/components/list-row/ListRow.test.ts`
Expected: FAIL。

- [ ] **Step 3: 写实现**

`ListRow.vue`：

```vue
<script setup lang="ts">
import { ChevronRight } from 'lucide-vue-next'
import { cn } from '../../lib/utils'

withDefaults(defineProps<{ title: string; subtitle?: string; interactive?: boolean; class?: string }>(), {
  interactive: true,
})
const emit = defineEmits<{ select: [] }>()
</script>

<template>
  <div
    data-row
    :role="interactive ? 'button' : undefined"
    :tabindex="interactive ? 0 : undefined"
    :class="cn(
      'min-h-row flex w-full items-center gap-3 border-b border-border bg-card px-4 py-3 text-left',
      interactive && 'active:bg-accent',
      $props.class,
    )"
    @click="interactive && emit('select')"
    @keydown.enter="interactive && emit('select')"
  >
    <div class="min-w-0 flex-1">
      <div class="truncate text-base font-medium text-foreground">{{ title }}</div>
      <div v-if="subtitle" class="truncate text-sm text-muted-foreground">{{ subtitle }}</div>
      <slot name="meta" />
    </div>
    <slot name="trailing" />
    <ChevronRight v-if="interactive" data-chevron class="size-5 shrink-0 text-muted-foreground" aria-hidden="true" />
  </div>
</template>
```

- [ ] **Step 4: 跑测试确认通过**

Run: `pnpm -C frontend --filter @nerv-iip/ui-mobile exec vp test run src/components/list-row/ListRow.test.ts`
Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add frontend/packages/ui-mobile/src/components/list-row
git commit -m "feat(ui-mobile): add ListRow touch list item"
```

### Task 9: `BottomSheet` 底部抽屉（基于 reka-ui Dialog）

**Files:**
- Create: `frontend/packages/ui-mobile/src/components/bottom-sheet/BottomSheet.vue`
- Test: `frontend/packages/ui-mobile/src/components/bottom-sheet/BottomSheet.test.ts`

- [ ] **Step 1: 写失败测试**

`BottomSheet.test.ts`（用 `v-model:open` 控制；reka-ui Dialog 用 Teleport 渲染到 body，断言查 `document.body`）：

```typescript
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
```

- [ ] **Step 2: 跑测试确认失败**

Run: `pnpm -C frontend --filter @nerv-iip/ui-mobile exec vp test run src/components/bottom-sheet/BottomSheet.test.ts`
Expected: FAIL。

- [ ] **Step 3: 写实现**

`BottomSheet.vue`（直接组合 reka-ui Dialog 原语；底部滑入；含拖拽手柄视觉；`useBodyScrollLock` 已由 reka-ui 内部处理）：

```vue
<script setup lang="ts">
import {
  DialogRoot,
  DialogPortal,
  DialogOverlay,
  DialogContent,
  DialogTitle,
} from 'reka-ui'
import { cn } from '../../lib/utils'

defineProps<{ open: boolean; title?: string; class?: string }>()
const emit = defineEmits<{ 'update:open': [value: boolean] }>()
</script>

<template>
  <DialogRoot :open="open" @update:open="emit('update:open', $event)">
    <DialogPortal>
      <DialogOverlay class="fixed inset-0 z-40 bg-black/50" />
      <DialogContent
        :class="cn(
          'fixed inset-x-0 bottom-0 z-50 flex max-h-[85dvh] flex-col rounded-t-2xl border-t border-border bg-card pb-safe',
          $props.class,
        )"
      >
        <div class="mx-auto mt-2 h-1.5 w-10 shrink-0 rounded-full bg-muted" aria-hidden="true" />
        <DialogTitle v-if="title" class="px-4 py-3 text-base font-semibold text-foreground">
          {{ title }}
        </DialogTitle>
        <div class="min-h-0 flex-1 overflow-y-auto px-4 pb-4">
          <slot />
        </div>
      </DialogContent>
    </DialogPortal>
  </DialogRoot>
</template>
```

- [ ] **Step 4: 跑测试确认通过**

Run: `pnpm -C frontend --filter @nerv-iip/ui-mobile exec vp test run src/components/bottom-sheet/BottomSheet.test.ts`
Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add frontend/packages/ui-mobile/src/components/bottom-sheet
git commit -m "feat(ui-mobile): add BottomSheet built on reka-ui dialog"
```

### Task 10: `Result` 结果页（操作闭环大反馈）

**Files:**
- Create: `frontend/packages/ui-mobile/src/components/result/Result.vue`
- Test: `frontend/packages/ui-mobile/src/components/result/Result.test.ts`

- [ ] **Step 1: 写失败测试**

`Result.test.ts`：

```typescript
import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import Result from './Result.vue'

describe('Result', () => {
  it('renders success state with title and description', () => {
    const wrapper = mount(Result, {
      props: { status: 'success', title: '过账成功', description: '收货单 RO-1 已完成' },
    })
    expect(wrapper.get('[data-result]').attributes('data-status')).toBe('success')
    expect(wrapper.text()).toContain('过账成功')
    expect(wrapper.text()).toContain('收货单 RO-1 已完成')
  })

  it('renders error state and the actions slot', () => {
    const wrapper = mount(Result, {
      props: { status: 'error', title: '过账失败' },
      slots: { actions: '<button>重试</button>' },
    })
    expect(wrapper.get('[data-result]').attributes('data-status')).toBe('error')
    expect(wrapper.text()).toContain('重试')
  })
})
```

- [ ] **Step 2: 跑测试确认失败**

Run: `pnpm -C frontend --filter @nerv-iip/ui-mobile exec vp test run src/components/result/Result.test.ts`
Expected: FAIL。

- [ ] **Step 3: 写实现**

`Result.vue`：

```vue
<script setup lang="ts">
import { computed } from 'vue'
import { CircleCheck, CircleX } from 'lucide-vue-next'
import { cn } from '../../lib/utils'

const props = defineProps<{ status: 'success' | 'error'; title: string; description?: string; class?: string }>()
const tone = computed(() =>
  props.status === 'success' ? 'text-success' : 'text-destructive',
)
</script>

<template>
  <div
    data-result
    :data-status="status"
    :class="cn('flex flex-col items-center justify-center gap-4 px-6 py-10 text-center', $props.class)"
  >
    <component :is="status === 'success' ? CircleCheck : CircleX" :class="cn('size-16', tone)" aria-hidden="true" />
    <div class="space-y-1">
      <h2 class="text-xl font-semibold text-foreground">{{ title }}</h2>
      <p v-if="description" class="text-sm text-muted-foreground">{{ description }}</p>
    </div>
    <div v-if="$slots.actions" class="w-full max-w-xs space-y-2 pt-2">
      <slot name="actions" />
    </div>
  </div>
</template>
```

- [ ] **Step 4: 跑测试确认通过**

Run: `pnpm -C frontend --filter @nerv-iip/ui-mobile exec vp test run src/components/result/Result.test.ts`
Expected: PASS。

- [ ] **Step 5: 包级门禁 + Commit**

Run: `pnpm -C frontend --filter @nerv-iip/ui-mobile typecheck` → Expected: PASS（barrel 引用的 5 个组件均已存在）
Run: `pnpm -C frontend --filter @nerv-iip/ui-mobile test` → Expected: PASS（全部组件用例）

```bash
git add frontend/packages/ui-mobile/src/components/result
git commit -m "feat(ui-mobile): add Result feedback screen; ui-mobile foundation complete"
```

---

## Phase 3 — `business-pda` 应用壳

### Task 11: 应用脚手架（config / 入口 / 样式 / App）

**Files:**
- Create: `frontend/apps/business-pda/package.json`
- Create: `frontend/apps/business-pda/tsconfig.json`
- Create: `frontend/apps/business-pda/index.html`
- Create: `frontend/apps/business-pda/vite.config.ts`
- Create: `frontend/apps/business-pda/src/assets/main.css`
- Create: `frontend/apps/business-pda/src/App.vue`
- Create: `frontend/apps/business-pda/src/test/setup.ts`

- [ ] **Step 1: 写 package.json**

`frontend/apps/business-pda/package.json`：

```json
{
  "name": "@nerv-iip/business-pda",
  "version": "0.1.0",
  "private": true,
  "type": "module",
  "scripts": {
    "dev": "vp dev --host 127.0.0.1 --port 5126",
    "build": "vue-tsc --noEmit -p tsconfig.json && vp build .",
    "test": "vp test run src",
    "typecheck": "vue-tsc --noEmit -p tsconfig.json"
  },
  "dependencies": {
    "@nerv-iip/api-client": "workspace:*",
    "@nerv-iip/business-core": "workspace:*",
    "@nerv-iip/ui": "workspace:*",
    "@nerv-iip/ui-mobile": "workspace:*",
    "@pinia/colada": "1.3.0",
    "@vueuse/core": "14.3.0",
    "lucide-vue-next": "1.0.0",
    "pinia": "3.0.4",
    "vue": "3.5.34",
    "vue-router": "5.0.7"
  }
}
```

- [ ] **Step 2: 写 tsconfig.json**

`frontend/apps/business-pda/tsconfig.json`：

```json
{
  "extends": "../../tsconfig.base.json",
  "include": ["src", "typed-router.d.ts"]
}
```

- [ ] **Step 3: 写 index.html（开启安全区计算）**

`frontend/apps/business-pda/index.html`：

```html
<!doctype html>
<html lang="zh-CN">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover" />
    <title>Nerv-IIP 手持作业台</title>
  </head>
  <body>
    <div id="app"></div>
    <script type="module" src="/src/main.ts"></script>
  </body>
</html>
```

- [ ] **Step 4: 写 vite.config.ts**

`frontend/apps/business-pda/vite.config.ts`（端口 5126；代理 BusinessGateway 5119；别名含 ui-mobile/business-core）：

```typescript
import { fileURLToPath, URL } from 'node:url'
import tailwindcss from '@tailwindcss/vite'
import Vue from '@vitejs/plugin-vue'
import VueRouter from 'unplugin-vue-router/vite'
import { defineConfig } from 'vite'

export default defineConfig({
  plugins: [
    tailwindcss(),
    VueRouter({
      routesFolder: [
        {
          src: 'src/pages',
          exclude: (excluded) =>
            excluded.concat(['**/components/**/*', '**/dialogs/**/*', '**/sheets/**/*']),
        },
      ],
      dts: 'typed-router.d.ts',
    }),
    Vue(),
  ],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
      '@nerv-iip/api-client': fileURLToPath(new URL('../../packages/api-client/src/index.ts', import.meta.url)),
      '@nerv-iip/ui': fileURLToPath(new URL('../../packages/ui/src/index.ts', import.meta.url)),
      '@nerv-iip/ui-mobile': fileURLToPath(new URL('../../packages/ui-mobile/src/index.ts', import.meta.url)),
      '@nerv-iip/business-core': fileURLToPath(new URL('../../packages/business-core/src/index.ts', import.meta.url)),
    },
  },
  server: {
    port: 5126,
    proxy: {
      '/api/business-console': {
        target: process.env.NERV_IIP_BUSINESS_GATEWAY_URL ?? 'http://127.0.0.1:5119',
        changeOrigin: true,
      },
      '/api/console': {
        target: process.env.NERV_IIP_PLATFORM_GATEWAY_URL ?? 'http://127.0.0.1:5100',
        changeOrigin: true,
      },
    },
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
  },
})
```

> 若 `unplugin-vue-router` 在 business-console 中是以其它导入名引入（核对 `frontend/apps/business-console/vite.config.ts` 顶部 import），照抄同名导入与版本，勿臆造。

- [ ] **Step 5: 写 main.css**

`frontend/apps/business-pda/src/assets/main.css`（token 来自 ui，安全区来自 ui-mobile）：

```css
@import 'tailwindcss';

@source '../../../../packages/ui/src';
@source '../../../../packages/ui-mobile/src';

@import 'tw-animate-css';
@import 'shadcn-vue/tailwind.css';

/* Design System v2 tokens — 单一源 */
@import '../../../../packages/ui/src/styles/theme.css';
/* 移动端安全区/触控基线 */
@import '../../../../packages/ui-mobile/src/styles/mobile.css';
```

- [ ] **Step 6: 写 App.vue + 测试 setup**

`frontend/apps/business-pda/src/App.vue`：

```vue
<script setup lang="ts">
import { RouterView } from 'vue-router'
</script>

<template>
  <RouterView />
</template>
```

`frontend/apps/business-pda/src/test/setup.ts`（照抄 business-console 的 localStorage polyfill）：

```typescript
import { enableAutoUnmount } from '@vue/test-utils'
import { afterEach } from 'vitest'

enableAutoUnmount(afterEach)

if (!globalThis.localStorage) {
  const storage = new Map<string, string>()
  Object.defineProperty(globalThis, 'localStorage', {
    configurable: true,
    value: {
      clear: () => storage.clear(),
      getItem: (key: string) => storage.get(key) ?? null,
      key: (index: number) => [...storage.keys()][index] ?? null,
      removeItem: (key: string) => storage.delete(key),
      setItem: (key: string, value: string) => storage.set(key, value),
      get length() {
        return storage.size
      },
    },
  })
}
```

- [ ] **Step 7: 安装并 commit**

Run: `pnpm -C frontend install` → Expected: business-pda 被识别。

```bash
git add frontend/apps/business-pda frontend/tsconfig.base.json frontend/pnpm-lock.yaml
git commit -m "chore(business-pda): scaffold app (vite/tailwind/router config + shell entry)"
```

### Task 12: 鉴权（store / api / guard / unauthorized）

**Files:**
- Create: `frontend/apps/business-pda/src/api/auth.ts`
- Create: `frontend/apps/business-pda/src/api/unauthorized.ts`
- Create: `frontend/apps/business-pda/src/stores/auth.ts`
- Create: `frontend/apps/business-pda/src/router/guards/auth.ts`
- Test: `frontend/apps/business-pda/src/stores/auth.test.ts`

> 复用 business-console 已验证的 console-auth 端点（`loginConsoleUser`/`getConsolePrincipal`/`refreshConsoleSession`/`logoutConsoleSession`）。本计划落地一个精简 store（login/logout/token provider/restore）；后续若 PC 与 PDA 需共享，再抽 `@nerv-iip/auth` 包（readiness 已预留）。

- [ ] **Step 1: 复制 api/auth.ts 与 unauthorized.ts**

把 `frontend/apps/business-console/src/api/auth.ts` 与 `frontend/apps/business-console/src/api/unauthorized.ts` 原样复制到 `frontend/apps/business-pda/src/api/` 下（两文件不依赖 business-console 私有内容，内容见约定核查报告第 7 项；逐字复制即可）。

- [ ] **Step 2: 写精简 auth store 测试**

`frontend/apps/business-pda/src/stores/auth.test.ts`：

```typescript
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'

vi.mock('@/api/auth', () => ({
  loginConsole: vi.fn(async () => ({
    accessToken: 'tok-1',
    refreshToken: 'r-1',
    sessionId: 's-1',
    expiresAtUtc: new Date(Date.now() + 600_000).toISOString(),
    principal: { loginName: 'op01' },
  })),
  logoutConsole: vi.fn(async () => {}),
  refreshConsole: vi.fn(),
  getConsoleMe: vi.fn(),
}))

import { useAuthStore } from './auth'

describe('pda auth store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
  })

  it('is unauthenticated initially', () => {
    expect(useAuthStore().isAuthenticated).toBe(false)
  })

  it('authenticates and exposes the access token after login', async () => {
    const auth = useAuthStore()
    await auth.login('op01', 'pw')
    expect(auth.isAuthenticated).toBe(true)
    expect(auth.accessToken).toBe('tok-1')
    expect(auth.displayName).toBe('op01')
  })

  it('clears the session on logout', async () => {
    const auth = useAuthStore()
    await auth.login('op01', 'pw')
    await auth.logout()
    expect(auth.isAuthenticated).toBe(false)
    expect(auth.accessToken).toBeUndefined()
  })
})
```

- [ ] **Step 3: 跑测试确认失败**

Run: `pnpm -C frontend --filter @nerv-iip/business-pda exec vp test run src/stores/auth.test.ts`
Expected: FAIL（store 未建）。

- [ ] **Step 4: 写精简 auth store**

`frontend/apps/business-pda/src/stores/auth.ts`：

```typescript
import { getConsoleMe, loginConsole, logoutConsole, refreshConsole } from '@/api/auth'
import type { ConsolePrincipalResponse } from '@nerv-iip/api-client'
import { defineStore } from 'pinia'
import { computed, shallowRef } from 'vue'

const STORAGE_KEY = 'nerv-iip.business-pda.auth'

interface StoredSession {
  refreshToken: string
  sessionId: string
  principal?: ConsolePrincipalResponse
}

export const useAuthStore = defineStore('pda-auth', () => {
  const accessToken = shallowRef<string>()
  const refreshToken = shallowRef<string>()
  const sessionId = shallowRef<string>()
  const principal = shallowRef<ConsolePrincipalResponse>()
  const restoreStatus = shallowRef<'idle' | 'restoring' | 'restored' | 'failed'>('idle')
  let sessionExpiredHandler: ((reason: string) => void) | undefined

  const isAuthenticated = computed(() => Boolean(accessToken.value && principal.value))
  const displayName = computed(() => principal.value?.loginName ?? '未知用户')

  function persist() {
    if (refreshToken.value && sessionId.value) {
      localStorage.setItem(
        STORAGE_KEY,
        JSON.stringify({ refreshToken: refreshToken.value, sessionId: sessionId.value, principal: principal.value } satisfies StoredSession),
      )
    }
  }

  function clearSession(_reason: string) {
    accessToken.value = undefined
    refreshToken.value = undefined
    sessionId.value = undefined
    principal.value = undefined
    localStorage.removeItem(STORAGE_KEY)
  }

  async function login(loginName: string, password: string) {
    const session = await loginConsole({ loginName, password })
    accessToken.value = session.accessToken ?? undefined
    refreshToken.value = session.refreshToken ?? undefined
    sessionId.value = session.sessionId ?? undefined
    principal.value = session.principal ?? undefined
    persist()
  }

  async function restoreSession() {
    restoreStatus.value = 'restoring'
    const raw = localStorage.getItem(STORAGE_KEY)
    if (!raw) {
      restoreStatus.value = 'failed'
      return
    }
    try {
      const stored = JSON.parse(raw) as StoredSession
      const session = await refreshConsole({ refreshToken: stored.refreshToken, sessionId: stored.sessionId })
      accessToken.value = session.accessToken ?? undefined
      refreshToken.value = session.refreshToken ?? undefined
      sessionId.value = session.sessionId ?? undefined
      principal.value = session.principal ?? (accessToken.value ? await getConsoleMe(accessToken.value) : undefined)
      persist()
      restoreStatus.value = 'restored'
    } catch {
      clearSession('restore-failed')
      restoreStatus.value = 'failed'
    }
  }

  async function logout() {
    if (accessToken.value && sessionId.value) {
      try {
        await logoutConsole(accessToken.value, { sessionId: sessionId.value })
      } catch {
        // 忽略登出网络错误，本地仍清会话
      }
    }
    clearSession('logout')
  }

  function setSessionExpiredHandler(handler: (reason: string) => void) {
    sessionExpiredHandler = handler
  }

  return {
    accessToken,
    principal,
    restoreStatus,
    isAuthenticated,
    displayName,
    login,
    logout,
    restoreSession,
    clearSession,
    setSessionExpiredHandler,
  }
})
```

> 核对 `@nerv-iip/api-client` 中 `ConsoleRefreshRequest`/`ConsoleLogoutRequest` 的真实字段名（约定核查报告第 7 项显示用 `{ refreshToken, sessionId }` / `{ sessionId }`）；若字段名不同，按 generated 类型调整。

- [ ] **Step 5: 写路由守卫**

`frontend/apps/business-pda/src/router/guards/auth.ts`：

```typescript
import { useAuthStore } from '@/stores/auth'
import type { Router } from 'vue-router'

declare module 'vue-router' {
  interface RouteMeta {
    guestOnly?: boolean
    requiresAuth?: boolean
    title?: string
  }
}

export function installAuthGuard(router: Router) {
  router.beforeEach(async (to) => {
    const auth = useAuthStore()
    if (auth.restoreStatus === 'idle') {
      await auth.restoreSession()
    }
    if (to.meta.requiresAuth && !auth.isAuthenticated) {
      return { path: '/login', query: { redirect: to.fullPath } }
    }
    if (to.meta.guestOnly && auth.isAuthenticated) {
      return '/'
    }
    return true
  })
}
```

- [ ] **Step 6: 跑测试确认通过**

Run: `pnpm -C frontend --filter @nerv-iip/business-pda exec vp test run src/stores/auth.test.ts`
Expected: PASS。

- [ ] **Step 7: Commit**

```bash
git add frontend/apps/business-pda/src/api frontend/apps/business-pda/src/stores frontend/apps/business-pda/src/router/guards
git commit -m "feat(business-pda): auth store/api + route guard reusing console auth"
```

### Task 13: router + main.ts（接 api-client / 主题 / colada）

**Files:**
- Create: `frontend/apps/business-pda/src/router/index.ts`
- Create: `frontend/apps/business-pda/src/main.ts`

- [ ] **Step 1: 写 router**

`frontend/apps/business-pda/src/router/index.ts`：

```typescript
import { createRouter, createWebHistory } from 'vue-router'
import { handleHotUpdate, routes } from 'vue-router/auto-routes'
import { installAuthGuard } from './guards/auth'

export const router = createRouter({
  history: createWebHistory(),
  routes,
})

installAuthGuard(router)

if (import.meta.hot) {
  handleHotUpdate(router)
}
```

- [ ] **Step 2: 写 main.ts（顺序照 business-console）**

`frontend/apps/business-pda/src/main.ts`：

```typescript
import { PiniaColada } from '@pinia/colada'
import { configureApiClient } from '@nerv-iip/api-client'
import { initTheme } from '@nerv-iip/ui'
import { createPinia } from 'pinia'
import { createApp } from 'vue'
import App from './App.vue'
import { handleUnauthorized } from './api/unauthorized'
import './assets/main.css'
import { router } from './router'
import { useAuthStore } from './stores/auth'

initTheme()

const app = createApp(App)
const pinia = createPinia()
app.use(pinia)

const auth = useAuthStore()
auth.setSessionExpiredHandler(() => handleUnauthorized(auth, router))
configureApiClient({
  accessTokenProvider: () => auth.accessToken,
  onUnauthorized: () => handleUnauthorized(auth, router),
})

app.use(PiniaColada, { queryOptions: { gcTime: 300_000 } })
app.use(router)
app.mount('#app')
```

> `unauthorized.ts` 的 `handleUnauthorized(auth, router)` 需要 `auth.clearSession(reason)`——store 已导出该方法，签名匹配。

- [ ] **Step 3: typecheck（router 依赖 pages，先建占位避免空 routes 报错）**

先建空占位页 `frontend/apps/business-pda/src/pages/index.vue`：

```vue
<script setup lang="ts">
import { definePage } from 'vue-router/auto'
definePage({ meta: { requiresAuth: true, title: '工作台' } })
</script>

<template>
  <div class="p-4">工作台占位</div>
</template>
```

Run: `pnpm -C frontend --filter @nerv-iip/business-pda typecheck`
Expected: PASS（占位页 + router 可编译）。

- [ ] **Step 4: Commit**

```bash
git add frontend/apps/business-pda/src/router/index.ts frontend/apps/business-pda/src/main.ts frontend/apps/business-pda/src/pages/index.vue
git commit -m "feat(business-pda): router + app bootstrap wiring (api-client/theme/colada)"
```

### Task 14: 登录页

**Files:**
- Create: `frontend/apps/business-pda/src/pages/login.vue`
- Test: `frontend/apps/business-pda/src/pages/login.test.ts`

- [ ] **Step 1: 写失败测试**

`login.test.ts`（mock store 的 `login`，断言提交后跳转）：

```typescript
import { mount, flushPromises } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'

const push = vi.fn()
vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
  useRoute: () => ({ query: {} }),
}))
vi.mock('vue-router/auto', () => ({ definePage: () => {} }))

const login = vi.fn(async () => {})
vi.mock('@/stores/auth', () => ({ useAuthStore: () => ({ login, isAuthenticated: false }) }))

import LoginPage from './login.vue'

describe('PDA login page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    push.mockClear()
    login.mockClear()
  })

  it('logs in and navigates home on submit', async () => {
    const wrapper = mount(LoginPage)
    await wrapper.get('input[name="loginName"]').setValue('op01')
    await wrapper.get('input[name="password"]').setValue('pw')
    await wrapper.get('form').trigger('submit.prevent')
    await flushPromises()
    expect(login).toHaveBeenCalledWith('op01', 'pw')
    expect(push).toHaveBeenCalledWith('/')
  })
})
```

- [ ] **Step 2: 跑测试确认失败**

Run: `pnpm -C frontend --filter @nerv-iip/business-pda exec vp test run src/pages/login.test.ts`
Expected: FAIL。

- [ ] **Step 3: 写登录页**

`frontend/apps/business-pda/src/pages/login.vue`：

```vue
<script setup lang="ts">
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { definePage } from 'vue-router/auto'
import { useAuthStore } from '@/stores/auth'

definePage({ meta: { guestOnly: true, title: '登录' } })

const auth = useAuthStore()
const router = useRouter()
const route = useRoute()
const loginName = ref('')
const password = ref('')
const error = ref('')
const submitting = ref(false)

async function onSubmit() {
  error.value = ''
  submitting.value = true
  try {
    await auth.login(loginName.value, password.value)
    const redirect = typeof route.query.redirect === 'string' ? route.query.redirect : '/'
    await router.push(redirect)
  } catch (e) {
    error.value = e instanceof Error ? e.message : '登录失败'
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <div class="pt-safe px-safe flex min-h-dvh flex-col justify-center bg-background px-6">
    <div class="mb-8 text-center">
      <h1 class="text-2xl font-semibold text-foreground">Nerv-IIP 手持作业台</h1>
      <p class="mt-1 text-sm text-muted-foreground">请登录以开始作业</p>
    </div>
    <form class="space-y-4" @submit.prevent="onSubmit">
      <input
        name="loginName"
        v-model="loginName"
        placeholder="账号"
        autocomplete="username"
        class="min-h-touch w-full rounded-lg border border-border bg-card px-4 text-base outline-none focus:border-brand"
      />
      <input
        name="password"
        v-model="password"
        type="password"
        placeholder="密码"
        autocomplete="current-password"
        class="min-h-touch w-full rounded-lg border border-border bg-card px-4 text-base outline-none focus:border-brand"
      />
      <p v-if="error" class="text-sm text-destructive">{{ error }}</p>
      <button
        type="submit"
        :disabled="submitting"
        class="min-h-touch w-full rounded-lg bg-primary text-base font-medium text-primary-foreground disabled:opacity-60"
      >
        {{ submitting ? '登录中…' : '登录' }}
      </button>
    </form>
  </div>
</template>
```

- [ ] **Step 4: 跑测试确认通过**

Run: `pnpm -C frontend --filter @nerv-iip/business-pda exec vp test run src/pages/login.test.ts`
Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add frontend/apps/business-pda/src/pages/login.vue frontend/apps/business-pda/src/pages/login.test.ts
git commit -m "feat(business-pda): login page reusing console auth"
```

### Task 15: 首页（扫码条 + 我的任务占位 + 应用墙）

**Files:**
- Modify: `frontend/apps/business-pda/src/pages/index.vue`
- Test: `frontend/apps/business-pda/src/pages/index.test.ts`

- [ ] **Step 1: 写失败测试**

`index.test.ts`：

```typescript
import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'

const push = vi.fn()
vi.mock('vue-router', () => ({ useRouter: () => ({ push }), RouterView: { template: '<div/>' } }))
vi.mock('vue-router/auto', () => ({ definePage: () => {} }))

import HomePage from './index.vue'

describe('PDA home', () => {
  it('renders the scan bar and the app wall from the task dictionary', () => {
    const wrapper = mount(HomePage)
    expect(wrapper.findComponent({ name: 'ScanBar' }).exists()).toBe(true)
    // 应用墙渲染字典中的任务标签
    expect(wrapper.text()).toContain('收货入库')
    expect(wrapper.text()).toContain('报工')
  })

  it('shows an empty-state for "我的任务" until the backend personal-task facade lands', () => {
    const wrapper = mount(HomePage)
    expect(wrapper.text()).toContain('暂无分配给你的任务')
  })
})
```

- [ ] **Step 2: 跑测试确认失败**

Run: `pnpm -C frontend --filter @nerv-iip/business-pda exec vp test run src/pages/index.test.ts`
Expected: FAIL。

- [ ] **Step 3: 写首页**

`frontend/apps/business-pda/src/pages/index.vue`（应用墙按 `routeReady` disable，不做空跳转；"我的任务"先真空态，待后端缺口 4 落地再接）：

```vue
<script setup lang="ts">
import { useRouter } from 'vue-router'
import { definePage } from 'vue-router/auto'
import { AppShellMobile, ScanBar, ListRow } from '@nerv-iip/ui-mobile'
import { PDA_TASK_KINDS } from '@nerv-iip/business-core'

definePage({ meta: { requiresAuth: true, title: '工作台' } })

const router = useRouter()

function onScan(value: string) {
  // 扫码解析端点（spec §9 缺口 5）落地前，先路由到占位（Plan 5 接 resolve）
  router.push({ path: '/scan', query: { code: value } }).catch(() => {})
}

function openTask(route: string, ready: boolean) {
  if (!ready) return
  router.push(route).catch(() => {})
}
</script>

<template>
  <AppShellMobile>
    <template #header>
      <div class="px-4 py-3">
        <h1 class="text-lg font-semibold text-foreground">工作台</h1>
      </div>
    </template>

    <div class="space-y-6 p-4">
      <ScanBar placeholder="扫描工单 / 库位 / 物料 / 设备" @scan="onScan" />

      <section>
        <h2 class="mb-2 text-sm font-medium text-muted-foreground">我的任务</h2>
        <div class="rounded-lg border border-dashed border-border bg-card px-4 py-8 text-center text-sm text-muted-foreground">
          暂无分配给你的任务
        </div>
      </section>

      <section>
        <h2 class="mb-2 text-sm font-medium text-muted-foreground">快捷应用</h2>
        <div class="grid grid-cols-3 gap-3">
          <button
            v-for="kind in PDA_TASK_KINDS"
            :key="kind.id"
            type="button"
            :disabled="!kind.routeReady"
            class="min-h-touch flex flex-col items-center justify-center gap-1 rounded-xl border border-border bg-card p-3 text-center text-sm text-foreground disabled:opacity-40"
            @click="openTask(kind.route, kind.routeReady)"
          >
            <span>{{ kind.label }}</span>
          </button>
        </div>
      </section>
    </div>
  </AppShellMobile>
</template>
```

> `ListRow` 此处暂未用到，但 barrel 已导出供后续业务页；为避免 unused import lint，先不 import 它（仅 import `AppShellMobile, ScanBar`）。上面的 import 已去掉 ListRow——若 lint 提示，删除未用导入即可。

- [ ] **Step 4: 修正 import（去掉未用的 ListRow）**

把 Step 3 文件首行 import 改为：

```typescript
import { AppShellMobile, ScanBar } from '@nerv-iip/ui-mobile'
```

- [ ] **Step 5: 跑测试确认通过**

Run: `pnpm -C frontend --filter @nerv-iip/business-pda exec vp test run src/pages/index.test.ts`
Expected: PASS。

> 注：测试断言 `findComponent({ name: 'ScanBar' })`——确保 `ScanBar.vue` 有 `name` 或通过 SFC 文件名推断（vue-tsc/test-utils 用文件名 `ScanBar` 作为组件名，通常可命中）。若未命中，改为断言存在 `input[placeholder^="扫描"]`。

- [ ] **Step 6: Commit**

```bash
git add frontend/apps/business-pda/src/pages/index.vue frontend/apps/business-pda/src/pages/index.test.ts
git commit -m "feat(business-pda): home with scan bar, my-tasks empty state, task-kind app wall"
```

---

## Phase 4 — Capacitor APK 打包基线

> 本阶段需要本机 Android 工具链（Android Studio / SDK / JDK 17+ / Gradle）。无 Android 环境时，Web 构建（`vp build`）仍是门禁；APK 产出在有环境时验证。

### Task 16: 接入 Capacitor 与 Android 平台

**Files:**
- Modify: `frontend/apps/business-pda/package.json`（新增 Capacitor 依赖与脚本）
- Create: `frontend/apps/business-pda/capacitor.config.ts`

- [ ] **Step 1: 装 Capacitor 依赖**

Run:
```bash
pnpm -C frontend --filter @nerv-iip/business-pda add @capacitor/core @capacitor/cli @capacitor/android @capacitor/status-bar @capacitor/keyboard @capacitor/app
```
Expected: 依赖写入 business-pda 的 package.json。

- [ ] **Step 2: 写 capacitor.config.ts**

`frontend/apps/business-pda/capacitor.config.ts`（`webDir` 指向 vp 构建产物目录——核对 `vp build` 实际输出目录，business-console build 后查看产物路径，通常为 `dist`）：

```typescript
import type { CapacitorConfig } from '@capacitor/cli'

const config: CapacitorConfig = {
  appId: 'com.nerviip.pda',
  appName: 'Nerv-IIP 手持作业台',
  webDir: 'dist',
  server: {
    androidScheme: 'https',
  },
}

export default config
```

- [ ] **Step 3: 加打包脚本**

在 `frontend/apps/business-pda/package.json` 的 `scripts` 中追加：

```json
"cap:sync": "vp build . && cap sync android",
"cap:open": "cap open android",
"cap:apk": "vp build . && cap sync android && cd android && ./gradlew assembleDebug"
```

- [ ] **Step 4: 生成 Android 平台（有 Android 环境时）**

Run（在 `frontend/apps/business-pda` 目录）:
```bash
pnpm exec cap add android
```
Expected: 生成 `android/` 原生工程。
若无 Android SDK：跳过本步，记录为环境依赖，不视为代码失败（同 AGENTS.md 对 Docker 不可用的处理口径）。

- [ ] **Step 5: 提交（android/ 视团队约定决定是否入库）**

```bash
git add frontend/apps/business-pda/package.json frontend/apps/business-pda/capacitor.config.ts frontend/pnpm-lock.yaml
git commit -m "build(business-pda): add Capacitor Android packaging baseline"
```

> 是否提交 `android/` 原生目录由团队决定（建议 `.gitignore` 掉，靠 `cap add` 复现）。本步只提交配置与脚本。

---

## Phase 5 — 验收门禁

### Task 17: 三包/应用门禁三连 + 工作区构建

- [ ] **Step 1: typecheck 全绿**

Run:
```bash
pnpm -C frontend --filter @nerv-iip/business-core typecheck
pnpm -C frontend --filter @nerv-iip/ui-mobile typecheck
pnpm -C frontend --filter @nerv-iip/business-pda typecheck
```
Expected: 三者均 PASS。

- [ ] **Step 2: test 全绿**

Run:
```bash
pnpm -C frontend --filter @nerv-iip/business-core test
pnpm -C frontend --filter @nerv-iip/ui-mobile test
pnpm -C frontend --filter @nerv-iip/business-pda test
```
Expected: 全部用例 PASS。

- [ ] **Step 3: business-pda 生产构建**

Run: `pnpm -C frontend --filter @nerv-iip/business-pda build`
Expected: PASS（vue-tsc + vp build 通过，产出 web 资源）。

- [ ] **Step 4: 确认未回归既有工作区 typecheck**

Run: `pnpm -C frontend typecheck`
Expected: 既有 console/business-console 不受影响（新增 paths 不破坏现有）；如 `check`/`fmt` 命中既有范围外格式问题，按 AGENTS.md「已知基线 caveat」如实记录，不视为本次回归。

- [ ] **Step 5: 最终 commit**

```bash
git add -A
git commit -m "test(business-pda): foundation gates green (typecheck/test/build)"
```

---

## Self-Review（计划作者自检结论）

- **Spec 覆盖**：§3 组件库/打包 → Task 5-16；§4 目录结构/包边界 → Task 2/5/11 + tsconfig 登记；§5 组件契约（5 个地基件）→ Task 6-10；§6 UI/UX 安全区/触控 → Task 5(mobile.css)/6/7/14；§7 首页范式 → Task 15；§8 同源 SOP → Task 3；§11 M0/M1 → 全部 Phase；§12 文档 → Task 1。WMS/MES/设备业务页（§7 其余）、扫码解析（§9 缺口5）、离线（§10）明确划归 Plan 2-5，非本计划缺口。
- **占位符扫描**：无 TBD/TODO；每个代码步骤含完整代码与可运行命令。两处"核对真实导入/字段名"是**对 generated 类型与既有 config 的核验提示**（api-client 类型、unplugin-vue-router 导入名、vp 构建产物目录），非占位逻辑，工程师按既有文件逐字对齐即可。
- **类型一致性**：`defineStepFlow`/`StepFlow` 在 Task 3 定义、barrel 在 Task 2 导出一致；`PdaTaskKind.routeReady` 在 Task 4 定义、Task 15 消费一致；`useAuthStore` 暴露的 `accessToken/clearSession/login/logout/restoreSession/setSessionExpiredHandler` 在 Task 12 定义、Task 13/14 消费一致；`ScanBar` 的 `@scan` 事件在 Task 7 定义、Task 15 消费一致。
```

