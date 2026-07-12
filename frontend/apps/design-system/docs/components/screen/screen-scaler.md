---
title: NvScreenScaler 舞台缩放
---

# NvScreenScaler 舞台缩放

大屏**舞台缩放容器**:内容按设计基准(默认 1920×1080)绝对布局,组件把整个舞台等比缩放到实际视口——挂墙电视 / 投影 / 拼接屏无论分辨率如何,画面构图恒定不跑版。`position: fixed` 占满视口、居中 letterbox,监听 resize 实时重算。

::: warning 全屏组件,无内嵌 demo
`NvScreenScaler` 固定占满整个视口,无法在文档页内演示——实际效果见 [大屏应用](https://github.com/Mang-X/Nerv-IIP/tree/main/frontend/apps/screen)(`apps/screen` 所有页面经 `ScreenLayout` 包裹在 1920×1080 舞台内)。
:::

## 基础用法

作为页面最外层容器,内部按设计稿像素绝对布局:

```vue
<template>
  <NvScreenScaler :design-width="1920" :design-height="1080">
    <!-- 内部一律按 1920×1080 设计稿布局，无需任何响应式处理 -->
    <NvScreenHeader title="Nerv-IIP 工厂运营大屏" />
    <main class="board"><!-- … --></main>
  </NvScreenScaler>
</template>
```

## 缩放模式

`computeScale(viewportW, viewportH, designW, designH, mode)` 纯函数亦从包内导出(单测覆盖):

| mode        | 行为                                   | 适用         |
| ----------- | -------------------------------------- | ------------ |
| `fit`(默认) | 等比取小边,letterbox 留边,整屏完整可见 | 挂墙大屏默认 |
| `width`     | 按宽度等比,高度可能溢出                | 超宽拼接屏   |
| `stretch`   | 宽高各自拉伸(非等比,会变形)            | 一般不用     |

```vue
<NvScreenScaler mode="width" :design-width="3840" :design-height="1080">
  <!-- 双联屏内容 -->
</NvScreenScaler>
```

## API

| Prop           | 类型                            | 默认    | 说明                   |
| -------------- | ------------------------------- | ------- | ---------------------- |
| `designWidth`  | `number`                        | `1920`  | 设计基准宽(px)         |
| `designHeight` | `number`                        | `1080`  | 设计基准高(px)         |
| `mode`         | `'fit' \| 'width' \| 'stretch'` | `'fit'` | 缩放模式               |
| 默认插槽       | —                               | —       | 舞台内容(按设计稿布局) |

::: tip SVG 文本与缩放
舞台整体 `transform: scale()` 下,SVG `preserveAspectRatio="none"` 图表内的文字层要用 HTML overlay 而非 SVG text(见 [NvScreenTrendChart](./trend-chart) 的悬停卡实现),否则非等比场景下文字变形。
:::
