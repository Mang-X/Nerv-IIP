---
title: KpiBar 指标横条
---

<script setup>
import { NvKpiBar } from '@nerv-iip/ui'
import { ClipboardList, ListChecks, ClipboardCheck, AlertTriangle, Zap, Activity } from 'lucide-vue-next'

const items = [
  { icon: ClipboardList, value: '24', label: '工单总数' },
  { icon: ListChecks, value: '8', label: '进行中' },
  { icon: ClipboardCheck, value: '16', label: '已完成' },
  { value: '93.2%', label: '良品率', tone: 'cyan', ring: 93.2 },
  { icon: AlertTriangle, value: '36', label: '不良数', tone: 'amber' },
  { icon: Zap, value: '1,284 kWh', label: '能耗电量' },
  { icon: Activity, value: '正常', label: '系统状态', tone: 'green' },
]
</script>

# NvKpiBar 指标横条

大屏底部的 KPI 横条:一排等宽单元,以发丝竖线分隔。每个单元由一枚 lucide 图标(圆角微辉光底块)配数值与标签组成;其中一个单元可用进度环替代图标 —— 适合「良品率」这类百分比指标。`tone` 决定强调色。组件自带边框与底色,横向铺满,放进 `<ScreenDemo wide>` 即可,无需额外容器。

## 默认横条

不传 `items` 时使用内置示例。下例传入一组真实工厂指标,其中「良品率」用 `ring` 渲染为进度环。

<ScreenDemo wide>
  <NvKpiBar :items="items" />
</ScreenDemo>

```vue
<script setup>
import { NvKpiBar } from '@nerv-iip/ui'
import {
  ClipboardList,
  ListChecks,
  ClipboardCheck,
  AlertTriangle,
  Zap,
  Activity,
} from 'lucide-vue-next'

const items = [
  { icon: ClipboardList, value: '24', label: '工单总数' },
  { icon: ListChecks, value: '8', label: '进行中' },
  { icon: ClipboardCheck, value: '16', label: '已完成' },
  { value: '93.2%', label: '良品率', tone: 'cyan', ring: 93.2 },
  { icon: AlertTriangle, value: '36', label: '不良数', tone: 'amber' },
  { icon: Zap, value: '1,284 kWh', label: '能耗电量' },
  { icon: Activity, value: '正常', label: '系统状态', tone: 'green' },
]
</script>

<NvKpiBar :items="items" />
```

## 属性

| 属性    | 说明         | 类型    | 默认          |
| ------- | ------------ | ------- | ------------- |
| `items` | KPI 单元数组 | `Kpi[]` | 内置 8 项示例 |

### `Kpi` 单元

| 字段    | 说明                                 | 类型                     | 默认   |
| ------- | ------------------------------------ | ------------------------ | ------ |
| `value` | 数值文本                             | `string`                 | —      |
| `label` | 数值下方标签                         | `string`                 | —      |
| `icon`  | lucide 图标组件;设了 `ring` 时省略   | `Component`              | —      |
| `tone`  | 强调色,着色图标底块与数值            | `cyan \| amber \| green` | `cyan` |
| `ring`  | `0–100`,渲染进度环替代图标(如良品率) | `number`                 | —      |
