# NvUI Cascade Layer Bootstrap Design

## Context

NvUI 的 PC、Mobile、Touch、Screen 自有组件都把样式放在 `@layer nv-components`。宿主应用的 `main.css` 已按 ADR 0020 首句声明八层顺序，但五个 Vue 宿主都在 `App.vue` 或 `@nerv-iip/ui` 之后导入该文件。Vite 开发态因此可能先遇到组件的 `nv-components`，再遇到 Tailwind 的 `base`，导致 Preflight 的 `margin: 0`、`padding: 0` 覆盖组件样式。

## Decision

保持 ADR 0020 的宿主所有权：不新增 UI 包 CSS 子入口，不给组件包增加隐式全局副作用，不调整 shadcn 原版。五个宿主的 `main.ts` 必须以 `import './assets/main.css'` 作为第一条 import，使八层顺序在任何 Vue SFC 或 NvUI barrel 被解析前建立。

`@nerv-iip/ui` 的设计系统契约测试同时验证：

1. 每个宿主 `main.css` 的第一条非注释语句仍是完整八层顺序。
2. 每个宿主 `main.ts` 的第一条 import 是其 `main.css`。

覆盖宿主为 `business-console`、`business-pda`、`console`、`design-system`、`screen`。这项入口修复不改变任何组件 props、事件、数据流或视觉 token。

## Alternatives Considered

### Package side-effect import

由 `@nerv-iip/ui` 和 `@nerv-iip/ui-mobile` 自动导入 layer 顺序。它能集中修复，但会让组件 barrel 产生隐式全局 CSS 副作用，还会要求新增或复制 CSS 子入口，破坏当前“宿主声明层级”的 ADR 边界，因此不采用。

### App-layer overrides or `!important`

逐组件恢复 padding/margin。它只能掩盖已观察到的组件，无法覆盖所有 NvUI 表面，并削弱 utilities/app 覆盖能力，因此不采用。

## Component Map

- 五个 `main.ts`：仅负责应用启动依赖和全局样式加载顺序；无组件状态或数据流变化。
- `design-system.contract.test.ts`：作为跨宿主 layer 启动顺序的单一静态门禁。
- NvUI 和 NvUI Mobile SFC：保持现有 `@layer nv-components`，无需逐件修改。

## Verification

- TDD 红绿验证新的跨宿主入口契约。
- 运行 `@nerv-iip/ui`、`@nerv-iip/ui-mobile`、`@nerv-iip/screen` 的 typecheck/test/build 门禁。
- 在 1920×1080 浏览器中验证大屏面板 padding、标题 margin、标签 padding 的 computed style，并检查受影响页面截图。
- 对 PC 与 Mobile 各选一个真实宿主，验证代表性 NvUI 组件 computed style 非零，证明修复不是 Screen 特例。

## Risks

- 风险集中在 CSS 打包/开发态顺序差异。静态契约防止入口回退，真实浏览器验证覆盖运行时结果。
- 不涉及 OpenAPI、生成代码、后端、数据库或业务数据契约。
