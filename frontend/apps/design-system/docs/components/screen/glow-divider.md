---
title: GlowDivider 辉光分割
---

<script setup>
import { NvGlowDivider } from '@nerv-iip/ui'
</script>

# GlowDivider 辉光分割

流动分割线:一条横向渐变发丝线,正中一颗亮青色光点;可选一道流光沿线滑过(系统开启「减弱动态效果」时自动停)。版块之间的安静分隔 —— 克制的辉光、单层。零必填属性。

## 基础用法

横向占满整列,默认带流光。常用来把页头与下方内容、或两个分区隔开。

<ScreenDemo wide>
  <div style="font-size:14px;color:var(--sb-text-2)">焊接线 A · 实时产出</div>
  <NvGlowDivider />
  <div style="font-size:13px;color:var(--sb-muted);margin-top:4px">当班 934 / 1 200 件 · 节拍 48.2 s · 截至 2024-06-12 10:24</div>
</ScreenDemo>

```vue
<div>焊接线 A · 实时产出</div>
<NvGlowDivider />
<div>当班 934 / 1 200 件 · 节拍 48.2 s</div>
```

## 静态分割

`flow` 设为 `false` 关掉流光,只留渐变线与中心光点 —— 适合密集排布、不想引入动态的场合。

<ScreenDemo wide>
  <NvGlowDivider :flow="false" />
</ScreenDemo>

```vue
<NvGlowDivider :flow="false" />
```

## 属性

| 属性   | 说明                                         | 类型      | 默认   |
| ------ | -------------------------------------------- | --------- | ------ |
| `flow` | 是否让一道流光沿线滑动(减弱动态效果时自动停) | `boolean` | `true` |
