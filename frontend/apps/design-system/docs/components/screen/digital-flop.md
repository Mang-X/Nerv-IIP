---
title: NvDigitalFlop 数字翻牌
---

<script setup>
import { NvDigitalFlop, NvScreenPanel } from '@nerv-iip/ui'
</script>

# NvDigitalFlop 数字翻牌

数字翻牌计数器:每位数字独占一格暗色格子,带顶部高光与青色辉光字。传入数字会自动按千位分组,逗号 / 小数点 / 空格渲染成无框的细分隔格。也可直接传预格式化字符串。基于独立的 `--nv-scr-*` 工业蓝令牌。

::: tip 容器
`NvDigitalFlop` 是「裸内容」组件,本身不带边框背景,通常放进 [`NvScreenPanel`](./screen-panel) 容器里使用。
:::

## 基础用法

传 `number`,自动千位分组;`suffix` 是末尾小号单位。

<ScreenDemo>
  <NvScreenPanel title="今日产量" style="width: 320px">
    <NvDigitalFlop :value="1156" suffix="件" />
  </NvScreenPanel>
</ScreenDemo>

```vue
<NvScreenPanel title="今日产量">
  <NvDigitalFlop :value="1156" suffix="件" />
</NvScreenPanel>
```

## 大数与单位

不同量级的数据,千位分组自动加逗号格。

<ScreenDemo>
  <NvScreenPanel title="累计能耗" style="width: 360px">
    <NvDigitalFlop :value="284750" suffix="kWh" />
  </NvScreenPanel>
</ScreenDemo>

```vue
<NvDigitalFlop :value="284750" suffix="kWh" />
```

## 预格式化字符串

传 `string` 时原样逐字渲染,适合带小数或自定义分隔的读数。

<ScreenDemo>
  <NvScreenPanel title="当班节拍" style="width: 300px">
    <NvDigitalFlop value="45.8" suffix="s" />
  </NvScreenPanel>
</ScreenDemo>

```vue
<NvDigitalFlop value="45.8" suffix="s" />
```

## 属性

| 属性     | 说明                               | 类型               | 默认 |
| -------- | ---------------------------------- | ------------------ | ---- |
| `value`  | 数字(自动千位分组)或预格式化字符串 | `number \| string` | —    |
| `suffix` | 计数器后的小号单位                 | `string`           | —    |
