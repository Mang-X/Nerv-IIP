---
layout: page
title: Stepper 步进器
---

<script setup>
import { Stepper } from '@nerv-iip/ui-mobile'
import { ref } from 'vue'

const qty = ref(12)
const batch = ref(50)
</script>

<MobileDoc>

<template #phone>
  <section>
    <p class="ds-mdoc-label">基础用法</p>
    <Stepper v-model="qty" :min="1" :max="999" />
  </section>
  <section>
    <p class="ds-mdoc-label">配合表单行</p>
    <div style="display:flex;align-items:center;justify-content:space-between;width:100%;border:1px solid var(--border);border-radius:12px;background:var(--card);padding:12px 16px">
      <span style="font-size:15px">报工数量</span>
      <Stepper v-model="qty" :min="1" :max="999" />
    </div>
  </section>
  <section>
    <p class="ds-mdoc-label">自定义步长</p>
    <Stepper v-model="batch" :min="0" :max="500" :step="10" />
  </section>
</template>

# Stepper 步进器

紧凑的原生风数字步进器，中间为可编辑输入框，约 32px 高。输入与失焦时自动钳制到 `[min, max]` 范围。右侧手机模拟器为实时组件，随页面滚动吸顶。

## 基础用法

`v-model` 绑定数值，`min` / `max` 约束范围。

```vue
<Stepper v-model="qty" :min="1" :max="999" />
```

## 配合表单行

与标签横向排列，构成报工数量等表单行。

```vue
<Stepper v-model="qty" :min="1" :max="999" />
```

## 自定义步长

传 `step` 设置每次增减的步长。

```vue
<Stepper v-model="batch" :min="0" :max="500" :step="10" />
```

## 属性

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `v-model` | 当前值 | `number` | `0` |
| `min` | 最小值 | `number` | `0` |
| `max` | 最大值 | `number` | `Infinity` |
| `step` | 每次增减步长 | `number` | `1` |

</MobileDoc>
