---
title: Chart 图表
---

<script setup>
import { NvAreaChart, NvLineChart, NvBarChart, NvDonutChart } from '@nerv-iip/ui'

const outputSeries = [
  { label: '08:00', value: 420 },
  { label: '10:00', value: 680 },
  { label: '12:00', value: 1240 },
  { label: '14:00', value: 1880 },
  { label: '16:00', value: 2610 },
  { label: '18:00', value: 3180 },
  { label: '20:00', value: 4210 },
]

const planActual = [
  { day: '周一', plan: 900, actual: 860 },
  { day: '周二', plan: 950, actual: 980 },
  { day: '周三', plan: 1000, actual: 940 },
  { day: '周四', plan: 1000, actual: 1060 },
  { day: '周五', plan: 1100, actual: 1020 },
  { day: '周六', plan: 700, actual: 720 },
]
const planActualSeries = [
  { key: 'plan', label: '计划' },
  { key: 'actual', label: '实际' },
]

const outputByCenter = [
  { center: 'CNC-07', good: 412, scrap: 18 },
  { center: 'FORGE-02', good: 1200, scrap: 24 },
  { center: 'CNC-11', good: 96, scrap: 6 },
  { center: 'ASM-04', good: 188, scrap: 12 },
  { center: 'STAMP-01', good: 2740, scrap: 60 },
]
const outputByCenterSeries = [
  { key: 'good', label: '良品' },
  { key: 'scrap', label: '废品' },
]

const statusMix = [
  { label: '执行中', value: 38 },
  { label: '已完成', value: 52 },
  { label: '待处理', value: 22 },
  { label: '阻塞', value: 6 },
]
</script>

# Chart 图表

基于 unovis 的一组工厂场景图表：面积、折线、柱状、环形。颜色取自设计令牌，随运行时品牌色重新着色。

## 面积图 AreaChartPro

单系列累计趋势，适合当班产出等时间序列。

<Demo>
  <div class="w-full">
    <NvAreaChart :data="outputSeries" :height="220" value-suffix=" 件" />
  </div>
</Demo>

```vue
<script setup>
const outputSeries = [
  { label: '08:00', value: 420 },
  { label: '10:00', value: 680 },
  { label: '12:00', value: 1240 },
  { label: '20:00', value: 4210 },
]
</script>

<template>
  <NvAreaChart :data="outputSeries" :height="220" value-suffix=" 件" />
</template>
```

## 折线图 LineChartPro

多系列对比，例如计划 vs 实际产量。

<Demo>
  <div class="w-full">
    <NvLineChart :data="planActual" x-key="day" :series="planActualSeries" :height="220" value-suffix=" 件" />
  </div>
</Demo>

```vue
<script setup>
const planActual = [
  { day: '周一', plan: 900, actual: 860 },
  { day: '周二', plan: 950, actual: 980 },
]
const planActualSeries = [
  { key: 'plan', label: '计划' },
  { key: 'actual', label: '实际' },
]
</script>

<template>
  <NvLineChart
    :data="planActual"
    x-key="day"
    :series="planActualSeries"
    :height="220"
    value-suffix=" 件"
  />
</template>
```

## 柱状图 BarChartPro

分类（可分组）对比，例如各工作中心的良品 / 废品。

<Demo>
  <div class="w-full">
    <NvBarChart :data="outputByCenter" x-key="center" :series="outputByCenterSeries" :height="220" />
  </div>
</Demo>

```vue
<script setup>
const outputByCenter = [
  { center: 'CNC-07', good: 412, scrap: 18 },
  { center: 'FORGE-02', good: 1200, scrap: 24 },
]
const outputByCenterSeries = [
  { key: 'good', label: '良品' },
  { key: 'scrap', label: '废品' },
]
</script>

<template>
  <NvBarChart :data="outputByCenter" x-key="center" :series="outputByCenterSeries" :height="220" />
</template>
```

## 环形图 DonutChartPro

占比构成，例如工单状态分布，支持中心标签。

<Demo>
  <div class="w-full max-w-sm">
    <NvDonutChart :data="statusMix" :height="180" central-label="118" central-sub-label="工单" />
  </div>
</Demo>

```vue
<script setup>
const statusMix = [
  { label: '执行中', value: 38 },
  { label: '已完成', value: 52 },
  { label: '待处理', value: 22 },
  { label: '阻塞', value: 6 },
]
</script>

<template>
  <NvDonutChart :data="statusMix" :height="180" central-label="118" central-sub-label="工单" />
</template>
```

## 属性

### AreaChartPro

| 属性          | 说明                           | 类型                                 | 默认    |
| ------------- | ------------------------------ | ------------------------------------ | ------- |
| `data`        | 数据点数组                     | `{ label: string; value: number }[]` | —       |
| `height`      | 高度（px）                     | `number`                             | `240`   |
| `valueSuffix` | 数值后缀                       | `string`                             | `''`    |
| `minimal`     | 迷你模式（无轴，适合卡内趋势） | `boolean`                            | `false` |

### LineChartPro / BarChartPro

| 属性          | 说明       | 类型                                       | 默认  |
| ------------- | ---------- | ------------------------------------------ | ----- |
| `data`        | 行数据数组 | `Record<string, number \| string>[]`       | —     |
| `xKey`        | X 轴字段名 | `string`                                   | —     |
| `series`      | 系列定义   | `{ key: string; label: string; color? }[]` | —     |
| `height`      | 高度（px） | `number`                                   | `260` |
| `valueSuffix` | 数值后缀   | `string`                                   | `''`  |

### DonutChartPro

| 属性              | 说明       | 类型                                         | 默认  |
| ----------------- | ---------- | -------------------------------------------- | ----- |
| `data`            | 分片数组   | `{ label: string; value: number; color? }[]` | —     |
| `height`          | 高度（px） | `number`                                     | `200` |
| `centralLabel`    | 中心主标签 | `string`                                     | —     |
| `centralSubLabel` | 中心副标签 | `string`                                     | —     |
