---
title: NvTouchSegmented 分段切换
---

<script setup>
import { NvTouchSegmented } from '@nerv-iip/ui'
import { ref } from 'vue'

const view = ref('queue')
const viewOptions = [
  { value: 'queue', label: '待加工' },
  { value: 'done', label: '已完成' },
]
const shift = ref('day')
const shiftOptions = [
  { value: 'day', label: '早班' },
  { value: 'swing', label: '中班' },
  { value: 'night', label: '夜班' },
]
</script>

# NvTouchSegmented 分段切换

工位大触控的分段控制器：**48px** 高，一点即切，适合 2–4 个互斥视图。比一体机上的下拉选择少一次展开-选择的路径。与大屏的 NvScreenSegmented 同源，尺寸按大触控放大。

## 基础

`v-model` 绑定当前值，`options` 提供 `{ value, label }` 列表。

<Demo>
  <NvTouchSegmented v-model="view" :options="viewOptions" />
</Demo>

```vue
<script setup>
const view = ref('queue')
const viewOptions = [
  { value: 'queue', label: '待加工' },
  { value: 'done', label: '已完成' },
]
</script>

<NvTouchSegmented v-model="view" :options="viewOptions" />
```

## 多段

段数建议 ≤4，超过改用列表或标签页。

<Demo>
  <NvTouchSegmented v-model="shift" :options="shiftOptions" />
</Demo>

## 属性

| 属性         | 说明       | 类型                                 | 默认 |
| ------------ | ---------- | ------------------------------------ | ---- |
| `modelValue` | 当前选中值 | `string`                             | —    |
| `options`    | 选项列表   | `{ value: string; label: string }[]` | —    |

## 事件

| 事件                | 说明       | 载荷     |
| ------------------- | ---------- | -------- |
| `update:modelValue` | 选中项变化 | `string` |
