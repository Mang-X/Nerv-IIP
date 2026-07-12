---
title: NvQtyStepper 数量步进
---

<script setup>
import { NvQtyStepper } from '@nerv-iip/ui'
import { ref } from 'vue'

const qty = ref(12)
const batch = ref(50)
</script>

# NvQtyStepper 数量步进

工位报工的大数量步进器：**56px** 高、超大 ± 触控键、等宽数字读数，clamp 到 `[min, max]`、步长可调。区别于移动端紧凑的 NvStepper——它为"戴手套快速加减报工数"而放大。

## 基础

`v-model` 绑定数值，`min` / `max` 约束范围，`step` 设定步长；触界时对应按钮自动禁用。

<Demo>
  <NvQtyStepper v-model="qty" :min="1" :max="480" :step="1" />
</Demo>

```vue
<script setup>
const qty = ref(12)
</script>

<NvQtyStepper v-model="qty" :min="1" :max="480" :step="1" />
```

## 自定义步长

批量报工可用较大步长，例如按整托 50 件递增。

<Demo>
  <NvQtyStepper v-model="batch" :min="0" :max="500" :step="50" />
</Demo>

## 属性

| 属性         | 说明     | 类型     | 默认       |
| ------------ | -------- | -------- | ---------- |
| `modelValue` | 当前数量 | `number` | `0`        |
| `min`        | 最小值   | `number` | `0`        |
| `max`        | 最大值   | `number` | `Infinity` |
| `step`       | 步长     | `number` | `1`        |

## 事件

| 事件                | 说明     | 载荷     |
| ------------------- | -------- | -------- |
| `update:modelValue` | 数值变化 | `number` |
