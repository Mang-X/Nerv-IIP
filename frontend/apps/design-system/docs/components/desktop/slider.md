---
title: Slider 滑块
---

<script setup>
import { SliderPro } from '@nerv-iip/ui'
import { ref } from 'vue'

const util = ref([75])
const taktRange = ref([45, 75])
const step = ref([60])
</script>

# Slider 滑块

数值滑动选择。`SliderPro` 基于 reka 原语重建，品牌色填充已选区间，拖动手柄带聚焦环与抓取放大反馈（无回弹，遵循动效哲学）。`modelValue` 传数组——多个值即多手柄（区间选择）。

## 基础用法

`v-model` 绑定一个单元素数组。

<Demo>
  <div class="flex w-full max-w-md flex-col gap-3">
    <div class="flex justify-between text-sm text-muted-foreground">
      <span>稼动率告警阈值</span><span class="font-medium text-foreground">{{ util[0] }}%</span>
    </div>
    <SliderPro v-model="util" :max="100" :step="1" />
  </div>
</Demo>

```vue
<SliderPro v-model="util" :max="100" :step="1" />
```

## 区间选择

`modelValue` 传两个值，渲染两个手柄。

<Demo>
  <div class="flex w-full max-w-md flex-col gap-3">
    <div class="flex justify-between text-sm text-muted-foreground">
      <span>节拍区间（秒）</span><span class="font-medium text-foreground">{{ taktRange[0] }} – {{ taktRange[1] }}</span>
    </div>
    <SliderPro v-model="taktRange" :min="0" :max="120" :step="5" />
  </div>
</Demo>

```vue
<SliderPro v-model="taktRange" :min="0" :max="120" :step="5" />
```

## 步长与禁用

<Demo>
  <div class="flex w-full max-w-md flex-col gap-6">
    <SliderPro v-model="step" :max="100" :step="10" />
    <SliderPro :model-value="[40]" :max="100" disabled />
  </div>
</Demo>

## 属性

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `v-model` | 当前值（数组，多元素 = 多手柄） | `number[]` | — |
| `min` / `max` | 取值范围 | `number` | `0` / `100` |
| `step` | 步长 | `number` | `1` |
| `orientation` | 方向 | `horizontal \| vertical` | `horizontal` |
| `disabled` | 禁用 | `boolean` | `false` |
