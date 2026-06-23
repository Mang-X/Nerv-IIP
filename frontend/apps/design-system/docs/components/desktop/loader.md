---
title: Loader 加载
---

<script setup>
import { Loader, Skeleton } from '@nerv-iip/ui'

const loaderVariants = [
  { v: 'ring', name: '环形' },
  { v: 'dots', name: '点阵' },
  { v: 'bars', name: '柱条' },
  { v: 'pulse', name: '脉冲' },
]
</script>

# Loader 加载

克制、品牌着色的加载指示。`Loader` 提供四种形态用于即时反馈，`Skeleton` 用于内容占位；减弱动效下均降级为静态。

## 形态

<Demo>
  <div class="flex flex-wrap gap-8">
    <div v-for="l in loaderVariants" :key="l.v" class="flex flex-col items-center gap-2">
      <Loader :variant="l.v" size="lg" />
      <span class="font-mono text-xs text-muted-foreground">{{ l.name }}</span>
    </div>
  </div>
</Demo>

```vue
<Loader variant="ring" size="lg" />
<Loader variant="dots" size="lg" />
<Loader variant="bars" size="lg" />
<Loader variant="pulse" size="lg" />
```

## 尺寸

<Demo>
  <div class="flex items-center gap-6">
    <Loader variant="ring" size="sm" />
    <Loader variant="ring" />
    <Loader variant="ring" size="lg" />
  </div>
</Demo>

```vue
<Loader size="sm" />
<Loader />
<Loader size="lg" />
```

## 骨架屏 Skeleton

内容加载前的占位，避免布局抖动。

<Demo>
  <div class="flex w-full max-w-sm items-center gap-3">
    <Skeleton class="size-10 rounded-full" />
    <div class="flex-1 space-y-2">
      <Skeleton class="h-3 w-2/5" />
      <Skeleton class="h-3 w-3/5" />
    </div>
  </div>
</Demo>

```vue
<div class="flex items-center gap-3">
  <Skeleton class="size-10 rounded-full" />
  <div class="flex-1 space-y-2">
    <Skeleton class="h-3 w-2/5" />
    <Skeleton class="h-3 w-3/5" />
  </div>
</div>
```

## 属性

### Loader

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `variant` | 形态 | `ring \| dots \| bars \| pulse` | `ring` |
| `size` | 尺寸 | `sm \| default \| lg` | `default` |
| `label` | 无障碍标签 | `string` | `加载中` |

### Skeleton

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `class` | 通过工具类控制宽高与圆角 | `string` | — |
