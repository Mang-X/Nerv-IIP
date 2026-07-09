---
title: NvOeeHero 核心指标
---

<script setup>
import { NvOeeHero, NvScreenPanel } from '@nerv-iip/ui'
</script>

# NvOeeHero 核心指标

大屏头部的核心 KPI 块:青色辉光大数字 + 单位 + 同比变化(升绿降红),底部自带一条归一化 sparkline 面积。数据驱动 —— sparkline 按自身 min/max 归一,任意量纲的序列都能填满。基于独立的 `--nv-scr-*` 工业蓝令牌。

::: tip 容器
`NvOeeHero` 是「裸内容」组件,本身不带边框背景,通常放进 [`NvScreenPanel`](./screen-panel) 容器里使用。
:::

## 基础用法

`value` 是主数字,`delta` 字符串的前导 `+ / -` 决定升降配色。

<ScreenDemo>
  <NvScreenPanel style="width: 440px">
    <NvOeeHero label="设备综合效率 OEE" :value="92.4" unit="%" delta="较昨日 +2.7%" />
  </NvScreenPanel>
</ScreenDemo>

```vue
<NvScreenPanel>
  <NvOeeHero label="设备综合效率 OEE" :value="92.4" unit="%" delta="较昨日 +2.7%" />
</NvScreenPanel>
```

## 下降态

`delta` 带前导 `-` 时数字与变化文本转红,适合不良率、停机等"越低越好"的指标。

<ScreenDemo>
  <NvScreenPanel style="width: 440px">
    <NvOeeHero
      label="制程不良率 PPM"
      :value="318"
      delta="较昨日 -42"
      :spark="[78, 72, 80, 68, 60, 64, 52, 48, 40, 36, 31]"
    />
  </NvScreenPanel>
</ScreenDemo>

```vue
<NvOeeHero label="制程不良率 PPM" :value="318" delta="较昨日 -42" :spark="series" />
```

## 属性

| 属性    | 说明                                 | 类型               | 默认         |
| ------- | ------------------------------------ | ------------------ | ------------ |
| `label` | 数值上方的标题                       | `string`           | —            |
| `value` | 主数字                               | `number \| string` | —            |
| `unit`  | 数字后的小号单位                     | `string`           | —            |
| `delta` | 同比变化文本,前导 `+ / -` 决定升降色 | `string`           | —            |
| `spark` | 底部 sparkline 序列                  | `number[]`         | 内置示例序列 |
