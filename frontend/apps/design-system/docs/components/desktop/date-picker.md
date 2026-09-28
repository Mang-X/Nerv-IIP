---
title: NvDatePicker 日期选择器
---

<script setup>
import { NvDatePicker, NvDateRangePicker } from '@nerv-iip/ui'
import { ref } from 'vue'

const planDate = ref('2026-06-18')
const emptyDate = ref()
const range = ref({ start: '2026-06-10', end: '2026-06-18' })
</script>

# NvDatePicker 日期选择器

通过日历浮层选择单个日期。`NvDatePicker` 以 `outline` 触发器配合品牌着色的日历单元。

## 基础用法

<Demo>
  <div style="max-width: 240px">
    <NvDatePicker v-model="planDate" placeholder="选择日期" />
  </div>
</Demo>

```vue
<NvDatePicker v-model="planDate" placeholder="选择日期" />
```

## 占位与禁用

<Demo>
  <div style="display:flex;flex-direction:column;gap:12px;max-width:240px">
    <NvDatePicker v-model="emptyDate" placeholder="计划开工日期" />
    <NvDatePicker :model-value="'2026-06-18'" :disabled="true" />
  </div>
</Demo>

```vue
<NvDatePicker v-model="emptyDate" placeholder="计划开工日期" />
<NvDatePicker v-model="planDate" disabled />
```

## 可清除与宽度

宽度完全由调用方的 `class` 给：`NvDatePicker` 与 `NvDateRangePicker` 都不自带任何断点宽度，所以外层字段壳声明的宽度和触发器渲染出来的宽度必然一致，不会互相覆盖。窄屏占满、到断点收成固定宽度是本组件的通行写法（与筛选条里的搜索框、下拉框同一套）。

**壳与控件的断点值必须一致**：壳给 `sm:w-44`、控件只给 `sm:w-36` 时，控件是 144px——`2026-09-15` 加上清除叉放不下，会被 `truncate` 截掉一截还压在叉上。改宽度时两边一起改。

`clearable` 打开后，触发按钮上出现清除叉，日历浮层底部也多一个「清除」。清除回传**空字符串**（不是 `null`），所以「选一次再退回全部」不需要刷新页面。给选了日期的格子留宽度时按「`YYYY-MM-DD`（约 72px）＋ 日历图标 16px ＋ 清除叉 20px ＋ 两侧内边距 32px ＋ 叉那一档让位 56px」估，`sm:w-48`（192px）够。

<Demo>
  <div style="display:flex;gap:12px;flex-wrap:wrap">
    <div style="max-width: 200px">
      <NvDatePicker v-model="planDate" placeholder="选择日期" clearable class="w-full sm:w-44" />
    </div>
  </div>
</Demo>

```vue
<NvDatePicker v-model="planDate" placeholder="选择日期" clearable class="w-full sm:w-44" />
```

## 日期范围 DateRangePicker

`NvDateRangePicker` 选择起止区间：首次点击定起点，再次点击定终点（自动排序），悬停可实时预览跨度。模型是 `{ start, end }` 字符串对象。

<Demo>
  <div style="max-width: 280px">
    <NvDateRangePicker v-model="range" placeholder="选择日期范围" />
  </div>
</Demo>

```vue
<script setup>
import { NvDateRangePicker } from '@nerv-iip/ui'
import { ref } from 'vue'

const range = ref({ start: '2026-06-10', end: '2026-06-18' })
</script>

<template>
  <NvDateRangePicker v-model="range" placeholder="选择日期范围" />
</template>
```

## 属性

### NvDatePicker

| 属性               | 说明                             | 类型                      | 默认       |
| ------------------ | -------------------------------- | ------------------------- | ---------- |
| `v-model`          | 绑定日期（`YYYY-MM-DD` 字符串）  | `string \| null`          | `null`     |
| `id`               | 触发按钮 ID，用于关联字段标签    | `string`                  | —          |
| `placeholder`      | 未选中占位文本                   | `string`                  | `选择日期` |
| `disabled`         | 是否禁用                         | `boolean`                 | `false`    |
| `clearable`        | 是否允许清除已选日期（回传空串） | `boolean`                 | `false`    |
| `aria-invalid`     | 字段是否处于校验失败状态         | `boolean`                 | `false`    |
| `aria-describedby` | 关联字段错误说明元素的 ID        | `string`                  | —          |
| `class`            | 触发按钮宽度                     | `HTMLAttributes['class']` | —          |

### NvDateRangePicker

| 属性          | 说明           | 类型                                                     | 默认           |
| ------------- | -------------- | -------------------------------------------------------- | -------------- |
| `v-model`     | 绑定区间       | `{ start: string \| null, end: string \| null } \| null` | `null`         |
| `placeholder` | 未选中占位文本 | `string`                                                 | `选择日期范围` |
| `disabled`    | 是否禁用       | `boolean`                                                | `false`        |
| `class`       | 触发按钮宽度   | `HTMLAttributes['class']`                                | —              |
