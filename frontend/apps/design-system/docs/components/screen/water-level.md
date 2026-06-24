---
title: WaterLevel 水位球
---

<script setup>
import { WaterLevel, ScreenPanel } from '@nerv-iip/ui'
</script>

# WaterLevel 水位球

液位计:圆角容器内一汪青色液体,液面高度跟随 `value`(0–100),两道错位的波峰横向漂移。百分比读在中央。整数原样显示,小数保留一位。基于独立的 `--sb-*` 工业蓝令牌。减弱动效时波浪静止不漂。

::: tip 容器
`WaterLevel` 是「裸内容」组件,本身不带边框背景,通常放进 [`ScreenPanel`](./screen-panel) 容器里使用。
:::

## 基础用法

`value` 是液位,`label` 是球下方的可选标题。

<ScreenDemo>
  <ScreenPanel style="width: 200px">
    <WaterLevel :value="64" label="原料罐 #2" />
  </ScreenPanel>
</ScreenDemo>

```vue
<ScreenPanel>
  <WaterLevel :value="64" label="原料罐 #2" />
</ScreenPanel>
```

## 多液位对比

传入不同 `value` 看液面高度变化。

<ScreenDemo>
  <ScreenPanel style="width: 200px">
    <WaterLevel :value="86" label="冷却水箱" />
  </ScreenPanel>
  <ScreenPanel style="width: 200px">
    <WaterLevel :value="23.5" label="润滑油位" />
  </ScreenPanel>
</ScreenDemo>

```vue
<WaterLevel :value="86" label="冷却水箱" />
<WaterLevel :value="23.5" label="润滑油位" />
```

## 属性

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `value` | 液位,0–100 | `number` | — |
| `label` | 球下方标题,可选 | `string` | — |
