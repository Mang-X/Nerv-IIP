# Design System Audit: Cascade Layer Bootstrap

## Executive Summary

NvUI 的 token、SFC layer 包裹和 shadcn 隔离规则已经建立，但宿主入口未保证在组件模块前加载全局 layer 顺序。真实浏览器中，Screen 的 `NvScreenPanel` 源码 padding `17px 20px` 计算为 `0px`，标题 margin `12px` 计算为 `0px`；相同加载模式存在于 PC、Mobile、Touch、Screen 的全部五个宿主。修复责任位于宿主 bootstrap 与跨宿主契约，不应逐组件提权。

## Scores

| Dimension               | Score / 4 | Evidence                                                            | Main Risk                               |
| ----------------------- | --------: | ------------------------------------------------------------------- | --------------------------------------- |
| Foundations             |         3 | ADR 0020 定义八层顺序与场景边界                                     | 加载时序未纳入门禁                      |
| Tokens                  |         4 | `theme.css` 与 Screen tokens 均分层、命名受契约保护                 | 无本次新增风险                          |
| Components and patterns |         2 | 自有 SFC 均在 `nv-components`，但运行时 spacing 可被 Preflight 清零 | 所有表面视觉密度失真                    |
| Accessibility           |         3 | 语义与交互未改变                                                    | 移动触控组件 spacing 清零可能压缩目标区 |
| Documentation           |         3 | ADR、AGENTS、产品定位已说明层级所有权                               | 未说明 `main.ts` 必须最先导入 CSS       |
| Governance and adoption |         2 | 契约检查 `main.css` 首语句                                          | 未检查 CSS 相对组件模块的入口顺序       |

## Critical Findings

| Severity | Finding                                                  | Evidence                                           | Impact                                                                                | Recommendation                                                 | Owner       | Effort | Confidence |
| -------- | -------------------------------------------------------- | -------------------------------------------------- | ------------------------------------------------------------------------------------- | -------------------------------------------------------------- | ----------- | ------ | ---------- |
| High     | 五个 Vue 宿主在 `App.vue` 或 UI barrel 后导入 `main.css` | 各 app `src/main.ts`; Screen 运行时 computed style | Tailwind `base` 可覆盖 `nv-components` 的 margin/padding，影响 PC/Mobile/Touch/Screen | 将 `main.css` 设为每个入口第一条 import，并用 UI contract 冻结 | Engineering | S      | High       |

## Roadmap

### 0-30 days

1. 在五个宿主入口修复加载顺序。
2. 扩展设计系统契约，同时检查 CSS 内部 layer 顺序与 JS 入口顺序。
3. 用 Screen、PC、Mobile 真实浏览器 computed style 验证跨表面结果。

### 31-60 days

无需额外迁移；持续由现有 contract test 阻止回退。

### 61-90 days

无需新增组件或 token。

## Verification Evidence

- TDD: 新契约在修复前对五个宿主全部失败；移动入口 import 后 22/22 通过。
- Screen mock: `/`、`/factory`、`/workshop/WS-ASSY`、`/line`、`/line/LN-ASSY-1`、`/equipment`、`/warehouse`、`/quality` 均在 1920×1080 下无滚动、无面板裁切、无零 padding；大尺寸 SVG 图表均含绘制标记。
- Screen spacing: `NvScreenPanel` 为 `17px 20px`，标题 margin 为 `12px`，状态标签为 `0px 9px`；layer 顺序 style 索引 0，ScreenPanel style 索引 45。
- PC: `NvDescriptions` bordered cell 为 `10px 14px`；layer 顺序索引 0，组件 style 索引 19。
- Mobile: `NvMobileGrid` item 为 `14px 4px`、gap `8px`；layer 顺序索引 0，组件 style 索引 627。
- Screen real: 未登录访问 `/factory` 正确进入 `/login`，layer 顺序索引 0，login style 索引 608，页面仍为 1920×1080 且无滚动。
- Package gates: UI 381 tests、UI Mobile 61 tests、Screen 172 tests、Console 141 tests、Business Console 1019 tests、PDA 340 tests 均通过；五个宿主生产构建通过。

## Assumptions and Gaps

- 本审计聚焦 cascade layer bootstrap，不是完整的视觉、可访问性或 Figma 对齐审计。
- 组件库源文件未发现需要逐件修复的 spacing 定义；最终判断以构建产物和真实浏览器 computed style 为准。
