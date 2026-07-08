---
title: NvTimePicker 时间选择器
---

<script setup>
import { NvTimePicker } from '@nerv-iip/ui'
import { ref } from 'vue'

const planTime = ref('08:30')
const stepTime = ref()
</script>

# NvTimePicker 时间选择器

通过浮层选择小时与分钟。`NvTimePicker` 支持 `minute-step` 控制分钟粒度，触发器与日期选择器一致。

## 基础用法

<Demo>
  <div style="max-width: 240px">
    <NvTimePicker v-model="planTime" placeholder="选择时间" />
  </div>
</Demo>

```vue
<NvTimePicker v-model="planTime" placeholder="选择时间" />
```

## 分钟步长

<Demo>
  <div style="max-width: 240px">
    <NvTimePicker v-model="stepTime" :minute-step="5" placeholder="按 5 分钟选择" />
  </div>
</Demo>

```vue
<NvTimePicker v-model="stepTime" :minute-step="5" placeholder="按 5 分钟选择" />
```

## 禁用

<Demo>
  <div style="max-width: 240px">
    <NvTimePicker :model-value="'08:30'" :disabled="true" />
  </div>
</Demo>

```vue
<NvTimePicker v-model="planTime" disabled />
```

## 属性

| 属性          | 说明                       | 类型             | 默认       |
| ------------- | -------------------------- | ---------------- | ---------- |
| `v-model`     | 绑定时间（`HH:mm` 字符串） | `string \| null` | `null`     |
| `minute-step` | 分钟步长                   | `number`         | `1`        |
| `placeholder` | 未选中占位文本             | `string`         | `选择时间` |
| `disabled`    | 是否禁用                   | `boolean`        | `false`    |
