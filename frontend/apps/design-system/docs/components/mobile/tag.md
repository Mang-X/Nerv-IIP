---
layout: page
title: Tag 标签
---

<script setup>
import { Tag } from '@nerv-iip/ui-mobile'
</script>

<MobileDoc>

<template #phone>
  <section>
    <p class="ds-mdoc-label">变体</p>
    <div class="flex flex-wrap items-center gap-1.5">
      <Tag>常规</Tag>
      <Tag variant="brand">优先</Tag>
      <Tag variant="success">已入库</Tag>
      <Tag variant="warning">待质检</Tag>
      <Tag variant="danger">超期</Tag>
    </div>
  </section>
  <section>
    <p class="ds-mdoc-label">尺寸</p>
    <div class="flex flex-wrap items-center gap-1.5">
      <Tag variant="brand" size="sm">小号</Tag>
      <Tag variant="brand" size="md">中号</Tag>
    </div>
  </section>
  <section>
    <p class="ds-mdoc-label">可关闭</p>
    <div class="flex flex-wrap items-center gap-1.5">
      <Tag variant="brand" closable>注塑</Tag>
      <Tag variant="success" closable>CNC</Tag>
      <Tag variant="default" closable>装配</Tag>
    </div>
  </section>
</template>

# Tag 标签

带文字的小标签，用于状态、分类或筛选条件。区别于 Badge 角标（固定在角落的计数 / 红点），Tag 是排在内容流里的独立色块：柔和的色底配同色系深色文字。右侧手机模拟器为实时组件，随页面滚动吸顶。

## 变体

`default / brand / success / warning / danger` 五种语义色。

```vue
<Tag variant="success">已入库</Tag>
<Tag variant="warning">待质检</Tag>
<Tag variant="danger">超期</Tag>
```

## 尺寸

`sm / md` 两档，密集列表里用 `sm`。

```vue
<Tag variant="brand" size="sm">小号</Tag>
<Tag variant="brand" size="md">中号</Tag>
```

## 可关闭

加 `closable` 显示关闭按钮，点击触发 `close` 事件，由父组件移除该标签。

```vue
<Tag variant="brand" closable @close="remove(t)">注塑</Tag>
```

## 属性

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `variant` | 语义色 | `default \| brand \| success \| warning \| danger` | `default` |
| `size` | 尺寸 | `sm \| md` | `md` |
| `closable` | 是否显示关闭按钮 | `boolean` | `false` |

## 事件

| 事件 | 说明 | 回调参数 |
|---|---|---|
| `close` | 点击关闭按钮时触发 | — |

</MobileDoc>
