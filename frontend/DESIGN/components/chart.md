# Chart (NvAreaChart / NvLineChart / NvBarChart / NvDonutChart)

应用页面使用从 `@nerv-iip/ui` 导入的品牌图表组件：

- `NvLineChart` / `NvAreaChart` — 时间趋势。属性：`data`（行对象）、`xKey`、
  `series: LineSeries[]`（`{ key, label, color? }`）、`height`、`valueSuffix`。
- `NvBarChart` — 类别比较（`BarSeries`）。
- `NvDonutChart` — 整体占比（`DonutSlice`）。

shadcn 风格的图表壳层（shell）（`ChartContainer`、`ChartTooltipContent`、
`ChartLegendContent`、`ChartConfig`）属于原版，仅限组件库内部使用；`Nv*`
图表已对其完成封装。不得在应用代码中组合该 shell。

## 契约

1. 系列颜色默认使用语义化图表令牌（token）`var(--chart-1)` … `var(--chart-5)`；仅在具有领域语义的覆盖场景传入 `color`，不得使用原始十六进制（hex）值。
2. 支持的图表形态为折线/面积、柱状和环形。不得在应用代码中新增第二套图表抽象。
3. 图表外围的加载、空数据和错误状态应使用 `Skeleton`、`Empty`、`Alert` 和 `NvLoader`；图表本身只渲染数据。
4. 图例和工具提示在密集面板中必须保持可读。
5. 大屏界面不得使用这些组件；screen 层有自己的图表（`NvScreenBarChart`、`NvScreenTrendChart`、`NvScreenDonut`、`NvSparkline`，…）。

## 用法

```vue
<script setup lang="ts">
import { NvLineChart, type LineSeries } from '@nerv-iip/ui'

const series: LineSeries[] = [
  { key: 'planned', label: 'Planned' },
  { key: 'actual', label: 'Actual' },
]
</script>

<template>
  <NvLineChart :data="rows" x-key="date" :series="series" :height="260" />
</template>
```
