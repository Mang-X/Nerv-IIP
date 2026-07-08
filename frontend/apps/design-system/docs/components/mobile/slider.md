---
layout: page
title: NvMobileSlider 滑块
---

<script setup>
import { ref } from 'vue'
import { NvMobileSlider } from '@nerv-iip/ui-mobile'

const basic = ref(40)
const stepped = ref(50)
const threshold = ref(85)
</script>

<MobileDoc>

<template #phone>

  <section>
    <p class="ds-mdoc-label">基础（拖动滑块）</p>
    <NvMobileSlider v-model="basic" />
    <p class="mt-1 text-sm text-muted-foreground">当前值：{{ basic }}</p>
  </section>
  <section>
    <p class="ds-mdoc-label">步进 + 数值气泡</p>
    <NvMobileSlider v-model="stepped" :min="0" :max="100" :step="5" show-bubble />
    <p class="mt-1 text-sm text-muted-foreground">当前值：{{ stepped }}（步长 5）</p>
  </section>
  <section>
    <p class="ds-mdoc-label">禁用</p>
    <NvMobileSlider :model-value="60" disabled />
  </section>
  <section>
    <p class="ds-mdoc-label">设备告警阈值</p>
    <div class="rounded-xl border border-border bg-card p-3">
      <div class="mb-2 flex items-center justify-between text-sm">
        <span class="font-medium text-foreground">CNC-07 主轴温度阈值</span>
        <span class="text-muted-foreground tabular-nums">{{ threshold }} ℃</span>
      </div>
      <NvMobileSlider v-model="threshold" :min="40" :max="120" :step="1" show-bubble />
    </div>
  </section>
</template>

# NvMobileSlider 滑块

单滑块范围选择。品牌色填充轨道，滑块 ≥44px 触控热区，指针拖动（与底部抽屉、滑动单元格等使用同一套指针手势）。用于数量、阈值等连续调节。右侧手机模拟器为实时组件。

## 基础

`v-model` 绑定数字，默认范围 0–100。

```vue
<NvMobileSlider v-model="basic" />
```

## 步进与气泡

`min / max / step` 控制范围与步长；加 `show-bubble` 在拖动时于滑块上方显示当前值。

```vue
<NvMobileSlider v-model="stepped" :min="0" :max="100" :step="5" show-bubble />
```

## 禁用

`disabled` 置灰且不响应拖动。

```vue
<NvMobileSlider :model-value="60" disabled />
```

## 属性

| 属性         | 说明               | 类型      | 默认    |
| ------------ | ------------------ | --------- | ------- |
| `v-model`    | 当前值             | `number`  | `0`     |
| `min`        | 最小值             | `number`  | `0`     |
| `max`        | 最大值             | `number`  | `100`   |
| `step`       | 步长               | `number`  | `1`     |
| `showBubble` | 拖动时显示数值气泡 | `boolean` | `false` |
| `disabled`   | 禁用               | `boolean` | `false` |

</MobileDoc>
