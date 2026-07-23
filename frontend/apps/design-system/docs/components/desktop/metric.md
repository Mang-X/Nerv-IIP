---
title: NvMetricCard 指标卡
---

<script setup>
import { NvMetricCard, NvMetricRing, NvMetricStrip } from '@nerv-iip/ui'
import { WrenchIcon, CircleCheckIcon, ClockIcon, TriangleAlertIcon } from '@lucide/vue'
</script>

# NvMetricCard 指标卡

给高频操作台用的 KPI 卡。**下半区永远承载可行动的数据**——一段趋势、一个离目标的差距、一组状态分布、一个处理入口——而不是被填成无意义的描述文本。`variant` 决定下半区结构；语义色沿用 `NvStatusBadge` 五 tone；数字一律 tabular-nums；内联微图带悬浮 tooltip（曲线复用 `NvAreaChart` 原生 crosshair）。

> 平铺的「计划 vs 实际 / 目标 vs 当前」对比卡用 `NvMetricComparison`（自动算差值与达成率）。

## variant 一览

| variant     | 下半区                       | 典型场景                       |
| ----------- | ---------------------------- | ------------------------------ |
| `default`   | 可选曲线（向后兼容旧用法）   | 存量页面沿用                   |
| `icon`      | 无，tone 图标位 + 环比 chip  | 页头一行多张总览，最省纵向空间 |
| `sparkline` | 迷你曲线 + 区间/统计脚注     | 报工量、稼动率趋势             |
| `target`    | 带目标刻度的进度条 + 缺口    | 月产量达成、履约率             |
| `breakdown` | 分段条 + 图例计数（五 tone） | 工单 / 质检 / 批次状态构成     |
| `bars`      | 迷你柱，当前柱强调           | 日产量、日报警次数             |
| `alert`     | tone 浸染卡面 + 处理入口     | 超期工单、临期批次             |
| `facets`    | 维度 chip 组（可点击成筛选） | 待质检批次 = 原料 / 半成品…    |

## icon 图标紧凑型

`tone` 决定左侧图标位配色，`trend` 出右上环比 chip。

<Demo>
  <div class="grid w-full gap-4 sm:grid-cols-2 xl:grid-cols-4">
    <NvMetricCard variant="icon" tone="brand" :icon="WrenchIcon" label="在制工单" :value="38" :trend="{ value: '5', direction: 'up' }" />
    <NvMetricCard variant="icon" tone="success" :icon="CircleCheckIcon" label="一次合格率" value="98.6" unit="%" :trend="{ value: '0.4pt', direction: 'up' }" />
    <NvMetricCard variant="icon" tone="warning" :icon="ClockIcon" label="待派工" :value="9" :trend="{ value: '持平', direction: 'flat' }" />
    <NvMetricCard variant="icon" tone="danger" :icon="TriangleAlertIcon" label="超期工单" :value="6" :trend="{ value: '2', direction: 'up', tone: 'danger' }" />
  </div>
</Demo>

```vue
<NvMetricCard
  variant="icon"
  tone="danger"
  :icon="TriangleAlertIcon"
  label="超期工单"
  :value="6"
  :trend="{ value: '2', direction: 'up', tone: 'danger' }"
/>
```

> 「涨了但是坏事」（超期 +2）：`direction: 'up'` 保留向上箭头，`tone: 'danger'` 把 chip 染红。

## sparkline 趋势曲线型

曲线常驻，脚注固定为「区间 + 区间统计」两个数据槽位——没有自由文本入口。悬浮曲线出定位点 + tooltip。

<Demo>
  <div class="grid w-full gap-4 sm:grid-cols-2 xl:grid-cols-3">
    <NvMetricCard
      variant="sparkline"
      label="今日报工数量"
      value="1,284"
      unit="件"
      :trend="{ value: '8.2%', direction: 'up' }"
      :series="[1052, 1118, 1086, 1204, 1163, 1272, 1229, 1284]"
      :series-labels="['07-16','07-17','07-18','07-19','07-20','07-21','07-22','07-23']"
      series-unit=" 件"
      foot-start="近 7 日"
      foot-end="日均 1,176 件"
    />
    <NvMetricCard
      variant="sparkline"
      label="设备综合稼动率"
      value="76.8"
      unit="%"
      :trend="{ value: '3.1pt', direction: 'down' }"
      :series="[83.2, 81.1, 82.4, 79.5, 80.6, 77.9, 78.4, 76.8]"
      :series-labels="['07-16','07-17','07-18','07-19','07-20','07-21','07-22','07-23']"
      series-unit="%"
      foot-start="近 7 日"
      foot-end="峰值 83.2%"
    />
  </div>
</Demo>

```vue
<NvMetricCard
  variant="sparkline"
  label="今日报工数量"
  value="1,284"
  unit="件"
  :trend="{ value: '8.2%', direction: 'up' }"
  :series="[1052, 1118, 1086, 1204, 1163, 1272, 1229, 1284]"
  :series-labels="['07-16', ..., '07-23']"
  series-unit=" 件"
  foot-start="近 7 日"
  foot-end="日均 1,176 件"
/>
```

## target 目标进度型

`progress`（0–100，调用方算好保持可预期）驱动进度条；`target-marker` 是目标刻度位置（默认条末）；`progress-tone` 缺省在 ≥100% 时转 success。悬浮进度条出「实际 / 目标 / 达成」结构化 tooltip。

<Demo>
  <div class="grid w-full gap-4 sm:grid-cols-2 xl:grid-cols-3">
    <NvMetricCard variant="target" label="本月产量达成" value="13,847" unit="件" target-label="目标 15,000 件" :progress="92.3" foot-start="达成 92.3%" foot-end="缺口 1,153 件 · 剩 5 天" />
    <NvMetricCard variant="target" label="计划准时完工率" value="96.4" unit="%" target-label="目标 95%" :progress="96.4" :target-marker="95" progress-tone="success" foot-start="已达标" foot-end="高于目标 1.4pt" />
    <NvMetricCard variant="target" label="交付订单履约" value="18" unit="/ 26 单" target-label="本周应发 26 单" :progress="69.2" progress-tone="warning" foot-start="完成 69.2%" foot-end="8 单待发 · 2 单临期" />
  </div>
</Demo>

```vue
<NvMetricCard
  variant="target"
  label="本月产量达成"
  value="13,847"
  unit="件"
  target-label="目标 15,000 件"
  :progress="92.3"
  foot-start="达成 92.3%"
  foot-end="缺口 1,153 件 · 剩 5 天"
/>
```

## breakdown 状态分布型

总数 + 分段条 + 图例计数，一张卡回答「38 个工单都处在什么状态」。分段色沿用五 tone，和表格里的状态徽章同源；悬浮分段或图例项联动高亮。

<Demo>
  <div class="grid w-full gap-4 sm:grid-cols-2 xl:grid-cols-3">
    <NvMetricCard
      variant="breakdown"
      label="在制工单"
      :value="38"
      :segments="[
        { label: '进行中', value: 24, tone: 'brand' },
        { label: '待派工', value: 9, tone: 'neutral' },
        { label: '已暂停', value: 3, tone: 'warning' },
        { label: '超期', value: 2, tone: 'danger' },
      ]"
    />
    <NvMetricCard
      variant="breakdown"
      label="库存批次"
      :value="142"
      :segments="[
        { label: '正常', value: 118, tone: 'success' },
        { label: '临期', value: 16, tone: 'warning' },
        { label: '已过期', value: 8, tone: 'danger' },
      ]"
    />
  </div>
</Demo>

```vue
<NvMetricCard
  variant="breakdown"
  label="在制工单"
  :value="38"
  :segments="[
    { label: '进行中', value: 24, tone: 'brand' },
    { label: '待派工', value: 9, tone: 'neutral' },
    { label: '超期', value: 2, tone: 'danger' },
  ]"
/>
```

## bars 迷你柱状型

离散周期量（日产量、日报警数）用柱状比曲线更贴切。`current-index` 标出今日柱做强调，`bar-tones` 可给异常日单独染色。悬浮任一柱其余淡出。

<Demo>
  <div class="grid w-full gap-4 sm:grid-cols-2 xl:grid-cols-3">
    <NvMetricCard
      variant="bars"
      label="日产量"
      value="12,480"
      unit="件"
      :series="[7050, 8680, 6370, 9760, 8130, 11250, 12480]"
      :series-labels="['07-17','07-18','07-19','07-20','07-21','07-22','07-23（今日）']"
      series-unit=" 件"
      :current-index="6"
      :trend="{ value: '4.9%', direction: 'up' }"
      foot-start="07-17"
      foot-end="今日"
    />
    <NvMetricCard
      variant="bars"
      label="设备报警次数"
      value="14"
      unit="次"
      :series="[5, 4, 6, 5, 7, 6, 14]"
      :series-labels="['07-17','07-18','07-19','07-20','07-21','07-22','07-23（今日）']"
      series-unit=" 次"
      :current-index="6"
      :bar-tones="['neutral','neutral','neutral','neutral','neutral','neutral','danger']"
      :trend="{ value: '6', direction: 'up', tone: 'danger' }"
      foot-start="07-17"
      foot-end="今日"
    />
  </div>
</Demo>

## alert 异常行动型

整卡按 `tone` 极淡浸染 + tone 化数值 + 底部处理入口。`action` 有 `href` 时渲染链接，否则触发 `action` 事件（路由交给调用方）。正常态收敛为中性卡面。

<Demo>
  <div class="grid w-full gap-4 sm:grid-cols-2 xl:grid-cols-3">
    <NvMetricCard variant="alert" tone="danger" label="超期未完工" :value="6" unit="单" :status="{ label: '需处理', tone: 'danger' }" foot-start="最久已超期 3 天 · WO-2607-0214" :action="{ label: '去处理' }" />
    <NvMetricCard variant="alert" tone="warning" label="临期库存批次" :value="16" unit="批" :status="{ label: '7 天内', tone: 'warning' }" foot-start="涉及 5 个物料 · 最近 07-26 到效期" :action="{ label: '查看批次' }" />
    <NvMetricCard variant="alert" tone="success" label="未确认报警" :value="0" unit="条" :status="{ label: '正常', tone: 'success' }" foot-start="近 24h 已确认 14 条" :action="{ label: '报警记录' }" />
  </div>
</Demo>

```vue
<NvMetricCard
  variant="alert"
  tone="danger"
  label="超期未完工"
  :value="6"
  unit="单"
  :status="{ label: '需处理', tone: 'danger' }"
  foot-start="最久已超期 3 天 · WO-2607-0214"
  :action="{ label: '去处理' }"
  @action="goToOverdueList"
/>
```

## facets 维度拆解型

主数值 + 维度 chip 组，替代描述文本：下半区放「这 12 批都是什么」。异常维度自动 tone 化，chip 可点击抛出 `facet` 事件作筛选入口。

<Demo>
  <div class="grid w-full gap-4 sm:grid-cols-2 xl:grid-cols-3">
    <NvMetricCard
      variant="facets"
      label="待质检批次"
      :value="12"
      unit="批"
      :trend="{ value: '今日 +4', direction: 'flat' }"
      :facets="[
        { label: '原料', value: 5 },
        { label: '半成品', value: 4 },
        { label: '成品', value: 3 },
      ]"
    />
    <NvMetricCard
      variant="facets"
      label="在线设备"
      :value="28"
      unit="台"
      :trend="{ value: '共 32 台', direction: 'flat' }"
      :facets="[
        { label: '运行', value: 24 },
        { label: '待机', value: 4 },
        { label: '断线', value: 4, tone: 'danger' },
      ]"
    />
  </div>
</Demo>

## NvMetricRing 环形构成型

比率类主指标 + 构成因子并列：环里是结果，右侧是成因（OEE 的 A / P / Q、齐套率的各档）——主管一眼知道该去查哪个因子。悬浮环出完整构成 tooltip。

<Demo>
  <div class="grid w-full gap-4 sm:grid-cols-2 xl:grid-cols-3">
    <NvMetricRing
      label="OEE · 总装一线"
      value="82.4%"
      :percent="82.4"
      tone="brand"
      :factors="[
        { label: '可用率 A', value: '94.1%' },
        { label: '性能 P', value: '89.6%' },
        { label: '质量 Q', value: '97.8%' },
      ]"
    />
    <NvMetricRing
      label="齐套率 · 本周排产"
      value="96.0%"
      :percent="96"
      tone="success"
      :factors="[
        { label: '已齐套', value: '48 单' },
        { label: '部分齐套', value: '2 单' },
        { label: '缺料', value: '0 单' },
      ]"
    />
  </div>
</Demo>

```vue
<NvMetricRing
  label="OEE · 总装一线"
  value="82.4%"
  :percent="82.4"
  tone="brand"
  :factors="[
    { label: '可用率 A', value: '94.1%' },
    { label: '性能 P', value: '89.6%' },
    { label: '质量 Q', value: '97.8%' },
  ]"
/>
```

## NvMetricStrip 组合指标条

一张卡装一组同域指标，分隔线代替卡缝，密度最高。适合列表页顶部：不和下方表格抢注意力，又把关键口径钉在一行。窄屏自动竖排。

<Demo>
  <NvMetricStrip
    class="w-full"
    :cells="[
      { label: '今日产量', value: '12,480', unit: '件', meta: '4.9% 环比', metaTone: 'up' },
      { label: '报工工单', value: '26', unit: '单', meta: '待审核 3 单', metaTone: 'neutral' },
      { label: '一次合格率', value: '98.6', unit: '%', meta: '0.4pt', metaTone: 'up' },
      { label: '超期工单', value: '2', unit: '单', valueTone: 'danger', meta: '最久超期 3 天', metaTone: 'neutral' },
    ]"
  />
</Demo>

```vue
<NvMetricStrip
  :cells="[
    { label: '今日产量', value: '12,480', unit: '件', meta: '4.9% 环比', metaTone: 'up' },
    { label: '超期工单', value: '2', unit: '单', valueTone: 'danger', meta: '最久超期 3 天' },
  ]"
/>
```

## NvMetricCard 属性

| 属性                  | 说明                                           | 类型                                                                                             | 默认      |
| --------------------- | ---------------------------------------------- | ------------------------------------------------------------------------------------------------ | --------- |
| `variant`             | 下半区结构                                     | `'default' \| 'icon' \| 'sparkline' \| 'target' \| 'breakdown' \| 'bars' \| 'alert' \| 'facets'` | `default` |
| `label`               | 指标名                                         | `string`                                                                                         | —         |
| `value`               | 主数值                                         | `string \| number`                                                                               | —         |
| `unit`                | 数值单位后缀                                   | `string`                                                                                         | —         |
| `tone`                | `icon` 图标位 / `alert` 卡面强调 tone          | `'brand' \| 'success' \| 'warning' \| 'danger' \| 'neutral'`                                     | `brand`   |
| `icon`                | `icon` 变体的图标组件                          | `Component`                                                                                      | —         |
| `trend`               | 右上环比 chip `{ value, direction?, tone? }`   | `NvMetricDelta`                                                                                  | —         |
| `series`              | `sparkline` / `bars` 数据                      | `number[]`                                                                                       | —         |
| `seriesLabels`        | 数据点标签（tooltip）                          | `string[]`                                                                                       | —         |
| `seriesUnit`          | tooltip 数值单位                               | `string`                                                                                         | —         |
| `currentIndex`        | `bars` 强调柱下标                              | `number`                                                                                         | —         |
| `barTones`            | `bars` 每柱 tone                               | `NvMetricTone[]`                                                                                 | —         |
| `progress`            | `target` 进度 0–100                            | `number`                                                                                         | —         |
| `targetMarker`        | `target` 刻度位 0–100                          | `number`                                                                                         | `100`     |
| `targetLabel`         | `target` 右上目标文案                          | `string`                                                                                         | —         |
| `progressTone`        | `target` 进度条 tone                           | `'brand' \| 'success' \| 'warning' \| 'danger'`                                                  | 自动      |
| `segments`            | `breakdown` 分段 `{ label, value, tone? }[]`   | `NvMetricSegment[]`                                                                              | —         |
| `status`              | `alert` 状态 pill `{ label, tone }`            | `NvMetricStatus`                                                                                 | —         |
| `action`              | `alert` 底部动作 `{ label, href? }`            | `NvMetricAction`                                                                                 | —         |
| `facets`              | `facets` 维度 chip `{ label, value, tone? }[]` | `NvMetricFacet[]`                                                                                | —         |
| `footStart`/`footEnd` | `sparkline` / `target` / `bars` 结构化脚注     | `string`                                                                                         | —         |
| `hint`                | **已弃用**：自由描述文本，改用结构化变体       | `string`                                                                                         | —         |

事件：`@action`（alert 动作按钮，无 `href` 时）、`@facet`（点击维度 chip，回传该 facet）。

## NvMetricRing 属性

| 属性      | 说明       | 类型                                            | 默认    |
| --------- | ---------- | ----------------------------------------------- | ------- |
| `label`   | 指标名     | `string`                                        | —       |
| `value`   | 环心读数   | `string \| number`                              | —       |
| `percent` | 弧长 0–100 | `number`                                        | —       |
| `tone`    | 弧色       | `'brand' \| 'success' \| 'warning' \| 'danger'` | `brand` |
| `factors` | 构成因子行 | `{ label, value }[]`                            | `[]`    |

## NvMetricStrip 属性

| 属性    | 说明     | 类型                  | 默认 |
| ------- | -------- | --------------------- | ---- |
| `cells` | 各格指标 | `NvMetricStripCell[]` | `[]` |

`NvMetricStripCell`：`{ label, value, unit?, valueTone?, meta?, metaTone? }`；`metaTone` 取 `'up' \| 'down' \| 'flat' \| 'neutral'`，向上/下附趋势图标与语义色。
