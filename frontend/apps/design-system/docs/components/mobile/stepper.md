---
title: Stepper 步进器
---

<script setup>
import { Stepper } from '@nerv-iip/ui-mobile'
import { ref } from 'vue'

const qty = ref(12)
const batch = ref(50)
</script>

# Stepper 步进器

紧凑的原生风数字步进器，中间为可编辑输入框，约 32px 高。输入与失焦时自动钳制到 `[min, max]` 范围。

## 基础用法

<Demo mobile>
  <Stepper v-model="qty" :min="1" :max="999" />
</Demo>

```vue
<Stepper v-model="qty" :min="1" :max="999" />
```

## 配合表单行

<Demo mobile>
  <div style="display:flex;align-items:center;justify-content:space-between;width:100%;border:1px solid var(--border);border-radius:12px;background:var(--card);padding:12px 16px">
    <span style="font-size:15px">报工数量</span>
    <Stepper v-model="qty" :min="1" :max="999" />
  </div>
</Demo>

```vue
<Stepper v-model="qty" :min="1" :max="999" />
```

## 自定义步长

<Demo mobile>
  <Stepper v-model="batch" :min="0" :max="500" :step="10" />
</Demo>

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
