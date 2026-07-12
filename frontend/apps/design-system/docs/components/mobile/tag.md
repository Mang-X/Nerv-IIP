---
layout: page
title: NvMobileTag 标签
---

<script setup>
import { NvMobileTag } from '@nerv-iip/ui-mobile'
</script>

<MobileDoc>

<template #phone>

  <section>
    <p class="nv-mdoc-label">变体</p>
    <div class="flex flex-wrap items-center gap-1.5">
      <NvMobileTag>常规</NvMobileTag>
      <NvMobileTag variant="brand">优先</NvMobileTag>
      <NvMobileTag variant="success">已入库</NvMobileTag>
      <NvMobileTag variant="warning">待质检</NvMobileTag>
      <NvMobileTag variant="danger">超期</NvMobileTag>
    </div>
  </section>
  <section>
    <p class="nv-mdoc-label">尺寸</p>
    <div class="flex flex-wrap items-center gap-1.5">
      <NvMobileTag variant="brand" size="sm">小号</NvMobileTag>
      <NvMobileTag variant="brand" size="md">中号</NvMobileTag>
    </div>
  </section>
  <section>
    <p class="nv-mdoc-label">可关闭</p>
    <div class="flex flex-wrap items-center gap-1.5">
      <NvMobileTag variant="brand" closable>注塑</NvMobileTag>
      <NvMobileTag variant="success" closable>CNC</NvMobileTag>
      <NvMobileTag variant="default" closable>装配</NvMobileTag>
    </div>
  </section>
</template>

# NvMobileTag 标签

带文字的小标签，用于状态、分类或筛选条件。区别于 NvMobileBadge 角标（固定在角落的计数 / 红点），NvMobileTag 是排在内容流里的独立色块：柔和的色底配同色系深色文字。右侧手机模拟器为实时组件，随页面滚动吸顶。

## 变体

`default / brand / success / warning / danger` 五种语义色。

```vue
<NvMobileTag variant="success">已入库</NvMobileTag>
<NvMobileTag variant="warning">待质检</NvMobileTag>
<NvMobileTag variant="danger">超期</NvMobileTag>
```

## 尺寸

`sm / md` 两档，密集列表里用 `sm`。

```vue
<NvMobileTag variant="brand" size="sm">小号</NvMobileTag>
<NvMobileTag variant="brand" size="md">中号</NvMobileTag>
```

## 可关闭

加 `closable` 显示关闭按钮，点击触发 `close` 事件，由父组件移除该标签。

```vue
<NvMobileTag variant="brand" closable @close="remove(t)">注塑</NvMobileTag>
```

## 属性

| 属性       | 说明             | 类型                                               | 默认      |
| ---------- | ---------------- | -------------------------------------------------- | --------- |
| `variant`  | 语义色           | `default \| brand \| success \| warning \| danger` | `default` |
| `size`     | 尺寸             | `sm \| md`                                         | `md`      |
| `closable` | 是否显示关闭按钮 | `boolean`                                          | `false`   |

## 事件

| 事件    | 说明               | 回调参数 |
| ------- | ------------------ | -------- |
| `close` | 点击关闭按钮时触发 | —        |

</MobileDoc>
