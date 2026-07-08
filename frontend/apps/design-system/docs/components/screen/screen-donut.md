---
title: ScreenDonut 环形占比
---

<script setup>
import { NvScreenDonut } from '@nerv-iip/ui'

const load = [
  { label: '排队', value: 38, color: 'rgba(160, 200, 245, 0.45)' },
  { label: '执行中', value: 29, color: '#4aa6ee' },
  { label: '失败', value: 4, color: '#ef5a63' },
]
const states = [
  { label: '运行', value: 52, color: '#45d089' },
  { label: '待机', value: 6, color: '#f2c14e' },
  { label: '停机', value: 3, color: 'rgba(200, 214, 235, 0.4)' },
  { label: '报警', value: 1, color: '#ef5a63' },
]
</script>

# ScreenDonut 环形占比

环形占比图——**状态构成 / 分布**类数据的正确形态:发丝底环 + 语义色弧段(段间留缝),中心 slot 放主数字,右侧内置图例(色点 + 名 + 值)。弧段 `dashoffset` 缓动(轮询更新平滑再分配),静态不发光。基于独立的 `--sb-*` 令牌。

## 基础用法

`segments` 传 `{ label, value, color }[]`,默认插槽放中心主数字。

<ScreenDemo>
  <NvScreenDonut :segments="load">
    <b style="font-size: 22px; font-weight: 800; color: var(--sb-text); font-variant-numeric: tabular-nums">532</b>
    <span style="margin-top: 4px; font-size: 11px; color: var(--sb-muted)">今日完成</span>
  </NvScreenDonut>
</ScreenDemo>

```vue
<template>
  <!-- 环 = 当前链路负载构成，中心 = 今日吞吐 -->
  <NvScreenDonut
    :segments="[
      { label: '排队', value: 38, color: 'rgba(160, 200, 245, 0.45)' },
      { label: '执行中', value: 29, color: '#4aa6ee' },
      { label: '失败', value: 4, color: '#ef5a63' },
    ]"
  >
    <b class="num">532</b>
    <span class="cap">今日完成</span>
  </NvScreenDonut>
</template>
```

## 设备状态分布

多段构成 + 自定义尺寸/厚度。

<ScreenDemo>
  <NvScreenDonut :segments="states" :size="132" :thickness="13">
    <b style="font-size: 24px; font-weight: 800; color: var(--sb-text)">62</b>
    <span style="margin-top: 4px; font-size: 11px; color: var(--sb-muted)">设备总数</span>
  </NvScreenDonut>
</ScreenDemo>

```vue
<NvScreenDonut :segments="deviceStates" :size="132" :thickness="13">
  <b class="num">62</b>
  <span class="cap">设备总数</span>
</NvScreenDonut>
```

## 关闭内置图例

`legend=false` 时只渲染环体,图例自行排版。

<ScreenDemo>
  <NvScreenDonut :segments="load" :legend="false" :size="96">
    <b style="font-size: 19px; font-weight: 800; color: var(--sb-text)">71</b>
  </NvScreenDonut>
</ScreenDemo>

```vue
<NvScreenDonut :segments="segments" :legend="false" :size="96" />
```

## API

| Prop        | 类型                        | 默认   | 说明                          |
| ----------- | --------------------------- | ------ | ----------------------------- |
| `segments`  | `{ label, value, color }[]` | —      | 占比段(value 为 0 的段不占缝) |
| `size`      | `number`                    | `118`  | 环直径(px)                    |
| `thickness` | `number`                    | `11`   | 环厚(px)                      |
| `legend`    | `boolean`                   | `true` | 右侧内置图例                  |
| 默认插槽    | —                           | —      | 环中心内容(主数字 + 说明)     |
