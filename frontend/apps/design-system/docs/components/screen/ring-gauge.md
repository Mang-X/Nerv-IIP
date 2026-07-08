---
title: NvRingGauge 环形仪表
---

<script setup>
import { NvRingGauge, NvScreenPanel } from '@nerv-iip/ui'
</script>

# NvRingGauge 环形仪表

细弧环形进度:浅色轨道上一道青色辉光弧,从 12 点钟方向顺时针铺开,中心是数值与标题。弧长由 `value`(0–100)推导。整数原样显示,小数保留一位。基于独立的 `--sb-*` 工业蓝令牌。

::: tip 容器
`NvRingGauge` 是「裸内容」组件,本身不带边框背景,通常放进 [`NvScreenPanel`](./screen-panel) 容器里使用。
:::

## 基础用法

`value` 决定弧长,`label` 是环内标题。`suffix` 默认 `%`。

<ScreenDemo>
  <NvScreenPanel style="width: 220px">
    <NvRingGauge :value="78" label="设备稼动率" />
  </NvScreenPanel>
</ScreenDemo>

```vue
<NvScreenPanel>
  <NvRingGauge :value="78" label="设备稼动率" />
</NvScreenPanel>
```

## 多值对比

同一组件传入不同 `value` 看弧长变化:高位接近满环,低位仅一小段。`size` 控制外径。

<ScreenDemo>
  <NvScreenPanel style="width: 220px">
    <NvRingGauge :value="92.4" label="OEE 综合效率" />
  </NvScreenPanel>
  <NvScreenPanel style="width: 220px">
    <NvRingGauge :value="41" label="CNC 线 C 负荷" />
  </NvScreenPanel>
</ScreenDemo>

```vue
<NvRingGauge :value="92.4" label="OEE 综合效率" />
<NvRingGauge :value="41" label="CNC 线 C 负荷" />
```

## 自定义单位与尺寸

`suffix` 换成任意单位,`size` 调整外径(px)。

<ScreenDemo>
  <NvScreenPanel style="width: 240px">
    <NvRingGauge :value="45" label="节拍 Takt" suffix="s" :size="160" />
  </NvScreenPanel>
</ScreenDemo>

```vue
<NvRingGauge :value="45" label="节拍 Takt" suffix="s" :size="160" />
```

## 属性

| 属性     | 说明         | 类型     | 默认  |
| -------- | ------------ | -------- | ----- |
| `value`  | 进度值,0–100 | `number` | —     |
| `label`  | 数值下方标题 | `string` | —     |
| `suffix` | 数值后的单位 | `string` | `'%'` |
| `size`   | 外径(px)     | `number` | `140` |
