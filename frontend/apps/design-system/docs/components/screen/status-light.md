---
title: StatusLight 状态灯
---

<script setup>
import { NvScreenStatusLight } from '@nerv-iip/ui'
</script>

# StatusLight 状态灯

一枚语义色呼吸辉光圆点,可选配文字。运行别名(`run` / `idle` / `alarm`)映射到调色板(绿 / 琥珀 / 红),也直接接受原始颜色名。状态不依赖颜色单独传达 —— 务必搭配 `label`。`prefers-reduced-motion` 下停止呼吸。

## 运行态别名

`run` / `idle` / `alarm` 对应运行、待机、报警三态。

<ScreenDemo>
  <div style="display:flex; gap:28px; align-items:center;">
    <NvScreenStatusLight tone="run" label="焊接线 A · 运行中" />
    <NvScreenStatusLight tone="idle" label="装配线 B · 待机" />
    <NvScreenStatusLight tone="alarm" label="CNC 线 C · 报警" />
  </div>
</ScreenDemo>

```vue
<NvScreenStatusLight tone="run" label="焊接线 A · 运行中" />
<NvScreenStatusLight tone="idle" label="装配线 B · 待机" />
<NvScreenStatusLight tone="alarm" label="CNC 线 C · 报警" />
```

## 原始颜色

也可直接用调色板颜色名,如 `cyan` 用于数据 / 中性提示。

<ScreenDemo>
  <div style="display:flex; gap:28px; align-items:center;">
    <NvScreenStatusLight tone="cyan" label="数据采集中" />
    <NvScreenStatusLight tone="green" label="正常" />
    <NvScreenStatusLight tone="amber" label="预警" />
    <NvScreenStatusLight tone="red" label="故障" />
  </div>
</ScreenDemo>

```vue
<NvScreenStatusLight tone="cyan" label="数据采集中" />
<NvScreenStatusLight tone="red" label="故障" />
```

## 属性

| 属性    | 说明                     | 类型                                                    | 默认  |
| ------- | ------------------------ | ------------------------------------------------------- | ----- |
| `tone`  | 运行别名或原始调色板颜色 | `run \| idle \| alarm \| cyan \| green \| amber \| red` | `run` |
| `label` | 圆点旁文字,如「运行中」  | `string`                                                | —     |
