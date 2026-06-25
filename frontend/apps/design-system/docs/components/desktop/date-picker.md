---
title: DatePicker 日期选择器
---

<script setup>
import { DatePickerPro, DateRangePickerPro } from '@nerv-iip/ui'
import { ref } from 'vue'

const planDate = ref('2026-06-18')
const emptyDate = ref()
const range = ref({ start: '2026-06-10', end: '2026-06-18' })
</script>

# DatePicker 日期选择器

通过日历浮层选择单个日期。`DatePickerPro` 以 `outline` 触发器配合品牌着色的日历单元。

## 基础用法

<Demo>
  <div style="max-width: 240px">
    <DatePickerPro v-model="planDate" placeholder="选择日期" />
  </div>
</Demo>

```vue
<DatePickerPro v-model="planDate" placeholder="选择日期" />
```

## 占位与禁用

<Demo>
  <div style="display:flex;flex-direction:column;gap:12px;max-width:240px">
    <DatePickerPro v-model="emptyDate" placeholder="计划开工日期" />
    <DatePickerPro :model-value="'2026-06-18'" :disabled="true" />
  </div>
</Demo>

```vue
<DatePickerPro v-model="emptyDate" placeholder="计划开工日期" />
<DatePickerPro v-model="planDate" disabled />
```

## 日期范围 DateRangePicker

`DateRangePickerPro` 选择起止区间：首次点击定起点，再次点击定终点（自动排序），悬停可实时预览跨度。模型是 `{ start, end }` 字符串对象。

<Demo>
  <div style="max-width: 280px">
    <DateRangePickerPro v-model="range" placeholder="选择日期范围" />
  </div>
</Demo>

```vue
<script setup>
import { DateRangePickerPro } from '@nerv-iip/ui'
import { ref } from 'vue'

const range = ref({ start: '2026-06-10', end: '2026-06-18' })
</script>

<template>
  <DateRangePickerPro v-model="range" placeholder="选择日期范围" />
</template>
```

## 属性

### DatePicker

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `v-model` | 绑定日期（`YYYY-MM-DD` 字符串） | `string \| null` | `null` |
| `placeholder` | 未选中占位文本 | `string` | `选择日期` |
| `disabled` | 是否禁用 | `boolean` | `false` |

### DateRangePicker

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `v-model` | 绑定区间 | `{ start: string \| null, end: string \| null } \| null` | `null` |
| `placeholder` | 未选中占位文本 | `string` | `选择日期范围` |
| `disabled` | 是否禁用 | `boolean` | `false` |
