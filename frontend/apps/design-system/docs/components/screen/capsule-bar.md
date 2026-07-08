---
title: CapsuleBar 胶囊进度
---

<script setup>
import { ref } from 'vue'
import { NvCapsuleBar, NvScreenPanel } from '@nerv-iip/ui'

const lines = ref([
  { label: '焊接线 A', value: 93, tone: 'cyan' },
  { label: '装配线 B', value: 76, tone: 'indigo' },
  { label: 'CNC 线 C', value: 41, tone: 'amber' },
  { label: '涂装线 D', value: 88, tone: 'green' },
])

const oee = ref([
  { label: '可用率', value: 95, tone: 'cyan' },
  { label: '性能率', value: 89, tone: 'green' },
  { label: '良品率', value: 99, tone: 'green' },
  { label: '不良率', value: 12, tone: 'red' },
])
</script>

# CapsuleBar 胶囊进度

横向胶囊进度条:每行是标题、圆角轨道与一段按色调辉光的渐变填充,百分比读在右端。填充从钳制在 0–100 的值生长。`tone` 承载语义(青/靛/绿/琥珀/红),但数值始终并列显示。基于独立的 `--sb-*` 工业蓝令牌。

::: tip 容器
`NvCapsuleBar` 是「裸内容」组件,本身不带边框背景,通常放进 [`NvScreenPanel`](./screen-panel) 容器里使用。
:::

## 基础用法

`items` 是行数组,每行 `{ label, value, tone? }`。`tone` 缺省为 `cyan`。

<ScreenDemo>
  <NvScreenPanel title="各产线稼动率" style="width: 360px">
    <NvCapsuleBar :items="lines" />
  </NvScreenPanel>
</ScreenDemo>

```vue
<script setup>
const lines = [
  { label: '焊接线 A', value: 93, tone: 'cyan' },
  { label: '装配线 B', value: 76, tone: 'indigo' },
  { label: 'CNC 线 C', value: 41, tone: 'amber' },
  { label: '涂装线 D', value: 88, tone: 'green' },
]
</script>

<NvScreenPanel title="各产线稼动率">
  <NvCapsuleBar :items="lines" />
</NvScreenPanel>
```

## 色调表意

用色调区分 OEE 分项:绿色良性、红色警示(不良率)。

<ScreenDemo>
  <NvScreenPanel title="OEE 分解" style="width: 360px">
    <NvCapsuleBar :items="oee" />
  </NvScreenPanel>
</ScreenDemo>

```vue
<script setup>
const oee = [
  { label: '可用率', value: 95, tone: 'cyan' },
  { label: '性能率', value: 89, tone: 'green' },
  { label: '良品率', value: 99, tone: 'green' },
  { label: '不良率', value: 12, tone: 'red' },
]
</script>

<NvCapsuleBar :items="oee" />
```

## 属性

| 属性     | 说明             | 类型            | 默认              |
| -------- | ---------------- | --------------- | ----------------- |
| `items`  | 行数组,见下表    | `CapsuleItem[]` | 内置 4 行产线示例 |
| `suffix` | 追加到每个数值后 | `string`        | `'%'`             |

`CapsuleItem` 结构:

| 字段    | 说明         | 类型                                                | 默认     |
| ------- | ------------ | --------------------------------------------------- | -------- |
| `label` | 行标题       | `string`                                            | —        |
| `value` | 填充值,0–100 | `number`                                            | —        |
| `tone`  | 条色调       | `'cyan' \| 'indigo' \| 'green' \| 'amber' \| 'red'` | `'cyan'` |
