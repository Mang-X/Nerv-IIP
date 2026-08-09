# Date Picker (NvDatePicker / NvDateRangePicker / NvTimePicker)

桌面产品 UI 的日期/时间控件，从 `@nerv-iip/ui` 导出：

- `NvDatePicker` — 单个日期。`v-model` 为 `YYYY-MM-DD` 字符串或 `null`；
  props 为 `placeholder`、`disabled`。
- `NvDateRangePicker` — 日期范围。`v-model` 为 `DateRange`
  (`{ start: string | null, end: string | null }`) 或 `null`。
- `NvTimePicker` — 用于排程/时间窗口输入的时间选择。

无前缀的 `DatePicker` / `DateRangePicker`（及其 `DateRangeValue` 类型）以及
`Calendar` / `RangeCalendar` 是原版 / 底层导出，仅限组件库内部使用；
不得在应用代码中使用。

## 契约

1. 值保持为兼容 DateOnly 的字符串；消费者在应用边界将其转换为 endpoint DTO。
2. 禁用和可清除状态由 props 处理；页面不得重新实现清除按钮。
3. 触发器保持紧凑，适用于工具栏筛选器和表单字段。

## 用法

```vue
<script setup lang="ts">
import { ref } from 'vue'
import { NvDatePicker, NvDateRangePicker, type DateRange } from '@nerv-iip/ui'

const dueDate = ref<string | null>(null)
const plannedWindow = ref<DateRange | null>(null)
</script>

<template>
  <NvDatePicker v-model="dueDate" />
  <NvDateRangePicker v-model="plannedWindow" />
</template>
```

## 规则

- 不得在应用代码中从深层路径（deep path）导入日历或 Popover 内部实现；应使用 `@nerv-iip/ui` 的桶导出（barrel export）。
- 不得手动组合 Popover + 原生日期输入；该前 NvUI 模式已由 `NvDatePicker` / `NvDateRangePicker` 取代。
