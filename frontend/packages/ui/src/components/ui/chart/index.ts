import type { Component } from 'vue'
export { default as ChartContainer } from './ChartContainer.vue'
export { default as ChartLegendContent } from './ChartLegendContent.vue'
export { default as ChartTooltipContent } from './ChartTooltipContent.vue'
export type ChartConfig = Record<string, { label?: string | Component, icon?: string | Component, color?: string }>
