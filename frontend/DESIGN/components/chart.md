# Chart

Charts use a shadcn-style chart shell. `@nerv-iip/ui` owns the chart container, tooltip content, legend content, and semantic token bridge; business pages may bring a chart engine such as Unovis later, but the base design-system package does not depend on one.

## Exports

- `ChartContainer`
- `ChartTooltipContent`
- `ChartLegendContent`
- `ChartConfig`

## Contract

1. Chart config colors must use semantic chart tokens such as `var(--chart-1)` through `var(--chart-5)`.
2. The first dashboard shapes are line, bar, and donut/pie. Do not add a second chart abstraction.
3. Loading, empty, and error states use `Skeleton`, `Empty`, `Alert`, and `Spinner`.
4. Legends and tooltips must remain readable in dense panels.
5. Page code may introduce a chart engine adapter later, but all chart shell pieces come from `@nerv-iip/ui`.

## Usage

```vue
<script setup lang="ts">
import type { ChartConfig } from '@nerv-iip/ui'
import { ChartContainer } from '@nerv-iip/ui'

const chartConfig = {
  planned: { label: 'Planned', color: 'var(--chart-1)' },
  actual: { label: 'Actual', color: 'var(--chart-2)' },
} satisfies ChartConfig
</script>

<template>
  <ChartContainer :config="chartConfig" class="min-h-48">
    <!-- Page-level chart engine goes here. -->
  </ChartContainer>
</template>
```
