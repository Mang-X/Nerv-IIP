---
title: StatusCard 产线状态卡
---

<script setup>
import { NvScreenStatusCard } from '@nerv-iip/ui'

const lines = [
  {
    name: '焊接线 A', state: '运行', label: '运行中', tone: 'run',
    plan: '1,240', actual: '1,156', rate: '93.2%', downtime: '0 分钟',
  },
  {
    name: '装配线 B', state: '待机', label: '待机中', tone: 'idle',
    plan: '960', actual: '742', rate: '77.3%', downtime: '28 分钟',
  },
  {
    name: 'CNC 线 C', state: '报警', label: '主轴过载', tone: 'alarm',
    plan: '1,080', actual: '604', rate: '55.9%', downtime: '46 分钟',
  },
]
</script>

# StatusCard 产线状态卡

单条产线的状态卡:顶部彩色描边 + 呼吸状态灯,大号当前状态(按语义色着色),计划 / 实际 / 达成率三联值,底部一条停机时长。`tone` 同时驱动描边、状态灯与大字配色。组件自带 `NvScreenPanel` 容器,直接使用即可。

## 三态并排

`run` 绿 / `idle` 琥珀 / `alarm` 红,分别对应运行、待机、报警。

<ScreenDemo>
  <NvScreenStatusCard
    v-for="l in lines"
    :key="l.name"
    style="width: 280px"
    :name="l.name"
    :state="l.state"
    :label="l.label"
    :tone="l.tone"
    :plan="l.plan"
    :actual="l.actual"
    :rate="l.rate"
    :downtime="l.downtime"
  />
</ScreenDemo>

```vue
<NvScreenStatusCard
  name="焊接线 A"
  state="运行"
  label="运行中"
  tone="run"
  plan="1,240"
  actual="1,156"
  rate="93.2%"
  downtime="0 分钟"
/>
```

## 属性

| 属性       | 说明                                | 类型                   | 默认 |
| ---------- | ----------------------------------- | ---------------------- | ---- |
| `name`     | 产线名称,如「焊接线 A」             | `string`               | —    |
| `state`    | 名称后的短状态词,如「运行」         | `string`               | —    |
| `label`    | 大号状态文本,如「运行中」           | `string`               | —    |
| `tone`     | 语义色,驱动描边 / 状态灯 / 大字配色 | `run \| idle \| alarm` | —    |
| `plan`     | 计划产量(组件内补「件」单位)        | `string`               | —    |
| `actual`   | 实际产量(组件内补「件」单位)        | `string`               | —    |
| `rate`     | 达成率,如「93.2%」                  | `string`               | —    |
| `downtime` | 停机时长,如「28 分钟」              | `string`               | —    |
