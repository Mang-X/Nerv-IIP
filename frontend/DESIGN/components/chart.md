# Chart (NvAreaChart / NvLineChart / NvBarChart / NvDonutChart)

App pages use the branded chart components from `@nerv-iip/ui`:

- `NvLineChart` / `NvAreaChart` — trends over time. Props: `data` (row
  objects), `xKey`, `series: LineSeries[]` (`{ key, label, color? }`),
  `height`, `valueSuffix`.
- `NvBarChart` — categorical comparison (`BarSeries`).
- `NvDonutChart` — share-of-whole (`DonutSlice`).

The shadcn-style chart shell (`ChartContainer`, `ChartTooltipContent`,
`ChartLegendContent`, `ChartConfig`) is 原版 and library-internal — the `Nv*`
charts already wrap it. Do not compose the shell in app code.

## Contract

1. Series colors default to the semantic chart tokens `var(--chart-1)` … `var(--chart-5)`; pass `color` only for domain-meaningful overrides, never raw hex.
2. The supported shapes are line/area, bar, and donut. Do not add a second chart abstraction in app code.
3. Loading, empty, and error states use `Skeleton`, `Empty`, `Alert`, and `NvLoader` around the chart — the chart itself renders data only.
4. Legends and tooltips must remain readable in dense panels.
5. Big-board surfaces do NOT use these — the screen layer has its own charts (`NvScreenBarChart`, `NvScreenTrendChart`, `NvScreenDonut`, `NvSparkline`, …).

## Usage

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
