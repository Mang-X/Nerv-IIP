---
title: ScreenPareto 帕累托
---

<script setup>
import { ScreenPareto } from '@nerv-iip/ui'

const defects = [
  { label: '极片对齐度超差', sub: '电芯线', count: 46, pct: 32.4 },
  { label: '卷绕张力不良', sub: '电芯线', count: 31, pct: 21.8 },
  { label: '焊点虚焊', sub: '焊装一线', count: 12, pct: 8.5 },
  { label: '面漆橘皮', sub: '面漆线', count: 9, pct: 6.3 },
  { label: '密封圈压伤', sub: '总装一线', count: 6, pct: 4.2 },
]
const downtime = [
  { label: '设备故障', sub: '卷绕机 1#', count: 96, pct: 45.3 },
  { label: '换型调机', sub: '冲压二线', count: 54, pct: 25.5 },
  { label: '缺料等待', sub: '总装二线', count: 31, pct: 14.6 },
]
</script>

# ScreenPareto 帕累托

帕累托 TOP-N——缺陷 / 停机原因 / 不良代码等「**少数关键项**」分析的正确形态:名称 + 发丝级条 + 数量/占比 + **累计占比 Σ**(没有累计不算真帕累托,80/20 集中度要能直接读出)。条长按 TOP1 归一便于对比;轨道即 100% 全量口径,竖刻度标累计位置;TOP1 红 / TOP2 橙强调、其余青色渐弱;填充语义色渐隐(左实右消),不发光、不描边。底部汇总 = TOP 合计 vs 长尾。基于独立的 `--sb-*` 令牌。

## 基础用法

`items` 传降序项(`pct` 为占**全量**的百分比);`total` 传全量总数以显示长尾行。

<ScreenDemo>
  <ScreenPareto :items="defects" :total="142" />
</ScreenDemo>

```vue
<template>
  <ScreenPareto
    :items="[
      { label: '极片对齐度超差', sub: '电芯线', count: 46, pct: 32.4 },
      { label: '卷绕张力不良', sub: '电芯线', count: 31, pct: 21.8 },
      /* … */
    ]"
    :total="142"
  />
</template>
```

## 其他口径（停机原因 / 分钟）

`unit` 换数量单位;不传 `total` 时无长尾行。

<ScreenDemo>
  <ScreenPareto :items="downtime" unit="min" />
</ScreenDemo>

```vue
<ScreenPareto :items="downtimeReasons" unit="min" />
```

## API

| Prop | 类型 | 默认 | 说明 |
| --- | --- | --- | --- |
| `items` | `{ label, sub?, count, pct }[]` | — | 降序项;`pct` 为占全量 % |
| `total` | `number` | — | 全量总数(算长尾行;不传则无长尾行) |
| `unit` | `string` | `件` | 数量单位 |
