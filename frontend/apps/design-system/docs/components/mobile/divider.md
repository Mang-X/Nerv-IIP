---
layout: page
title: Divider 分割线
---

<script setup>
import { NvMobileDivider } from '@nerv-iip/ui-mobile'
</script>

<MobileDoc>

<template #phone>

  <section>
    <p class="ds-mdoc-label">基础分割</p>
    <div class="text-[15px] text-foreground">工单 WO-20406</div>
    <NvMobileDivider />
    <div class="text-sm text-muted-foreground">注塑车间 · A2 产线</div>
  </section>
  <section>
    <p class="ds-mdoc-label">带文字</p>
    <NvMobileDivider>今日已完成</NvMobileDivider>
  </section>
  <section>
    <p class="ds-mdoc-label">垂直分割</p>
    <div class="flex items-center text-sm text-muted-foreground">
      <span>计划 320</span>
      <NvMobileDivider direction="vertical" class="mx-3" />
      <span>已产 286</span>
      <NvMobileDivider direction="vertical" class="mx-3" />
      <span>不良 4</span>
    </div>
  </section>
</template>

# Divider 分割线

一根细发丝线（`var(--border)`），用于在内容块之间做轻量分隔。横向可在中间嵌入说明文字；纵向用于分隔同一行内的若干指标或操作。右侧手机模拟器为实时组件，随页面滚动吸顶。

## 基础分割

不带任何内容时渲染一条占满宽度的横向细线。

```vue
<NvMobileDivider />
```

## 带文字

在默认插槽放入文字时，文字居中、两侧自动补齐细线，常用于分隔列表的时间段或分组。

```vue
<NvMobileDivider>今日已完成</NvMobileDivider>
```

## 垂直分割

`direction="vertical"` 渲染一段与文字等高的竖线，用于行内分隔指标。高度跟随当前字号，配合 `class` 控制左右间距。

```vue
<span>计划 320</span>
<NvMobileDivider direction="vertical" class="mx-3" />
<span>已产 286</span>
```

## 属性

| 属性        | 说明                 | 类型                     | 默认         |
| ----------- | -------------------- | ------------------------ | ------------ |
| `direction` | 方向                 | `horizontal \| vertical` | `horizontal` |
| 默认插槽    | 横向时居中的说明文字 | `slot`                   | —            |

</MobileDoc>
