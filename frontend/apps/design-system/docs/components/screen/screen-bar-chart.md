---
title: NvScreenBarChart 柱状图
---

<script setup>
import { NvScreenBarChart } from '@nerv-iip/ui'

const inbound = [4, 9, 14, 18, 17, 21, 16, 19, 15, 12, 8, 3]
const outbound = [2, 5, 9, 13, 16, 19, 21, 18, 20, 17, 11, 6]
const hours = Array.from({ length: 12 }, (_, i) => `${String(8 + i).padStart(2, '0')}:00`)
</script>

# NvScreenBarChart 柱状图

纵向柱状图——小时流量 / 日产量这类**离散量**的正确形态("一段一个数",柱比面积曲线诚实)。支持 1–2 条序列并排对比;柱体语义色**上实下消**渐隐(与数字强调线同源语言,静态不发光);悬停整列高亮 + HTML 信息卡逐序列读数(文字层不随 SVG 拉伸变形)。基于独立的 `--sb-*` 令牌。

## 基础用法

`series` 传 `{ label, color, data }[]`;高度由外层容器决定。

<ScreenDemo>
  <div style="height: 150px">
    <NvScreenBarChart
      :series="[{ label: '入库行', color: '#4aa6ee', data: inbound }]"
      :hover-labels="hours"
      :x-labels="[hours[0], hours[6], '现在']"
    />
  </div>
</ScreenDemo>

```vue
<template>
  <div style="height: 150px">
    <NvScreenBarChart
      :series="[{ label: '入库行', color: '#4aa6ee', data: hourly }]"
      :hover-labels="hourLabels"
      :x-labels="[hourLabels[0], hourLabels[6], '现在']"
    />
  </div>
</template>
```

## 双序列对比

两条序列按组并排(如出入库对照)。

<ScreenDemo>
  <div style="height: 170px">
    <NvScreenBarChart
      :series="[
        { label: '入库行', color: '#4aa6ee', data: inbound },
        { label: '出库行', color: '#8b9be6', data: outbound },
      ]"
      :hover-labels="hours"
      :x-labels="[hours[0], hours[6], hours[11]]"
    />
  </div>
</ScreenDemo>

```vue
<NvScreenBarChart
  :series="[
    { label: '入库行', color: '#4aa6ee', data: inbound },
    { label: '出库行', color: '#8b9be6', data: outbound },
  ]"
  :hover-labels="hourLabels"
/>
```

## 自动巡显

`autoplay` 开启后信息卡逐列自动巡显(挂墙无人操作也能读到每一格的数);鼠标悬停即暂停、移出恢复。`prefers-reduced-motion` 下不自动巡显。

<ScreenDemo>
  <div style="height: 150px">
    <NvScreenBarChart
      :series="[{ label: '出库行', color: '#8b9be6', data: outbound }]"
      :hover-labels="hours"
      autoplay
      :autoplay-ms="2000"
    />
  </div>
</ScreenDemo>

```vue
<NvScreenBarChart :series="series" :hover-labels="hourLabels" autoplay :autoplay-ms="2400" />
```

## API

| Prop          | 类型                       | 默认    | 说明                                |
| ------------- | -------------------------- | ------- | ----------------------------------- |
| `series`      | `{ label, color, data }[]` | —       | 1–2 条序列(并排分组柱),`data` 等长  |
| `xLabels`     | `string[]`                 | `[]`    | X 轴下标签(可稀疏,均匀分布)         |
| `hoverLabels` | `string[]`                 | —       | 悬停标签(与 `data` 等长;缺省用序号) |
| `autoplay`    | `boolean`                  | `false` | 信息卡自动巡显(用户悬停时暂停)      |
| `autoplayMs`  | `number`                   | `2400`  | 巡显间隔(毫秒)                      |
