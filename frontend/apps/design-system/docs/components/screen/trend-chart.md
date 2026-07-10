---
title: NvScreenTrendChart 趋势图
---

<script setup>
import { ref } from 'vue'
import { NvScreenTrendChart } from '@nerv-iip/ui'

const actual = ref([120, 360, 640, 760, 980, 880, 1086, 760, 940, 910, 930, 910])
const plan = ref([140, 420, 700, 900, 1010, 1080, 1150, 1180, 1220, 1260, 1320, 1380])

// 多序列示例：车间累计 + 各产线分线（Σ 各线 = 总线）
const wsTotal = ref([0, 620, 1280, 1900, 2540, 3120, 3735])
const wsPlan = ref([0, 730, 1460, 2190, 2920, 3650, 4386])
const wsSeries = ref([
  { label: '电芯线', color: '#ef5a63', data: [0, 260, 520, 760, 980, 1150, 1228] },
  { label: '电芯二线', color: '#f0ad4e', data: [0, 220, 470, 720, 980, 1220, 1353] },
  { label: 'PACK 线', color: '#4aa6ee', data: [0, 140, 290, 420, 580, 750, 1154] },
])
</script>

# NvScreenTrendChart 趋势图

产量趋势图:辉光青色实际线压在柔和面积填充之上,叠一条虚线靛色计划线和极淡的虚线网格。可选十字光标 —— 一道虚线竖标、实际点上的辉光圆点、以及读出双序列的暗色信息卡。所有路径由 `actual` / `plan` 计算,y 轴刻度从数据向上取整,调用方传原始数值即可。基于独立的 `--nv-scr-*` 工业蓝令牌。

::: tip 容器
`NvScreenTrendChart` **自带** [`NvScreenPanel`](./screen-panel) 容器,直接使用即可。宽组件用 `<ScreenDemo wide>` 占满整列。
:::

## 基础用法

不传任何 props 时使用内置的全天产量示例(实际 vs 计划,十字光标钉在 10:00)。

<ScreenDemo wide>
  <NvScreenTrendChart />
</ScreenDemo>

```vue
<NvScreenTrendChart />
```

## 数据驱动

传入 `actual` / `plan` 两条序列,以及 `title`、`tooltip`,光标 `x` 是要钉住的数据下标。

<ScreenDemo wide>
  <NvScreenTrendChart
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

<NvScreenTrendChart
  title="装配线 B 产量趋势（件）"
  :actual="actual"
  :plan="plan"
  :tooltip="{ x: 6, label: '12:00', actual: '1,086', plan: '1,150' }"
/>
```

## 多序列对比

`series` 叠加任意条**对比细线**(主曲线之下、不发光):分层不良率、车间各产线分线累计等。图例自动追加色点项,悬停信息卡逐序列读数,量程纳入序列峰值。图例过长时单行截断(悬停卡有全量),范围切换 tabs 永不被挤出面板头。

<ScreenDemo wide>
  <NvScreenTrendChart
    title="产量趋势"
    :actual="wsTotal"
    :plan="wsPlan"
    :series="wsSeries"
    actual-label="车间累计"
    plan-label="计划"
    :x-labels="['08:00', '10:00', '12:00', '14:00']"
    :y-labels="['5,000', '4,000', '3,000', '2,000', '1,000', '0']"
    :tooltip="{ x: 6, label: '14:22', actual: '3,735', plan: '4,386' }"
    :tabs="false"
  />
</ScreenDemo>

```vue
<NvScreenTrendChart
  :actual="workshopTotal"
  :plan="plan"
  :series="[
    { label: '电芯线', color: '#ef5a63', data: line1 }, // 报警线红
    { label: '电芯二线', color: '#f0ad4e', data: line2 }, // 关注线黄
    { label: 'PACK 线', color: '#4aa6ee', data: line3 },
  ]"
  actual-label="车间累计"
  plan-label="计划"
/>
```

::: tip 颜色传字面量
`series[].color` 建议传字面量色值——CSS 变量可能被调用方**局部重映射**(如质量屏把 `--nv-scr-indigo` 重映射为红线色),细线颜色会被污染。
:::

## 属性

| 属性                        | 说明                                         | 类型                                                         | 默认                                                              |
| --------------------------- | -------------------------------------------- | ------------------------------------------------------------ | ----------------------------------------------------------------- |
| `actual`                    | 各 x 刻度的实际产量(青色实线)                | `number[]`                                                   | 内置示例序列                                                      |
| `plan`                      | 各 x 刻度的计划产量(靛色虚线)                | `number[]`                                                   | 内置示例序列                                                      |
| `series`                    | 附加对比序列(细线 + 图例色点 + 悬停逐条读数) | `{ label, color, data }[]`                                   | —                                                                 |
| `ranges`                    | 真实时间范围切换(经 `v-model:range`)         | `{ label, value }[]`                                         | —                                                                 |
| `hoverLabels`               | 每个数据点的悬停标签(与 `actual` 等长)       | `string[]`                                                   | 按 24h 均匀推算                                                   |
| `actualLabel` / `planLabel` | 双序列图例/悬停名                            | `string`                                                     | 实际产量 / 计划产量                                               |
| `yLabels`                   | y 轴标签,上→下(纯视觉,刻度由数据推导)        | `string[]`                                                   | `['1,500', '1,200', '900', '600', '300', '0']`                    |
| `xLabels`                   | 图下方 x 轴标签                              | `string[]`                                                   | `['00:00', '04:00', '08:00', '12:00', '16:00', '20:00', '24:00']` |
| `tooltip`                   | 十字光标 + 信息卡,`x` 是钉住的数据下标       | `{ x: number; label: string; actual: string; plan: string }` | 钉在下标 6(10:00)                                                 |
| `title`                     | 面板标题                                     | `string`                                                     | `'产量趋势（件）'`                                                |

悬停信息卡与十字点为 **HTML overlay**(百分比锚定 + 右半区翻转 + 纵向钳制)——SVG `preserveAspectRatio="none"` 非均匀拉伸下文字零变形,任何容器宽高比 / NvScreenScaler 缩放都成立。
