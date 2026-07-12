---
layout: page
title: NvMobileProgress 进度条
---

<script setup>
import { NvMobileProgress } from '@nerv-iip/ui-mobile'
</script>

<MobileDoc>

<template #phone>

  <section>
    <p class="nv-mdoc-label">基础</p>
    <NvMobileProgress :value="42" />
  </section>
  <section>
    <p class="nv-mdoc-label">带百分比</p>
    <div class="flex flex-col gap-3">
      <NvMobileProgress :value="72" show-label />
      <NvMobileProgress :value="100" show-label />
    </div>
  </section>
  <section>
    <p class="nv-mdoc-label">语义色</p>
    <div class="flex flex-col gap-3">
      <NvMobileProgress :value="68" tone="brand" show-label />
      <NvMobileProgress :value="100" tone="success" show-label />
      <NvMobileProgress :value="35" tone="warning" show-label />
      <NvMobileProgress :value="18" tone="danger" show-label />
    </div>
  </section>
  <section>
    <p class="nv-mdoc-label">工单报工进度</p>
    <div class="rounded-xl border border-border bg-card p-3">
      <div class="mb-1.5 flex items-center justify-between text-sm">
        <span class="font-medium text-foreground">WO-20260617-0382</span>
        <span class="text-muted-foreground">已报 860 / 1200</span>
      </div>
      <NvMobileProgress :value="72" tone="brand" show-label />
    </div>
  </section>
</template>

# NvMobileProgress 进度条

线性进度条。圆角轨道承载语义色填充，数值变化时填充宽度平滑过渡（已适配 `prefers-reduced-motion`）。用于报工进度、上传进度等 PDA 场景。右侧手机模拟器为实时组件。

## 基础

`value` 取值 0–100，超出范围自动夹紧。

```vue
<NvMobileProgress :value="42" />
```

## 百分比标签

加 `show-label` 在右侧显示整数百分比。

```vue
<NvMobileProgress :value="72" show-label />
```

## 语义色

`tone` 支持 `brand / success / warning / danger`，对应品牌、完成、预警、异常。

```vue
<NvMobileProgress :value="100" tone="success" show-label />
<NvMobileProgress :value="18" tone="danger" show-label />
```

## 属性

| 属性        | 说明                      | 类型                                    | 默认    |
| ----------- | ------------------------- | --------------------------------------- | ------- |
| `value`     | 进度值（0–100，自动夹紧） | `number`                                | `0`     |
| `tone`      | 语义色                    | `brand \| success \| warning \| danger` | `brand` |
| `showLabel` | 是否显示百分比标签        | `boolean`                               | `false` |

</MobileDoc>
