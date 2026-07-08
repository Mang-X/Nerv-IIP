# 组件概览

组件分两层，共享同一套[设计令牌](/foundations/tokens)，但各自适配表面的原生手感。

## 按表面浏览

- [**桌面 PC**](/components/desktop/) —— `@nerv-iip/ui`：按钮、徽标、卡片、提示、FileUpload、NvDataTable、描述列表、时间线、图表、覆盖层。
- [**移动 PDA**](/components/mobile/) —— `@nerv-iip/ui-mobile`：原生质感控件、手势（侧滑 / 下拉刷新 / 抽屉）、宫格 / 悬浮按钮 / 居中提示。
- [**一体机看板**](/components/board) —— 大屏触控布局与大卡组件。

## 在文档里直接用组件

本文档站已把设计系统接入，Markdown 里可以直接 `import` 并渲染任意组件：

```md
<script setup>
import { NvButton, NvBadge } from '@nerv-iip/ui'
</script>

<NvButton>主操作</NvButton> <NvBadge>徽标</NvBadge>
```

下面每个组件页都是**真组件实时渲染**，不是截图。

## 命名约定

品牌组件一律带 `Nv` 前缀（`NvButton`、`NvDataTable`、`NvMobileBadge`、`NvOeeHero`……）——看到
`Nv*` 即「可直接使用的 Nerv-IIP 品牌件」。**没有** `Nv` 前缀的名字要么是 shadcn 原版底座（仅组件库内部
使用），要么是过渡期保留的旧名。旧名（`*Pro` / `Screen*` / `Mobile*` / 裸名）已标 `@deprecated`，仍可
编译但请改用对应 `Nv*`；组件迁移到位后（#789）移除。命名规则与逐件映射见 ADR 0020 附录 A
（`docs/adr/0020-nvui-naming-token-namespaces-and-style-isolation.md`）。

::: tip 完整画廊
[桌面 PC](/components/desktop/)、[移动 PDA](/components/mobile/)、[一体机看板](/components/board) 三个表面的完整 showcase 已迁入本文档站，以全宽页面实时演示真实组件（含图表、表格、手势、覆盖层等）。
:::
