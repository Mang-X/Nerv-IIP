---
title: NvScreenTable 数据表格
---

<script setup>
import { NvScreenButton, NvScreenPagination, NvScreenPanel, NvScreenSearch, NvScreenSelect, NvScreenTable, NvScreenStatusTag } from '@nerv-iip/ui'
import { ref } from 'vue'

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

// 完整数据表格示例:筛选 + 操作 + 分页
const keyword = ref('')
const lineFilter = ref('all')
const page = ref(1)
const lineOptions = [
  { label: '全部产线', value: 'all' },
  { label: '焊接线 A', value: 'a' },
  { label: '装配线 B', value: 'b' },
  { label: 'CNC 线 C', value: 'c' },
]
const fullColumns = [
  { key: 'wo', label: '工单号' },
  { key: 'product', label: '产品' },
  { key: 'actual', label: '实际', align: 'right' },
  { key: 'status', label: '状态', align: 'center' },
  { key: 'action', label: '操作', align: 'center' },
]
</script>

# NvScreenTable 数据表格

大屏数据表:微微发光的表头行压在细线正文行上(无竖向分隔),行随悬停点亮。列各自设对齐方式;任意单元格可被 `#cell-<key>` 插槽接管,用于状态点、等宽编码等。纯数据驱动 —— `columns` + `rows` 即可渲染。基于独立的 `--sb-*` 令牌。

## 基础用法

`columns` 定义列(`align` 控制对齐),`rows` 是数据,`rowKey` 指明唯一列。

<ScreenDemo wide>
  <NvScreenTable :columns="columns" :rows="rows" row-key="wo">
    <template #cell-status="{ value }">
      <NvScreenStatusTag :tone="value">{{ tones[value] }}</NvScreenStatusTag>
    </template>
  </NvScreenTable>
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
  <NvScreenTable :columns="columns" :rows="rows" row-key="wo">
    <!-- 用 #cell-<key> 插槽渲染状态标签 -->
    <template #cell-status="{ value }">
      <NvScreenStatusTag :tone="value">{{ tones[value] }}</NvScreenStatusTag>
    </template>
  </NvScreenTable>
</template>
```

## 完整数据表格（筛选 · 操作 · 分页）

配合 `NvScreenSearch` / `NvScreenSelect` 组成筛选栏,`#cell-action` 插槽放 `NvScreenButton size="sm"` 行内操作,底部接 `NvScreenPagination`。下拉浮层 `Teleport` 到 `<body>`,不会被面板 `overflow` 裁切。

<ScreenDemo wide>
  <NvScreenPanel>
    <div style="display:flex;gap:10px;margin-bottom:14px;flex-wrap:wrap;align-items:center">
      <div style="flex:1;min-width:200px"><NvScreenSearch v-model="keyword" placeholder="搜索工单号或产品" /></div>
      <div style="width:160px"><NvScreenSelect v-model="lineFilter" :options="lineOptions" /></div>
      <NvScreenButton variant="secondary">导出</NvScreenButton>
    </div>
    <NvScreenTable :columns="fullColumns" :rows="rows" row-key="wo">
      <template #cell-status="{ value }">
        <NvScreenStatusTag :tone="value">{{ tones[value] }}</NvScreenStatusTag>
      </template>
      <template #cell-action>
        <NvScreenButton variant="ghost" size="sm">详情</NvScreenButton>
      </template>
    </NvScreenTable>
    <div style="margin-top:14px">
      <NvScreenPagination v-model:page="page" :total="248" :page-size="4" />
    </div>
  </NvScreenPanel>
</ScreenDemo>

## 属性

| 属性      | 说明                          | 类型                                                                      | 默认         |
| --------- | ----------------------------- | ------------------------------------------------------------------------- | ------------ |
| `columns` | 列定义,`align` 控制单元格对齐 | `{ key: string; label: string; align?: 'left' \| 'center' \| 'right' }[]` | 内置示例列   |
| `rows`    | 数据行                        | `Record<string, unknown>[]`                                               | 内置示例工单 |
| `rowKey`  | 作为 `v-for` key 的唯一列     | `string`                                                                  | `'wo'`       |

## 插槽

| 插槽         | 作用域参数       | 说明                                                               |
| ------------ | ---------------- | ------------------------------------------------------------------ |
| `cell-<key>` | `{ row, value }` | 接管对应列的单元格渲染(状态点、等宽编码等);缺省直接输出 `row[key]` |
