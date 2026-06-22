---
title: DatePicker 日期选择器
---

<script setup>
import { DatePickerPro } from '@nerv-iip/ui'
import { ref } from 'vue'

const planDate = ref('2026-06-18')
const emptyDate = ref()
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

## 属性

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `v-model` | 绑定日期（`YYYY-MM-DD` 字符串） | `string \| null` | `null` |
| `placeholder` | 未选中占位文本 | `string` | `选择日期` |
| `disabled` | 是否禁用 | `boolean` | `false` |
