---
title: Sparkline 迷你趋势
---

<script setup>
import { ref } from 'vue'
import { Sparkline, ScreenPanel } from '@nerv-iip/ui'

const yieldSeries = ref([8, 11, 9, 14, 12, 17, 15, 21, 19, 24, 22, 28])
const tempSeries = ref([62, 64, 63, 67, 71, 69, 73, 70, 68, 66, 64, 63])
</script>

# Sparkline 迷你趋势

极简迷你趋势线:一道辉光青色折线,按自身 min/max 归一化,可选线下渐变面积。`preserveAspectRatio="none"` 使其拉伸填满容器,任意量纲的序列都能塞进任意格子。基于独立的 `--sb-*` 工业蓝令牌。至少需要两个点。

::: tip 容器
`Sparkline` 是「裸内容」组件,会拉伸填满容器,通常放进 [`ScreenPanel`](./screen-panel) 并给定宽高使用。
:::

## 基础用法

`data` 是序列;线条按自身 min/max 归一,不必预先缩放。

<ScreenDemo>
  <ScreenPanel title="良品率趋势" style="width: 320px">
    <div style="height: 56px">
      <Sparkline :data="yieldSeries" />
    </div>
  </ScreenPanel>
</ScreenDemo>

```vue
<ScreenPanel title="良品率趋势">
  <div style="height: 56px">
    <Sparkline :data="series" />
  </div>
</ScreenPanel>
```

## 面积填充

`area` 为 `true` 时在线下铺一层渐隐渐变,趋势更醒目。

<ScreenDemo>
  <ScreenPanel title="主轴温度（℃）" style="width: 320px">
    <div style="height: 56px">
      <Sparkline :data="tempSeries" area />
    </div>
  </ScreenPanel>
</ScreenDemo>

```vue
<Sparkline :data="series" area />
```

## 属性

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `data` | 待绘序列,至少两个点 | `number[]` | 内置示例序列 |
| `area` | 线下铺渐隐面积 | `boolean` | `false` |
