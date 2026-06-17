# 组件概览

组件分两层，共享同一套[设计令牌](/foundations/tokens)，但各自适配表面的原生手感。

## 按表面浏览

- [**桌面 PC**](/components/desktop/) —— `@nerv-iip/ui`：按钮、徽标、卡片、提示、DataTablePro、描述列表、时间线、图表、Pro 覆盖层。
- [**移动 PDA**](/components/mobile/) —— `@nerv-iip/ui-mobile`：原生质感控件、手势（侧滑 / 下拉刷新 / 抽屉）、宫格 / 悬浮按钮 / 居中提示。
- [**一体机看板**](/components/board) —— 大屏触控布局与大卡组件。

## 在文档里直接用组件

本文档站已把设计系统接入，Markdown 里可以直接 `import` 并渲染任意组件：

```md
<script setup>
import { Button, Badge } from '@nerv-iip/ui'
</script>

<Button>主操作</Button> <Badge>徽标</Badge>
```

下面每个组件页都是**真组件实时渲染**，不是截图。

::: tip 完整画廊
[桌面 PC](/components/desktop/)、[移动 PDA](/components/mobile/)、[一体机看板](/components/board) 三个表面的完整 showcase 已迁入本文档站，以全宽页面实时演示真实组件（含图表、表格、手势、覆盖层等）。
:::
