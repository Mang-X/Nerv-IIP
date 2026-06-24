---
title: ScreenTable 数据表格
---

<script setup>
import { ScreenTable, StatusTag } from '@nerv-iip/ui'

const columns = [
  { key: 'wo', label: '工单号' },
  { key: 'product', label: '产品' },
  { key: 'plan', label: '计划', align: 'right' },
  { key: 'actual', label: '实际', align: 'right' },
  { key: 'status', label: '状态', align: 'center' },
]
const rows = [
  { wo: 'WO-2406-0312', product: '车架总成', plan: '1,240', actual: '1,156', status: 'run' },
  { wo: 'WO-2406-0308', product: '门板组件', plan: '980', actual: '742', status: 'idle' },
  { wo: 'WO-2406-0301', product: '齿轮箱体', plan: '760', actual: '312', status: 'alarm' },
  { wo: 'WO-2406-0297', product: '外壳面板', plan: '1,080', actual: '1,024', status: 'run' },
]
const tones = { run: '运行中', idle: '待机', alarm: '报警' }
</script>

# ScreenTable 数据表格

大屏数据表:微微发光的表头行压在细线正文行上(无竖向分隔),行随悬停点亮。列各自设对齐方式;任意单元格可被 `#cell-<key>` 插槽接管,用于状态点、等宽编码等。纯数据驱动 —— `columns` + `rows` 即可渲染。基于独立的 `--sb-*` 令牌。

## 基础用法

`columns` 定义列(`align` 控制对齐),`rows` 是数据,`rowKey` 指明唯一列。

<ScreenDemo wide>
  <ScreenTable :columns="columns" :rows="rows" row-key="wo">
    <template #cell-status="{ value }">
      <StatusTag :tone="value">{{ tones[value] }}</StatusTag>
    </template>
  </ScreenTable>
</ScreenDemo>

```vue
<script setup>
const columns = [
  { key: 'wo', label: '工单号' },
  { key: 'product', label: '产品' },
  { key: 'plan', label: '计划', align: 'right' },
  { key: 'actual', label: '实际', align: 'right' },
  { key: 'status', label: '状态', align: 'center' },
]
const rows = [
  { wo: 'WO-2406-0312', product: '车架总成', plan: '1,240', actual: '1,156', status: 'run' },
  // …
]
</script>

<template>
  <ScreenTable :columns="columns" :rows="rows" row-key="wo">
    <!-- 用 #cell-<key> 插槽渲染状态标签 -->
    <template #cell-status="{ value }">
      <StatusTag :tone="value">{{ tones[value] }}</StatusTag>
    </template>
  </ScreenTable>
</template>
```

## 属性

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `columns` | 列定义,`align` 控制单元格对齐 | `{ key: string; label: string; align?: 'left' \| 'center' \| 'right' }[]` | 内置示例列 |
| `rows` | 数据行 | `Record<string, unknown>[]` | 内置示例工单 |
| `rowKey` | 作为 `v-for` key 的唯一列 | `string` | `'wo'` |

## 插槽

| 插槽 | 作用域参数 | 说明 |
|---|---|---|
| `cell-<key>` | `{ row, value }` | 接管对应列的单元格渲染(状态点、等宽编码等);缺省直接输出 `row[key]` |
