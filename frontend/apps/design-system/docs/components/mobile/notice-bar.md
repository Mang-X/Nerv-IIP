---
layout: page
title: NoticeBar 通知条
---

<script setup>
import { NoticeBar } from '@nerv-iip/ui-mobile'
</script>

<MobileDoc>

<template #phone>
  <section>
    <p class="ds-mdoc-label">色调</p>
    <div class="w-full space-y-2">
      <NoticeBar tone="info">今日计划已重排，受影响工单 6 张</NoticeBar>
      <NoticeBar tone="warning">B 线物料不足：液压阀体 V3 缺口 452 件</NoticeBar>
      <NoticeBar tone="danger">WC-ASM-04 设备报警，请尽快处理</NoticeBar>
    </div>
  </section>
</template>

# NoticeBar 通知条

单行通知条，带前置语义图标与色调，文本溢出截断，适合页面顶部的提示与预警。

## 色调

`tone` 切换 info / warning / danger 三种语义色，默认插槽承载文案。

```vue
<script setup>
import { NoticeBar } from '@nerv-iip/ui-mobile'
</script>

<template>
  <NoticeBar tone="info">今日计划已重排，受影响工单 6 张</NoticeBar>
  <NoticeBar tone="warning">B 线物料不足：液压阀体 V3 缺口 452 件</NoticeBar>
  <NoticeBar tone="danger">WC-ASM-04 设备报警，请尽快处理</NoticeBar>
</template>
```

## 属性

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `tone` | 语义色调 | `info \| warning \| danger` | `info` |

</MobileDoc>
