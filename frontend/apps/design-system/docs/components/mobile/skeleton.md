---
layout: page
title: NvMobileSkeleton 骨架屏
---

<script setup>
import { NvMobileSkeleton } from '@nerv-iip/ui-mobile'
</script>

<MobileDoc>

<template #phone>

  <section>
    <p class="nv-mdoc-label">基础形状</p>
    <div class="space-y-2">
      <NvMobileSkeleton variant="text" />
      <NvMobileSkeleton variant="text" class="w-3/5" />
      <NvMobileSkeleton variant="rect" />
      <NvMobileSkeleton variant="circle" />
    </div>
  </section>
  <section>
    <p class="nv-mdoc-label">组合占位</p>
    <div class="flex items-center gap-3">
      <NvMobileSkeleton variant="circle" />
      <div class="flex-1 space-y-2">
        <NvMobileSkeleton variant="text" class="w-2/5" />
        <NvMobileSkeleton variant="text" class="w-4/5" />
      </div>
    </div>
  </section>
</template>

# NvMobileSkeleton 骨架屏

内容加载时的占位块，带一道从左向右扫过的微弱微光（shimmer）。`variant` 决定基础形状，尺寸通过 `class`（如 `class="w-3/5"`）传入。在系统开启「减少动态效果」时自动停掉动画。右侧手机模拟器为实时组件，随页面滚动吸顶。

## 基础形状

`text` 文本行、`rect` 矩形块、`circle` 圆形，配合 `class` 调整宽高。

```vue
<NvMobileSkeleton variant="text" class="w-3/5" />
<NvMobileSkeleton variant="rect" />
<NvMobileSkeleton variant="circle" />
```

## 组合占位

实际场景常把若干骨架拼成一条列表项的轮廓 —— 圆形头像加两行文本。

```vue
<div class="flex items-center gap-3">
  <NvMobileSkeleton variant="circle" />
  <div class="flex-1 space-y-2">
    <NvMobileSkeleton variant="text" class="w-2/5" />
    <NvMobileSkeleton variant="text" class="w-4/5" />
  </div>
</div>
```

## 属性

| 属性      | 说明                    | 类型                     | 默认   |
| --------- | ----------------------- | ------------------------ | ------ |
| `variant` | 基础形状                | `text \| rect \| circle` | `text` |
| `class`   | 透传尺寸 / 圆角等工具类 | `string`                 | —      |

</MobileDoc>
