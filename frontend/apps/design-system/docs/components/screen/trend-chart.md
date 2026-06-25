---
title: TrendChart 趋势图
---

<script setup>
import { ref } from 'vue'
import { TrendChart } from '@nerv-iip/ui'

const actual = ref([120, 360, 640, 760, 980, 880, 1086, 760, 940, 910, 930, 910])
const plan = ref([140, 420, 700, 900, 1010, 1080, 1150, 1180, 1220, 1260, 1320, 1380])
</script>

# TrendChart 趋势图

产量趋势图:辉光青色实际线压在柔和面积填充之上,叠一条虚线靛色计划线和极淡的虚线网格。可选十字光标 —— 一道虚线竖标、实际点上的辉光圆点、以及读出双序列的暗色信息卡。所有路径由 `actual` / `plan` 计算,y 轴刻度从数据向上取整,调用方传原始数值即可。基于独立的 `--sb-*` 工业蓝令牌。

::: tip 容器
`TrendChart` **自带** [`ScreenPanel`](./screen-panel) 容器,直接使用即可。宽组件用 `<ScreenDemo wide>` 占满整列。
:::

## 基础用法

不传任何 props 时使用内置的全天产量示例(实际 vs 计划,十字光标钉在 10:00)。

<ScreenDemo wide>
  <TrendChart />
</ScreenDemo>

```vue
<TrendChart />
```

## 数据驱动

传入 `actual` / `plan` 两条序列,以及 `title`、`tooltip`,光标 `x` 是要钉住的数据下标。

<ScreenDemo wide>
  <TrendChart
    title="装配线 B 产量趋势（件）"
    :actual="actual"
    :plan="plan"
    :tooltip="{ x: 6, label: '12:00', actual: '1,086', plan: '1,150' }"
  />
</ScreenDemo>

```vue
<script setup>
const actual = [120, 360, 640, 760, 980, 880, 1086, 760, 940, 910, 930, 910]
const plan = [140, 420, 700, 900, 1010, 1080, 1150, 1180, 1220, 1260, 1320, 1380]
</script>

<TrendChart
  title="装配线 B 产量趋势（件）"
  :actual="actual"
  :plan="plan"
  :tooltip="{ x: 6, label: '12:00', actual: '1,086', plan: '1,150' }"
/>
```

## 属性

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `actual` | 各 x 刻度的实际产量(青色实线) | `number[]` | 内置示例序列 |
| `plan` | 各 x 刻度的计划产量(靛色虚线) | `number[]` | 内置示例序列 |
| `yLabels` | y 轴标签,上→下(纯视觉,刻度由数据推导) | `string[]` | `['1,500', '1,200', '900', '600', '300', '0']` |
| `xLabels` | 图下方 x 轴标签 | `string[]` | `['00:00', '04:00', '08:00', '12:00', '16:00', '20:00', '24:00']` |
| `tooltip` | 十字光标 + 信息卡,`x` 是钉住的数据下标 | `{ x: number; label: string; actual: string; plan: string }` | 钉在下标 6(10:00) |
| `title` | 面板标题 | `string` | `'产量趋势（件）'` |
